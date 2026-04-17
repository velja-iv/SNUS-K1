using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProcessingSystem.Core.Interfaces;
using ProcessingSystem.Core.Models;
using ProcessingSystem.Infrastructure.Queue;
using ProcessingSystem.Infrastructure.Logging;
using ProcessingSystem.Infrastructure.Config;
using ProcessingSystem.Infrastructure.Execution;
using ProcessingSystem.Reporting;

namespace ProcessingSystem.Application
{
    public class ProcessingSystem : IProcessingSystem, IDisposable
    {
        private readonly PriorityJobQueue _queue;
        private readonly ReportService _reportService = new();

        private readonly SemaphoreSlim _items = new(0);
        private readonly object _sync = new();
        private readonly HashSet<Guid> _processed = new();
        private readonly Dictionary<Guid, TaskCompletionSource<int>> _pending = new();

        private readonly List<Task> _workers = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly int _workerCount;

        public event EventHandler<JobCompletedEventArgs>? JobCompleted;
        public event EventHandler<JobFailedEventArgs>? JobFailed;
        public event EventHandler<JobStartedEventArgs>? JobStarted;

        public ProcessingSystem(ProcessingConfig config)
        {
            _queue = new PriorityJobQueue(config.MaxQueueSize);
            _workerCount = Math.Max(1, config.NumberOfWorkers);

            // subscribe report service to events (run handlers asynchronously to avoid blocking workers)
            JobStarted += (s, e) => _ = Task.Run(() => _reportService.NoteJobStarted(e.JobId));
            JobCompleted += (s, e) => _ = Task.Run(() => _reportService.NoteJobFinished(e.JobId, e.Type, true, e.Result, e.Priority));
            JobFailed += (s, e) => _ = Task.Run(() => _reportService.NoteJobFinished(e.JobId, e.Type, false, null, e.Priority));

            // start worker tasks
            for (int i = 0; i < _workerCount; i++)
            {
                _workers.Add(Task.Run(() => WorkerLoop(_cts.Token)));
            }
        }

        public JobHandle Submit(Job job)
        {
            lock (_sync)
            {
                if (_processed.Contains(job.Id))
                {
                    return new JobHandle { Id = job.Id, Result = Task.FromResult(0) };
                }

                if (_pending.ContainsKey(job.Id))
                {
                    return new JobHandle { Id = job.Id, Result = _pending[job.Id].Task };
                }

                var enqueued = _queue.Enqueue(job);
                if (!enqueued)
                {
                    throw new InvalidOperationException("Queue full");
                }

                var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending[job.Id] = tcs;
                _items.Release();
                return new JobHandle { Id = job.Id, Result = tcs.Task };
            }
        }

        private async Task WorkerLoop(CancellationToken cancellationToken)
        {
            var primeExecutor = new PrimeExecutor();
            var ioExecutor = new IoExecutor();

            while (!cancellationToken.IsCancellationRequested)
            {
                Job? job = null;
                try
                {
                    await _items.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                lock (_sync)
                {
                    if (!_queue.TryDequeue(out job))
                        job = null;
                }

                if (job == null) continue;

                int attempt = 0;
                bool succeeded = false;
                while (attempt < 3 && !succeeded && !cancellationToken.IsCancellationRequested)
                {
                    attempt++;
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    linked.CancelAfter(TimeSpan.FromSeconds(2));
                    try
                    {
                        // notify subscribers that job started
                        JobStarted?.Invoke(this, new JobStartedEventArgs { JobId = job.Id, Type = job.Type });

                        int result = job.Type switch
                        {
                            JobType.Prime => await primeExecutor.ExecuteAsync(job, linked.Token),
                            JobType.IO => await ioExecutor.ExecuteAsync(job, linked.Token),
                            _ => 0
                        };

                        // on success
                        Console.WriteLine($"(\n\tJob {job.Id} \n\tcompleted with result {result} \n\ton attempt {attempt}.\n)");

                        CompleteJob(job.Id, job.Type, job.Priority, result);
                        succeeded = true;
                    }
                    catch (OperationCanceledException)
                    {
                        if (attempt >= 3)
                        {
                            Console.WriteLine($"(\nJob {job.Id} timed out after 3 attempts.\n)");
                            FailJob(job.Id, job.Type, job.Priority, "ABORT");
                        }
                    }
                    catch (Exception)
                    {
                        if (attempt >= 3)
                        {
                            Console.WriteLine($"(\nJob {job.Id} failed after 3 attempts.\n)");
                            FailJob(job.Id, job.Type, job.Priority, "ABORT");
                        }
                    }
                }
            }
        }

        public void CompleteJob(Guid id, JobType type, int priority, int result)
        {
            TaskCompletionSource<int>? tcs = null;
            lock (_sync)
            {
                if (_pending.TryGetValue(id, out tcs))
                {
                    _pending.Remove(id);
                    _processed.Add(id);
                }
            }

            if (tcs != null)
            {
                tcs.TrySetResult(result);
            }

            // mark completed in queue for idempotency
            try { _queue.MarkCompleted(id); } catch { }

            JobCompleted?.Invoke(this, new JobCompletedEventArgs { JobId = id, Result = result, Type = type, Priority = priority });
        }

        public void FailJob(Guid id, JobType type, int priority, string reason)
        {
            TaskCompletionSource<int>? tcs = null;
            lock (_sync)
            {
                if (_pending.TryGetValue(id, out tcs))
                {
                    _pending.Remove(id);
                    _processed.Add(id);
                }
            }

            if (tcs != null)
            {
                tcs.TrySetException(new InvalidOperationException(reason));
            }

            try { _queue.MarkCompleted(id); } catch { }

            JobFailed?.Invoke(this, new JobFailedEventArgs { JobId = id, Reason = reason, Type = type, Priority = priority });
        }

        public IEnumerable<Job> GetTopJobs(int n) => _queue.GetTopN(n);

        public Job GetJob(Guid id) => _queue.GetById(id) ?? throw new KeyNotFoundException();

        public void Dispose()
        {
            _cts.Cancel();
            try
            {
                Task.WaitAll(_workers.ToArray());
            }
            catch { }

            _items.Dispose();
            _cts.Dispose();
            try { _reportService.Dispose(); } catch { }
        }
    }
}
