using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SearchService.Services
{
    public interface ISearcherService
    {
        Task<IEnumerable<Guid>> SearchFilesAsync(string searchTerm, int? userIdFilter = null);
    }
}