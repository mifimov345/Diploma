using System;
using System.Threading.Tasks;

namespace SearchService.Services
{
    public interface IIndexService
    {
        Task IndexFileAsync(Guid fileId, int userId, string textContent);
        Task DeleteFileAsync(Guid fileId);
    }
}