using System;
using System.Collections.Generic;
using ProcessingSystem.Core.Models;

namespace ProcessingSystem.Core.Interfaces
{
    public interface IProcessingSystem : IDisposable
    {
        JobHandle Submit(Job job);
        IEnumerable<Job> GetTopJobs(int n);
        Job GetJob(Guid id);

        event EventHandler<JobCompletedEventArgs> JobCompleted;
        event EventHandler<JobFailedEventArgs> JobFailed;
    }
}
