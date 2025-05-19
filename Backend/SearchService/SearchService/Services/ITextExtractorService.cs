using System.IO;
using System.Threading.Tasks;

namespace SearchService.Services
{
    public interface ITextExtractorService
    {
        bool SupportsContentType(string? contentType, string? fileName);
        Task<string?> ExtractTextAsync(Stream fileStream, string? contentType, string? fileName);
    }
}