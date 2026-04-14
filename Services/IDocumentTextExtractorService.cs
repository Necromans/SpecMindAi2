using Microsoft.AspNetCore.Http;

namespace SpecMind.Services
{
    public interface IDocumentTextExtractorService
    {
        Task<string> ExtractTextAsync(IFormFile file);
    }
}