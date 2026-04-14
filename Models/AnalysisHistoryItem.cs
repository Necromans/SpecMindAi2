namespace SpecMind.Models
{
    public class AnalysisHistoryItem
    {
        public int Id { get; set; }

        public string FileName { get; set; } = "";
        public string SourceType { get; set; } = "";

        public int Score { get; set; }
        public int CompletenessPercent { get; set; }
        public int ClarityPercent { get; set; }

        public string Verdict { get; set; } = "";

        public string OriginalText { get; set; } = "";
        public string ImprovedText { get; set; } = "";

        public string Summary { get; set; } = "";
        public string Recommendation { get; set; } = "";

        public string CriteriaJson { get; set; } = "[]";
        public string ProblemsJson { get; set; } = "[]";
        public string ImprovementsJson { get; set; } = "[]";
        public string TemplateComplianceJson { get; set; } = "{}";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? SectionClassificationsJson { get; set; }
        public string? ExtractedRequirementsJson { get; set; }
        public string? ExtractedDeadlinesJson { get; set; }
        public string? ExtractedKpisJson { get; set; }
        public string? ExtractedExpectedResultsJson { get; set; }
    }
}