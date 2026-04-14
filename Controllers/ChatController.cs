using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpecMind.DataBase;
using SpecMind.Models;
using SpecMind.Services;
using SpecMind.Services.AI;
using System.Text;

namespace SpecMind.Controllers
{
    public class ChatController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IAiService _aiService;
        private readonly IDocumentTextExtractorService _documentTextExtractorService;

        public ChatController(
            ApplicationDbContext db,
            IAiService aiService,
            IDocumentTextExtractorService documentTextExtractorService)
        {
            _db = db;
            _aiService = aiService;
            _documentTextExtractorService = documentTextExtractorService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? historyId = null, int? conversationId = null)
        {
            if (conversationId.HasValue)
            {
                var existingConversation = await _db.ChatConversations
                    .Include(x => x.Messages.OrderBy(m => m.CreatedAt))
                    .FirstOrDefaultAsync(x => x.Id == conversationId.Value);

                if (existingConversation != null)
                {
                    var existingModel = await BuildChatViewModel(existingConversation);
                    return View(existingModel);
                }
            }

            var model = new ChatViewModel();

            if (historyId.HasValue)
            {
                var item = await _db.AnalysisHistoryItems.FirstOrDefaultAsync(x => x.Id == historyId.Value);
                if (item != null)
                {
                    model.HistoryId = item.Id;
                    model.ContextText = item.ImprovedText;
                    model.OriginalText = item.OriginalText;
                    model.UserMessage = BuildDraftFromHistory(item);

                    var conversation = new ChatConversation
                    {
                        AnalysisHistoryItemId = item.Id,
                        Title = $"Чат по анализу #{item.Id}",
                        ContextText = item.ImprovedText,
                        OriginalText = item.OriginalText,
                        DraftPrompt = model.UserMessage,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    conversation.Messages.Add(new ChatConversationMessage
                    {
                        Role = "assistant",
                        Content = "Загружен контекст из предыдущего анализа. Я уже вижу спорные места, рекомендации и проблемные блоки. Можем точечно доработать ТЗ.",
                        CreatedAt = DateTime.UtcNow
                    });

                    _db.ChatConversations.Add(conversation);
                    await _db.SaveChangesAsync();

                    return RedirectToAction(nameof(Index), new { conversationId = conversation.Id });
                }
            }

            model.Conversations = await LoadConversationListAsync();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(ChatViewModel model)
        {
            try
            {
                string uploadedText = "";

                if (model.UploadedFile != null && model.UploadedFile.Length > 0)
                {
                    uploadedText = await _documentTextExtractorService.ExtractTextAsync(model.UploadedFile);
                }

                ChatConversation? conversation = null;

                if (model.ConversationId.HasValue)
                {
                    conversation = await _db.ChatConversations
                        .Include(x => x.Messages.OrderBy(m => m.CreatedAt))
                        .FirstOrDefaultAsync(x => x.Id == model.ConversationId.Value);
                }

                if (conversation == null)
                {
                    conversation = new ChatConversation
                    {
                        AnalysisHistoryItemId = model.HistoryId,
                        Title = $"Новый чат {DateTime.Now:dd.MM.yyyy HH:mm}",
                        ContextText = model.ContextText ?? "",
                        OriginalText = model.OriginalText ?? "",
                        DraftPrompt = "",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    _db.ChatConversations.Add(conversation);
                    await _db.SaveChangesAsync();
                }

                var contextBuilder = new StringBuilder();

                if (!string.IsNullOrWhiteSpace(conversation.ContextText))
                {
                    contextBuilder.AppendLine("ТЕКУЩАЯ УЛУЧШЕННАЯ ВЕРСИЯ ТЗ:");
                    contextBuilder.AppendLine(conversation.ContextText);
                    contextBuilder.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(conversation.OriginalText))
                {
                    contextBuilder.AppendLine("ИСХОДНЫЙ ТЕКСТ:");
                    contextBuilder.AppendLine(conversation.OriginalText);
                    contextBuilder.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(uploadedText))
                {
                    contextBuilder.AppendLine("ДОПОЛНИТЕЛЬНО ЗАГРУЖЕННЫЙ ФАЙЛ:");
                    contextBuilder.AppendLine(uploadedText);
                    contextBuilder.AppendLine();

                    if (string.IsNullOrWhiteSpace(conversation.OriginalText))
                    {
                        conversation.OriginalText = uploadedText;
                    }
                }

                var userMessage = (model.UserMessage ?? "").Trim();

                if (!string.IsNullOrWhiteSpace(userMessage))
                {
                    conversation.Messages.Add(new ChatConversationMessage
                    {
                        Role = "user",
                        Content = userMessage,
                        CreatedAt = DateTime.UtcNow
                    });

                    var assistantReply = await _aiService.AnswerChatAsync(
                        userMessage,
                        contextBuilder.Length > 0 ? contextBuilder.ToString() : null
                    );

                    conversation.Messages.Add(new ChatConversationMessage
                    {
                        Role = "assistant",
                        Content = assistantReply,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                else if (!string.IsNullOrWhiteSpace(uploadedText))
                {
                    conversation.Messages.Add(new ChatConversationMessage
                    {
                        Role = "assistant",
                        Content = "Файл загружен и текст добавлен в контекст. Теперь можешь задать вопрос по нему.",
                        CreatedAt = DateTime.UtcNow
                    });
                }

                conversation.DraftPrompt = "";
                conversation.UpdatedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync();

                
                return RedirectToAction(nameof(Index), new { conversationId = conversation.Id });
            }
            catch (Exception ex)
            {
                model.Messages ??= new List<ChatMessageViewModel>();
                model.Messages.Add(new ChatMessageViewModel
                {
                    Role = "assistant",
                    Content = $"Ошибка: {ex.Message}"
                });

                model.Conversations = await LoadConversationListAsync();
                model.UserMessage = "";
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int conversationId)
        {
            var conversation = await _db.ChatConversations
                .Include(x => x.Messages)
                .FirstOrDefaultAsync(x => x.Id == conversationId);

            if (conversation != null)
            {
                _db.ChatConversationMessages.RemoveRange(conversation.Messages);
                _db.ChatConversations.Remove(conversation);
                await _db.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task<List<ChatConversationListItemViewModel>> LoadConversationListAsync()
        {
            return await _db.ChatConversations
                .OrderByDescending(x => x.UpdatedAt)
                .Take(30)
                .Select(x => new ChatConversationListItemViewModel
                {
                    Id = x.Id,
                    Title = x.Title,
                    UpdatedAt = x.UpdatedAt
                })
                .ToListAsync();
        }

        private string BuildDraftFromHistory(AnalysisHistoryItem item)
        {
            var lines = new List<string>
            {
                "Продолжим доработку ТЗ по результатам анализа.",
                "",
                "Нужно внимательно переработать спорные и слабые места."
            };

            if (!string.IsNullOrWhiteSpace(item.Recommendation))
            {
                lines.Add("");
                lines.Add("Рекомендации:");
                lines.Add(item.Recommendation);
            }

            if (!string.IsNullOrWhiteSpace(item.ImprovedText))
            {
                var flagged = item.ImprovedText
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Contains("требует подтверждения", StringComparison.OrdinalIgnoreCase)
                             || x.Contains("нужно уточнить", StringComparison.OrdinalIgnoreCase))
                    .Distinct()
                    .Take(20)
                    .ToList();

                if (flagged.Any())
                {
                    lines.Add("");
                    lines.Add("Уточни и доработай вот эти спорные места:");
                    foreach (var f in flagged)
                        lines.Add($"- {f}");
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        private async Task<ChatViewModel> BuildChatViewModel(ChatConversation conversation)
        {
            return new ChatViewModel
            {
                ConversationId = conversation.Id,
                HistoryId = conversation.AnalysisHistoryItemId,
                ContextText = conversation.ContextText,
                OriginalText = conversation.OriginalText,
                UserMessage = "",
                Conversations = await LoadConversationListAsync(),
                Messages = conversation.Messages
                    .OrderBy(x => x.CreatedAt)
                    .Select(x => new ChatMessageViewModel
                    {
                        Role = x.Role,
                        Content = x.Content
                    })
                    .ToList()
            };
        }
    }

    public class ChatViewModel
    {
        public int? ConversationId { get; set; }
        public int? HistoryId { get; set; }
        public string UserMessage { get; set; } = "";
        public string ContextText { get; set; } = "";
        public string OriginalText { get; set; } = "";
        public IFormFile? UploadedFile { get; set; }
        public List<ChatMessageViewModel> Messages { get; set; } = new();
        public List<ChatConversationListItemViewModel> Conversations { get; set; } = new();
    }

    public class ChatMessageViewModel
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
    }

    public class ChatConversationListItemViewModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public DateTime UpdatedAt { get; set; }
    }
}