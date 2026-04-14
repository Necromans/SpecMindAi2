namespace SpecMind.Models
{
    public class DocumentRecord
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }
        public User User { get; set; } = null!;

        public string OriginalFileName { get; set; } = string.Empty;
        public string StoredFileName { get; set; } = string.Empty;
        public string FileExtension { get; set; } = string.Empty;

        public string OriginalText { get; set; } = string.Empty;
        public string ImprovedText { get; set; } = string.Empty;

        public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;

        public AnalysisResult? AnalysisResult { get; set; }
    }
}