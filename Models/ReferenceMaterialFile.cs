namespace SpecMind.Models
{
    public class ReferenceMaterialFile
    {
        public int Id { get; set; }

        // template | example | criteria
        public string MaterialType { get; set; } = "";

        public string OriginalFileName { get; set; } = "";
        public string StoredFileName { get; set; } = "";
        public string RelativePath { get; set; } = "";

        public bool IsCustom { get; set; }
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }
}