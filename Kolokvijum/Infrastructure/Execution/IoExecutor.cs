using System;
using System.Threading;
using System.Threading.Tasks;
using ProcessingSystem.Core.Interfaces;
using ProcessingSystem.Core.Models;

namespace ProcessingSystem.Infrastructure.Execution
{
    public class IoExecutor : IJobExecutor
    {
        public async Task<int> ExecuteAsync(Job job, CancellationToken cancellationToken)
        {
            // Expected payload format: "delay:1_000"
            var payload = job.Payload ?? string.Empty;
            var parts = payload.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            int delayMs = 0;
            foreach (var part in parts)
            {
                var kv = part.Split(':', 2, StringSplitOptions.TrimEntries);
                if (kv.Length != 2) continue;
                var key = kv[0].ToLowerInvariant();
                var val = kv[1].Replace("_", string.Empty);
                if (key == "delay" && int.TryParse(val, out var d)) delayMs = d;
            }

            // Simulate blocking I/O as requested (but support cancellation)
            try
            {
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            // Return random value between 0 and 100 inclusive
            int value = Random.Shared.Next(0, 101);
            return value;
        }
    }
}
