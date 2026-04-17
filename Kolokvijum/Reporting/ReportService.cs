using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using ProcessingSystem.Core.Models;
using ProcessingSystem.Infrastructure.Logging;

namespace ProcessingSystem.Reporting
{
    public class ReportService : IDisposable
    {
        private readonly object _sync = new();

        public class TypeStats
        {
            public int FailedJobs { get; set; }
            public int SuccessJobs { get; set; }
            public TimeSpan AverageTime { get; set; }
        }

        // stats per job type
        private readonly Dictionary<JobType, TypeStats> _stats = new();

        // active map of job start times
        private readonly Dictionary<Guid, DateTime> _active = new();

        private readonly EventLogger _logger = new("../../../../events.xml");

        private readonly CancellationTokenSource _cts = new();
        private readonly Task _reportTask;

        private readonly string _reportsDir;

        public ReportService()
        {
            // initialize stats for all JobType values
            foreach (JobType t in Enum.GetValues(typeof(JobType)))
            {
                _stats[t] = new TypeStats { FailedJobs = 0, SuccessJobs = 0, AverageTime = TimeSpan.Zero };
            }

            // determine reports directory (solution folder /reports)
            var sol = FindSolutionDirectory() ?? AppContext.BaseDirectory;
            _reportsDir = Path.Combine(sol, "reports");
            Console.WriteLine($"ReportService: storing reports in {_reportsDir}");
            if (!Directory.Exists(_reportsDir)) Directory.CreateDirectory(_reportsDir);

            // start background report loop
            _reportTask = Task.Run(() => ReportLoopAsync(_cts.Token));
        }

        // Called when a worker starts processing a job
        public void NoteJobStarted(Guid jobId)
        {
            lock (_sync)
            {
                _active[jobId] = DateTime.UtcNow;
            }
        }

        // Called when a job finished; success indicates whether job succeeded.
        public void NoteJobFinished(Guid jobId, JobType jobType, bool success, int? result = null, int? priority = null)
        {
            DateTime start;
            lock (_sync)
            {
                if (!_active.TryGetValue(jobId, out start))
                {
                    // unknown start; ignore timing but still record failure/success counts
                    if (success)
                        _stats[jobType].SuccessJobs++;
                    else
                        _stats[jobType].FailedJobs++;
                }
                else
                {
                    _active.Remove(jobId);
                    var elapsed = DateTime.UtcNow - start;
                    if (success)
                    {
                        var ts = _stats[jobType];
                        // new average: (success_jobs * avg + new_time) / (success_jobs + 1)
                        var prevCount = ts.SuccessJobs;
                        var prevAvgMs = ts.AverageTime.TotalMilliseconds;
                        var newAvgMs = (prevCount * prevAvgMs + elapsed.TotalMilliseconds) / (prevCount + 1);
                        ts.SuccessJobs = prevCount + 1;
                        ts.AverageTime = TimeSpan.FromMilliseconds(newAvgMs);
                    }
                    else
                    {
                        _stats[jobType].FailedJobs++;
                    }
                }
            }

            // log event as XML via EventLogger (include priority if known)
            var status = success ? "COMPLETED" : "FAILED";
            try
            {
                _logger.LogEventAsync(status, jobId, result, priority).GetAwaiter().GetResult();
            }
            catch
            {
                // swallow logger exceptions
            }
        }

        // Expose a snapshot of stats for reporting
        public Dictionary<JobType, TypeStats> GetStatsSnapshot()
        {
            lock (_sync)
            {
                return _stats.ToDictionary(kv => kv.Key, kv => new TypeStats
                {
                    FailedJobs = kv.Value.FailedJobs,
                    SuccessJobs = kv.Value.SuccessJobs,
                    AverageTime = kv.Value.AverageTime
                });
            }
        }

        private async Task ReportLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), token).ConfigureAwait(false);
                    try
                    {
                        GenerateReport();
                    }
                    catch
                    {
                        // swallow report errors
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private void GenerateReport()
        {
            Dictionary<JobType, TypeStats> snapshot;
            lock (_sync)
            {
                snapshot = _stats.ToDictionary(kv => kv.Key, kv => new TypeStats
                {
                    FailedJobs = kv.Value.FailedJobs,
                    SuccessJobs = kv.Value.SuccessJobs,
                    AverageTime = kv.Value.AverageTime
                });
            }

            var doc = new XDocument(new XElement("Report",
                new XElement("GeneratedAt", DateTime.UtcNow.ToString("o")),
                new XElement("Types",
                    snapshot.Select(kv =>
                        new XElement("Type",
                            new XAttribute("Name", kv.Key.ToString()),
                            new XElement("Executed", kv.Value.SuccessJobs),
                            new XElement("AverageTimeMs", (long)kv.Value.AverageTime.TotalMilliseconds),
                            new XElement("Failed", kv.Value.FailedJobs)
                        )
                    )
                )
            ));

            // decide file to write: if less than 10 files, create new one; otherwise overwrite oldest
            var files = Directory.GetFiles(_reportsDir, "report_*.xml");
            string path;
            if (files.Length < 10)
            {
                path = Path.Combine(_reportsDir, $"report_{DateTime.UtcNow:yyyyMMddHHmmssfff}.xml");
            }
            else
            {
                // overwrite oldest
                var oldest = files.OrderBy(f => File.GetCreationTimeUtc(f)).First();
                path = oldest;
            }

            // save
            doc.Save(path);
        }

        private static string? FindSolutionDirectory()
        {
            try
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    if (dir.GetFiles("*.sln").Any() || dir.GetFiles("*.slnx").Any())
                        return dir.FullName;
                    dir = dir.Parent;
                }
            }
            catch { }
            return null;
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _reportTask?.Wait(); } catch { }
            _cts.Dispose();
        }
    }
}
