using Microsoft.AspNetCore.Http;
using SpecMind.Models;

namespace SpecMind.Services.ReferenceMaterials
{
    public interface IReferenceMaterialService
    {
        Task<List<ReferenceMaterialViewModel>> GetMaterialsAsync();
        Task SaveCustomMaterialAsync(string materialType, IFormFile file);
        Task ResetMaterialAsync(string materialType);
        Task<(string title, string html)> GetPreviewAsync(string materialType);

        Task<string> GetTemplateTextAsync();
        Task<string> GetExampleTextAsync();
        Task<string> GetCriteriaTextAsync();
    }
}