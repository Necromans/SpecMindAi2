namespace SpecMind.Models
{
    public class AnalysisResult
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid DocumentRecordId { get; set; }
        public DocumentRecord DocumentRecord { get; set; } = null!;

        public int TotalScore { get; set; }
        public int CompletenessPercent { get; set; }
        public int ClarityPercent { get; set; }

        public string RiskLevel { get; set; } = string.Empty;
        public string MainProblem { get; set; } = string.Empty;
        public string AiRecommendation { get; set; } = string.Empty;

        public string ImprovementsJson { get; set; } = "[]";

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}