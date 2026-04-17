using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProcessingSystem.Application;
using ProcessingSystem.Core.Models;
using ProcessingSystem.Infrastructure.Config;
using Processing = ProcessingSystem.Application.ProcessingSystem;

class Program
{
    static async Task Main(string[] args)
    {
        var config = XmlConfigLoader.Load("config.xml");
        using var system = new Processing(config);

        // ProcessingSystem starts its own worker tasks and performs logging.

        // Submit initial jobs from config
        foreach (var j in config.InitialJobs)
        {
            // expected format: Type|Payload|Priority
            var parts = j.Split('|', 3);
            var typeStr = parts.Length > 0 ? parts[0] : string.Empty;
            var payload = parts.Length > 1 ? parts[1] : string.Empty;
            var priorityStr = parts.Length > 2 ? parts[2] : "100";

            if (!Enum.TryParse<JobType>(typeStr, true, out var type))
                type = JobType.IO;
            if (!int.TryParse(priorityStr, out var priority))
                priority = 100;

            var job = new Job { Id = Guid.NewGuid(), Type = type, Payload = payload, Priority = priority };

            // try submit with a few retries if queue is full
            int submitAttempts = 0;
            while (true)
            {
                submitAttempts++;
                try
                {
                    system.Submit(job);
                    break;
                }
                catch (InvalidOperationException)
                {
                    if (submitAttempts >= 3) throw;
                    await Task.Delay(100);
                }
            }
        }

        // Start producer threads: these create random jobs and submit them.
        // Number of producers comes from configuration (specification).
        var cts = new CancellationTokenSource();
        var producers = new List<Task>();
        var rnd = new Random();

        for (int i = 0; i < Math.Max(1, config.NumberOfWorkers); i++)
        {
            producers.Add(Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    // create a random job
                    var isPrime = rnd.Next(0, 2) == 0;
                    Job job;
                    if (isPrime)
                    {
                        int numbers = rnd.Next(1000, 20000);
                        int threads = rnd.Next(1, 9);
                        var payload = $"numbers:{numbers.ToString()}";
                        // ensure underscores in thousands
                        payload = $"numbers:{numbers.ToString("N0").Replace(",", "_")},threads:{threads}";
                        job = new Job { Id = Guid.NewGuid(), Type = JobType.Prime, Payload = payload, Priority = rnd.Next(1, 5) };
                    }
                    else
                    {
                        int delay = rnd.Next(100, 5000);
                        var payload = $"delay:{delay.ToString().Replace(",", "_")}";
                        job = new Job { Id = Guid.NewGuid(), Type = JobType.IO, Payload = payload, Priority = rnd.Next(1, 5) };
                    }
                    Console.WriteLine($"(\n\tProducer created job {job.Id} \n\tof type {job.Type} \n\twith payload: {job.Payload}\n)");

                    int submitAttempts = 0;
                    while (!cts.Token.IsCancellationRequested)
                    {
                        submitAttempts++;
                        try
                        {
                            system.Submit(job);
                            break;
                        }
                        catch (InvalidOperationException)
                        {
                            if (submitAttempts >= 3) break;
                            await Task.Delay(200, cts.Token).ConfigureAwait(false);
                        }
                    }

                    // wait a bit before creating next job
                    try { await Task.Delay(rnd.Next(100, 1000), cts.Token).ConfigureAwait(false); } catch { break; }
                }
            }, cts.Token));
        }

        Console.WriteLine("Processing system started. Press Enter to stop.");
        Console.ReadLine();

        cts.Cancel();
        await Task.WhenAll(producers);
    }

    // Workers are started and managed by ProcessingSystem; producers are in Main.
}
