using System;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ProcessingSystem.Infrastructure.Logging
{
    public class EventLogger
    {
        private readonly string _path;

        public EventLogger(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) path = "events.xml";
            // If relative path provided, store under application base directory so file is easy to find
            if (!Path.IsPathRooted(path))
            {
                _path = Path.Combine(AppContext.BaseDirectory, path);
            }
            else
            {
                _path = path;
            }

            // ensure directory exists
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        private readonly object _fileLock = new();

        /// <summary>
        /// Log an event as XML. Creates the file with root &lt;Events&gt; if missing and appends an &lt;Event&gt; node.
        /// </summary>
        public Task LogEventAsync(string status, Guid jobId, int? result = null, int? priority = null)
        {
            lock (_fileLock)
            {
                XDocument doc;
                if (File.Exists(_path))
                {
                    try
                    {
                        doc = XDocument.Load(_path);
                    }
                    catch
                    {
                        // if file is corrupted, start fresh
                        doc = new XDocument(new XElement("Events"));
                    }
                }
                else
                {
                    doc = new XDocument(new XElement("Events"));
                }

                var ev = new XElement("Event",
                    new XElement("DateTime", DateTime.UtcNow.ToString("o")),
                    new XElement("Status", status),
                    new XElement("JobId", jobId.ToString())
                );

                if (priority.HasValue)
                {
                    ev.Add(new XElement("Priority", priority.Value));
                }

                if (result.HasValue)
                {
                    ev.Add(new XElement("Result", result.Value));
                }

                doc.Root!.Add(ev);

                // write out (synchronously inside lock so file is flushed predictably)
                try
                {
                    var xml = doc.ToString();
                    File.WriteAllText(_path, xml);
                }
                catch
                {
                    // swallow exceptions to avoid crashing background workers; caller may log elsewhere
                }

                return Task.CompletedTask;
            }
        }
    }
}
