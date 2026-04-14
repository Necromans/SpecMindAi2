namespace SpecMind.Models
{
    public class ChatMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ChatSessionId { get; set; }
        public ChatSession ChatSession { get; set; } = null!;

        public string Role { get; set; } = "user"; // user / bot / system
        public string Content { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}