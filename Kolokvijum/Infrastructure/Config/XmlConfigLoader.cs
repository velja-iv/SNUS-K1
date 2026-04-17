using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace ProcessingSystem.Infrastructure.Config
{
    public class ProcessingConfig
    {
        public int NumberOfWorkers { get; set; } = 4;
        public int MaxQueueSize { get; set; } = 100;

        public List<string> InitialJobs { get; set; } = new();
    }
    public class XmlConfigLoader
    {
        public static ProcessingConfig Load(string path)
        {
            // If file doesn't exist return defaults
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new ProcessingConfig();

            var config = new ProcessingConfig();

            var doc = XDocument.Load(path);
            var root = doc.Root;
            if (root == null) return config;

            var workerEl = root.Element("WorkerCount");
            if (workerEl != null && int.TryParse(workerEl.Value.Trim(), out var workers))
            {
                config.NumberOfWorkers = workers;
            }

            var maxEl = root.Element("MaxQueueSize");
            if (maxEl != null && int.TryParse(maxEl.Value.Trim(), out var max))
            {
                config.MaxQueueSize = max;
            }

            var jobsEl = root.Element("Jobs");
            if (jobsEl != null)
            {
                foreach (var jobEl in jobsEl.Elements("Job"))
                {
                    var type = jobEl.Attribute("Type")?.Value ?? string.Empty;
                    var payload = jobEl.Attribute("Payload")?.Value ?? string.Empty;
                    var priority = jobEl.Attribute("Priority")?.Value ?? string.Empty;

                    // Store a compact string representation: Type|Payload|Priority
                    config.InitialJobs.Add($"{type}|{payload}|{priority}");
                }
            }

            return config;
        }
    }
}
