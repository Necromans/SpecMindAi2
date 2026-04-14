namespace SpecMind.Models
{
    public class ChatConversationMessage
    {
        public int Id { get; set; }
        public int ChatConversationId { get; set; }

        public string Role { get; set; } = "";
        public string Content { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ChatConversation? ChatConversation { get; set; }
    }
}