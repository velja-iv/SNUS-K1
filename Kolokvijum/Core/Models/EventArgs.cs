using System;

namespace ProcessingSystem.Core.Models
{
    public class JobCompletedEventArgs : EventArgs
    {
        public Guid JobId { get; init; }
        public int Result { get; init; }
        public JobType Type { get; init; }
        public int Priority { get; init; }
    }

    public class JobFailedEventArgs : EventArgs
    {
        public Guid JobId { get; init; }
        public string Reason { get; init; } = string.Empty;
        public JobType Type { get; init; }
        public int Priority { get; init; }
    }

    public class JobStartedEventArgs : EventArgs
    {
        public Guid JobId { get; init; }
        public JobType Type { get; init; }
        public int Priority { get; init; }
    }
}
