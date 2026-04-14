using Microsoft.AspNetCore.Http;

namespace SpecMind.Models
{
    public class HomeIndexViewModel
    {
        public IFormFile? UploadedFile { get; set; }
        public string? ManualText { get; set; }

        public string AnalysisMode { get; set; } = "fast";
        public bool IsUploadPanelCollapsed { get; set; }

        public string MainProblem { get; set; } = "";
        public string AiRecommendation { get; set; } = "";

        public List<string> Improvements { get; set; } = new();
        public List<string> Problems { get; set; } = new();

        public int TotalScore { get; set; }
        public int CompletenessPercent { get; set; }
        public int ClarityPercent { get; set; }
        public string RiskLevel { get; set; } = "";

        public string UploadedTextPreview { get; set; } = "";
        public string OriginalTzText { get; set; } = "";
        public string ImprovedTzText { get; set; } = "";

        public List<CriterionResult> CriteriaResults { get; set; } = new();
        public TemplateComplianceResult TemplateCompliance { get; set; } = new();

        public string DownloadFileName { get; set; } = "";

        public List<ReferenceMaterialViewModel> ReferenceMaterials { get; set; } = new();
        public List<AnalysisHistoryViewModel> AnalysisHistory { get; set; } = new();

        public int? CurrentHistoryId { get; set; }
        public string ChatDraftPrompt { get; set; } = "";

        public List<SectionClassificationResult> SectionClassifications { get; set; } = new();
        public List<string> ExtractedRequirements { get; set; } = new();
        public List<string> ExtractedDeadlines { get; set; } = new();
        public List<string> ExtractedKpis { get; set; } = new();
        public List<string> ExtractedExpectedResults { get; set; } = new();
    }

    public class ReferenceMaterialViewModel
    {
        public string MaterialType { get; set; } = "";
        public string Title { get; set; } = "";
        public bool IsCustom { get; set; }
        public string StatusText => IsCustom ? "кастомный" : "стандарт";
        public string DownloadUrl { get; set; } = "#";
        public string PreviewUrl { get; set; } = "#";
        public string UploadAction { get; set; } = "";
        public string ResetAction { get; set; } = "";
    }

    public class AnalysisHistoryViewModel
    {
        public int Id { get; set; }
        public string FileName { get; set; } = "";
        public string SourceType { get; set; } = "";
        public int Score { get; set; }
        public int CompletenessPercent { get; set; }
        public int ClarityPercent { get; set; }
        public string Verdict { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }
}