using System;
using System.Threading.Tasks;

namespace ProcessingSystem.Core.Models
{
    public class JobHandle
    {
        public Guid Id { get; set; }
        public Task<int> Result { get; set; } = Task.FromResult(0);
    }
}
