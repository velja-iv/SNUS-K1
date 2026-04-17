using System.Threading;
using System.Threading.Tasks;
using ProcessingSystem.Core.Models;

namespace ProcessingSystem.Core.Interfaces
{
    public interface IJobExecutor
    {
        Task<int> ExecuteAsync(Job job, CancellationToken cancellationToken);
    }
}
