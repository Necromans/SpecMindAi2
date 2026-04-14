using SpecMind.Models;

namespace SpecMind.Services.AI
{
    public interface IAiService
    {
        Task<AiAnalysisResult> AnalyzeTextAsync(string text, string analysisMode = "fast");
        Task<string> AnswerChatAsync(string userMessage, string? contextText = null);
    }
}