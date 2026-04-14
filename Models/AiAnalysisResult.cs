namespace SpecMind.Models
{
    public class AiAnalysisResult
    {
        public string DocumentTitle { get; set; } = "";
        public string Organization { get; set; } = "";
        public string Expert { get; set; } = "";
        public int OverallScore { get; set; }
        public string OverallVerdict { get; set; } = "";
        public string Summary { get; set; } = "";

        public TemplateComplianceResult TemplateCompliance { get; set; } = new();
        public List<CriterionResult> Criteria { get; set; } = new();
        public List<string> Problems { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();

        public string OriginalText { get; set; } = "";
        public string ImprovedText { get; set; } = "";

        public List<SectionClassificationResult> SectionClassifications { get; set; } = new();
        public List<string> ExtractedRequirements { get; set; } = new();
        public List<string> ExtractedDeadlines { get; set; } = new();
        public List<string> ExtractedKpis { get; set; } = new();
        public List<string> ExtractedExpectedResults { get; set; } = new();
    }

    public class CriterionResult
    {
        public string Name { get; set; } = "";
        public int Weight { get; set; }
        public int Score { get; set; }
        public int MaxScore { get; set; }
        public bool Passed { get; set; }
        public string Comment { get; set; } = "";
    }

    public class TemplateComplianceResult
    {
        public bool MatchesTemplate { get; set; }
        public List<string> MissingSections { get; set; } = new();
        public string StructureComment { get; set; } = "";
    }

    public class SectionClassificationResult
    {
        public string SectionName { get; set; } = "";
        public string Category { get; set; } = "";
        public string Comment { get; set; } = "";
    }
}