using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProcessingSystem.Core.Interfaces;
using ProcessingSystem.Core.Models;

namespace ProcessingSystem.Infrastructure.Execution
{
    public class PrimeExecutor : IJobExecutor
    {
        public Task<int> ExecuteAsync(Job job, CancellationToken cancellationToken)
        {
            // Expected payload format: "numbers:10_000,threads:3"
            var payload = job.Payload ?? string.Empty;
            var parts = payload.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            int numbers = 0;
            int threads = 1;

            foreach (var part in parts)
            {
                var kv = part.Split(':', 2, StringSplitOptions.TrimEntries);
                if (kv.Length != 2) continue;
                var key = kv[0].ToLowerInvariant();
                var val = kv[1].Replace("_", string.Empty);
                if (key == "numbers" && int.TryParse(val, out var n)) numbers = n;
                if (key == "threads" && int.TryParse(val, out var t)) threads = t;
            }

            // clamp threads to [1,8]
            threads = Math.Max(1, Math.Min(8, threads));

            if (numbers < 2) return Task.FromResult(0);

            return Task.Run(() => CountPrimes(numbers, threads, cancellationToken), cancellationToken);
        }

        private static int CountPrimes(int maxNumber, int threads, CancellationToken cancellationToken)
        {
            // simple parallel range partitioning
            var tasks = new Task<int>[threads];

            int rangeStart = 2;
            int totalNumbers = Math.Max(0, maxNumber - rangeStart + 1);
            int chunk = totalNumbers / threads;
            int remainder = totalNumbers % threads;

            int currentStart = rangeStart;
            for (int i = 0; i < threads; i++)
            {
                int thisChunk = chunk + (i < remainder ? 1 : 0);
                int start = currentStart;
                int end = (thisChunk > 0) ? (start + thisChunk - 1) : (start - 1);
                currentStart = end + 1;

                tasks[i] = Task.Run(() =>
                {
                    int localCount = 0;
                    for (int n = start; n <= end; n++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (IsPrime(n)) localCount++;
                    }
                    return localCount;
                }, cancellationToken);
            }

            try
            {
                Task.WaitAll(tasks, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            return tasks.Sum(t => t.Result);
        }

        private static bool IsPrime(int n)
        {
            if (n <= 1) return false;
            if (n <= 3) return true;
            if ((n & 1) == 0) return n == 2;

            int r = (int)Math.Sqrt(n);
            for (int i = 3; i <= r; i += 2)
            {
                if (n % i == 0) return false;
            }
            return true;
        }
    }
}
