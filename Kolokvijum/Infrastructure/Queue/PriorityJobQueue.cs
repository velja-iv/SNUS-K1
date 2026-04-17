using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ProcessingSystem.Core.Models;

namespace ProcessingSystem.Infrastructure.Queue
{
    public class PriorityJobQueue
    {
        private readonly SortedDictionary<int, Queue<Job>> _queues = new();
        private readonly ReaderWriterLockSlim _lock = new();
        private int _count;
        private int _maxQueueSize = 100;
        private readonly HashSet<Guid> _completed = new();

        public PriorityJobQueue(int maxQueueSize)
        {
            _maxQueueSize = maxQueueSize;
        }

        public int Count
        {
            get
            {
                _lock.EnterReadLock();
                try { return _count; }
                finally { _lock.ExitReadLock(); }
            }
        }

        public bool Enqueue(Job job)
        {
            _lock.EnterWriteLock();
            try
            {
                if (_completed.Contains(job.Id))
                    // already completed, don't enqueue
                    return false;

                if (_count >= _maxQueueSize)
                    // queue full
                    return false;
                if (!_queues.TryGetValue(job.Priority, out var q))
                {
                    q = new Queue<Job>();
                    _queues[job.Priority] = q;
                }
                q.Enqueue(job);
                _count++;
                return true;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public void MarkCompleted(Guid id)
        {
            _lock.EnterWriteLock();
            try
            {
                _completed.Add(id);
            }
            finally { _lock.ExitWriteLock(); }
        }

        public bool TryDequeue(out Job? job)
        {
            _lock.EnterWriteLock();
            try
            {
                foreach (var key in _queues.Keys.OrderBy(k => k))
                {
                    var q = _queues[key];
                    if (q.Count > 0)
                    {
                        job = q.Dequeue();
                        _count--;
                        return true;
                    }
                }
                job = null;
                return false;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public IEnumerable<Job> GetTopN(int n)
        {
            _lock.EnterReadLock();
            try
            {
                return _queues.Keys.OrderBy(k => k)
                    .SelectMany(k => _queues[k])
                    .Take(n)
                    .ToList();
            }
            finally { _lock.ExitReadLock(); }
        }

        public Job? GetById(Guid id)
        {
            _lock.EnterReadLock();
            try
            {
                return _queues.Values.SelectMany(q => q).FirstOrDefault(j => j.Id == id);
            }
            finally { _lock.ExitReadLock(); }
        }
    }
}
