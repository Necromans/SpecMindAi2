namespace SpecMind.Models
{
    public class ChatConversation
    {
        public int Id { get; set; }
        public int? AnalysisHistoryItemId { get; set; }

        public string Title { get; set; } = "";
        public string ContextText { get; set; } = "";
        public string OriginalText { get; set; } = "";
        public string DraftPrompt { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public List<ChatConversationMessage> Messages { get; set; } = new();
    }
}