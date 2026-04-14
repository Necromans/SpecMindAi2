using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpecMind.DataBase;
using SpecMind.Models;
using SpecMind.Services;
using SpecMind.Services.AI;
using SpecMind.Services.ReferenceMaterials;
using System.Text.Json;

namespace SpecMind.Controllers
{
    public class HomeController : Controller
    {
        private readonly IWebHostEnvironment _environment;
        private readonly IDocumentTextExtractorService _documentTextExtractorService;
        private readonly IAiService _aiService;
        private readonly IDocumentExportService _documentExportService;
        private readonly IReferenceMaterialService _referenceMaterialService;
        private readonly ApplicationDbContext _db;

        public HomeController(
            IWebHostEnvironment environment,
            IDocumentTextExtractorService documentTextExtractorService,
            IAiService aiService,
            IDocumentExportService documentExportService,
            IReferenceMaterialService referenceMaterialService,
            ApplicationDbContext db)
        {
            _environment = environment;
            _documentTextExtractorService = documentTextExtractorService;
            _aiService = aiService;
            _documentExportService = documentExportService;
            _referenceMaterialService = referenceMaterialService;
            _db = db;
        }

        [HttpGet]
        public IActionResult Landing()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? historyId = null)
        {
            var model = await BuildDefaultViewModelAsync();

            if (historyId.HasValue)
            {
                var item = await _db.AnalysisHistoryItems.FirstOrDefaultAsync(x => x.Id == historyId.Value);
                if (item != null)
                {
                    ApplyHistoryToModel(model, item);
                    model.IsUploadPanelCollapsed = true;
                }
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(HomeIndexViewModel model)
        {
            try
            {
                ModelState.Remove(nameof(HomeIndexViewModel.ManualText));

                string extractedText;
                string sourceType;
                string fileName;

                if (model.UploadedFile != null && model.UploadedFile.Length > 0)
                {
                    var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads");
                    Directory.CreateDirectory(uploadsFolder);

                    var safeFileName = $"{Guid.NewGuid()}_{Path.GetFileName(model.UploadedFile.FileName)}";
                    var filePath = Path.Combine(uploadsFolder, safeFileName);

                    await using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await model.UploadedFile.CopyToAsync(stream);
                    }

                    extractedText = await _documentTextExtractorService.ExtractTextAsync(model.UploadedFile);
                    sourceType = "file";
                    fileName = model.UploadedFile.FileName;
                }
                else if (!string.IsNullOrWhiteSpace(model.ManualText))
                {
                    extractedText = model.ManualText.Trim();
                    sourceType = "manual";
                    fileName = $"manual_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                }
                else
                {
                    model = await BuildDefaultViewModelAsync(model);
                    ModelState.AddModelError("", "Загрузите файл или вставьте текст.");
                    return View(model);
                }

                model = await BuildDefaultViewModelAsync(model);

                model.UploadedTextPreview = extractedText;
                model.OriginalTzText = extractedText;
                model.ImprovedTzText = "";
                model.MainProblem = "Текст успешно извлечён.";
                model.AiRecommendation = "Документ отправлен на AI-анализ.";
                model.DownloadFileName = $"Improved_TZ_{DateTime.Now:yyyyMMdd_HHmm}";
                model.IsUploadPanelCollapsed = true;

                var aiResult = await _aiService.AnalyzeTextAsync(extractedText);

                model.MainProblem = string.IsNullOrWhiteSpace(aiResult.Summary)
                    ? "Анализ завершён."
                    : aiResult.Summary;

                model.AiRecommendation = aiResult.Recommendations.Any()
                    ? string.Join(" ", aiResult.Recommendations)
                    : "Рекомендации не найдены.";

                model.Improvements = aiResult.Recommendations ?? new List<string>();
                model.Problems = aiResult.Problems ?? new List<string>();
                model.CriteriaResults = aiResult.Criteria ?? new List<CriterionResult>();
                model.TemplateCompliance = aiResult.TemplateCompliance ?? new TemplateComplianceResult();

                model.SectionClassifications = aiResult.SectionClassifications ?? new List<SectionClassificationResult>();
                model.ExtractedRequirements = aiResult.ExtractedRequirements ?? new List<string>();
                model.ExtractedDeadlines = aiResult.ExtractedDeadlines ?? new List<string>();
                model.ExtractedKpis = aiResult.ExtractedKpis ?? new List<string>();
                model.ExtractedExpectedResults = aiResult.ExtractedExpectedResults ?? new List<string>();

                model.OriginalTzText = string.IsNullOrWhiteSpace(aiResult.OriginalText)
                    ? extractedText
                    : aiResult.OriginalText;

                model.ImprovedTzText = string.IsNullOrWhiteSpace(aiResult.ImprovedText)
                    ? extractedText
                    : aiResult.ImprovedText;

                EnsureScores(model);
                model.ChatDraftPrompt = BuildChatDraftPrompt(model);

                var history = new AnalysisHistoryItem
                {
                    FileName = fileName,
                    SourceType = sourceType,
                    Score = model.TotalScore,
                    CompletenessPercent = model.CompletenessPercent,
                    ClarityPercent = model.ClarityPercent,
                    Verdict = model.RiskLevel,
                    OriginalText = model.OriginalTzText,
                    ImprovedText = model.ImprovedTzText,
                    Summary = model.MainProblem,
                    Recommendation = model.AiRecommendation,
                    CriteriaJson = JsonSerializer.Serialize(model.CriteriaResults),
                    ProblemsJson = JsonSerializer.Serialize(model.Problems),
                    ImprovementsJson = JsonSerializer.Serialize(model.Improvements),
                    TemplateComplianceJson = JsonSerializer.Serialize(model.TemplateCompliance),
                    SectionClassificationsJson = JsonSerializer.Serialize(model.SectionClassifications),
                    ExtractedRequirementsJson = JsonSerializer.Serialize(model.ExtractedRequirements),
                    ExtractedDeadlinesJson = JsonSerializer.Serialize(model.ExtractedDeadlines),
                    ExtractedKpisJson = JsonSerializer.Serialize(model.ExtractedKpis),
                    ExtractedExpectedResultsJson = JsonSerializer.Serialize(model.ExtractedExpectedResults),
                    CreatedAt = DateTime.UtcNow
                };

                _db.AnalysisHistoryItems.Add(history);
                await _db.SaveChangesAsync();

                model.CurrentHistoryId = history.Id;
                model.AnalysisHistory = await GetHistoryAsync();

                return View(model);
            }
            catch (Exception ex)
            {
                model = await BuildDefaultViewModelAsync(model);
                ModelState.AddModelError("", $"Ошибка анализа файла: {ex.Message}");
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> PreviewReferenceMaterial(string materialType)
        {
            try
            {
                var preview = await _referenceMaterialService.GetPreviewAsync(materialType);
                return Json(new
                {
                    success = true,
                    title = preview.title,
                    html = preview.html
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    title = "Ошибка",
                    html = $"<p>{System.Net.WebUtility.HtmlEncode(ex.Message)}</p>"
                });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadCustomTemplate(IFormFile file)
        {
            await _referenceMaterialService.SaveCustomMaterialAsync("template", file);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadCustomExample(IFormFile file)
        {
            await _referenceMaterialService.SaveCustomMaterialAsync("example", file);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadCustomCriteria(IFormFile file)
        {
            await _referenceMaterialService.SaveCustomMaterialAsync("criteria", file);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetReferenceMaterial(string materialType)
        {
            await _referenceMaterialService.ResetMaterialAsync(materialType);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DownloadImprovedDocx(string improvedText, string? fileName)
        {
            var safeFileName = BuildFileName(fileName, ".docx");
            var fileBytes = _documentExportService.CreateImprovedTzDocx(improvedText, "Исправленное техническое задание");
            return File(fileBytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", safeFileName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DownloadImprovedPdf(string improvedText, string? fileName)
        {
            var safeFileName = BuildFileName(fileName, ".pdf");
            var fileBytes = _documentExportService.CreateImprovedTzPdf(improvedText, "Исправленное техническое задание");
            return File(fileBytes, "application/pdf", safeFileName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DownloadImprovedTxt(string improvedText, string? fileName)
        {
            var safeFileName = BuildFileName(fileName, ".txt");
            var fileBytes = _documentExportService.CreateImprovedTzTxt(improvedText, "Исправленное техническое задание");
            return File(fileBytes, "text/plain; charset=utf-8", safeFileName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DownloadFullReportDocx(HomeIndexViewModel model)
        {
            var safeFileName = BuildFileName(model.DownloadFileName, "_FullReport.docx");
            var fileBytes = _documentExportService.CreateAnalysisReportDocx(model, "Полный отчет по анализу НТЗ");
            return File(fileBytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", safeFileName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DownloadFullReportPdf(HomeIndexViewModel model)
        {
            var safeFileName = BuildFileName(model.DownloadFileName, "_FullReport.pdf");
            var fileBytes = _documentExportService.CreateAnalysisReportPdf(model, "Полный отчет по анализу НТЗ");
            return File(fileBytes, "application/pdf", safeFileName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DownloadFullReportExcel(HomeIndexViewModel model)
        {
            var safeFileName = BuildFileName(model.DownloadFileName, "_FullReport.xlsx");
            var fileBytes = _documentExportService.CreateAnalysisReportExcel(model, "Полный отчет по анализу НТЗ");
            return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", safeFileName);
        }

        private async Task<HomeIndexViewModel> BuildDefaultViewModelAsync(HomeIndexViewModel? model = null)
        {
            model ??= new HomeIndexViewModel();

            model.ReferenceMaterials = await _referenceMaterialService.GetMaterialsAsync();
            model.AnalysisHistory = await GetHistoryAsync();

            if (string.IsNullOrWhiteSpace(model.MainProblem))
                model.MainProblem = "Загрузите НТЗ или вставьте текст для анализа.";

            if (string.IsNullOrWhiteSpace(model.AiRecommendation))
                model.AiRecommendation = "Система проверит документ по критериям, сравнит с шаблоном и предложит улучшенную версию.";

            model.CriteriaResults ??= new List<CriterionResult>();
            model.TemplateCompliance ??= new TemplateComplianceResult();
            model.Problems ??= new List<string>();
            model.Improvements ??= new List<string>();
            model.SectionClassifications ??= new List<SectionClassificationResult>();
            model.ExtractedRequirements ??= new List<string>();
            model.ExtractedDeadlines ??= new List<string>();
            model.ExtractedKpis ??= new List<string>();
            model.ExtractedExpectedResults ??= new List<string>();

            if (string.IsNullOrWhiteSpace(model.AnalysisMode))
                model.AnalysisMode = "fast";

            return model;
        }

        private async Task<List<AnalysisHistoryViewModel>> GetHistoryAsync()
        {
            return await _db.AnalysisHistoryItems
                .OrderByDescending(x => x.CreatedAt)
                .Take(30)
                .Select(x => new AnalysisHistoryViewModel
                {
                    Id = x.Id,
                    FileName = x.FileName,
                    SourceType = x.SourceType,
                    Score = x.Score,
                    CompletenessPercent = x.CompletenessPercent,
                    ClarityPercent = x.ClarityPercent,
                    Verdict = x.Verdict,
                    CreatedAt = x.CreatedAt
                })
                .ToListAsync();
        }

        private static void ApplyHistoryToModel(HomeIndexViewModel model, AnalysisHistoryItem item)
        {
            model.CurrentHistoryId = item.Id;
            model.OriginalTzText = item.OriginalText;
            model.ImprovedTzText = item.ImprovedText;
            model.MainProblem = item.Summary;
            model.AiRecommendation = item.Recommendation;
            model.TotalScore = item.Score;
            model.CompletenessPercent = item.CompletenessPercent;
            model.ClarityPercent = item.ClarityPercent;
            model.RiskLevel = item.Verdict;
            model.DownloadFileName = $"Improved_TZ_{item.Id}";

            model.CriteriaResults = DeserializeOrDefault<List<CriterionResult>>(item.CriteriaJson, new());
            model.Problems = DeserializeOrDefault<List<string>>(item.ProblemsJson, new());
            model.Improvements = DeserializeOrDefault<List<string>>(item.ImprovementsJson, new());
            model.TemplateCompliance = DeserializeOrDefault<TemplateComplianceResult>(item.TemplateComplianceJson, new());

            model.SectionClassifications = DeserializeOrDefault<List<SectionClassificationResult>>(item.SectionClassificationsJson, new());
            model.ExtractedRequirements = DeserializeOrDefault<List<string>>(item.ExtractedRequirementsJson, new());
            model.ExtractedDeadlines = DeserializeOrDefault<List<string>>(item.ExtractedDeadlinesJson, new());
            model.ExtractedKpis = DeserializeOrDefault<List<string>>(item.ExtractedKpisJson, new());
            model.ExtractedExpectedResults = DeserializeOrDefault<List<string>>(item.ExtractedExpectedResultsJson, new());

            model.ChatDraftPrompt = BuildChatDraftPrompt(model);
        }

        private static T DeserializeOrDefault<T>(string? json, T fallback)
        {
            if (string.IsNullOrWhiteSpace(json))
                return fallback;

            try
            {
                var result = JsonSerializer.Deserialize<T>(json);
                return result == null ? fallback : result;
            }
            catch
            {
                return fallback;
            }
        }

        private static void EnsureScores(HomeIndexViewModel model)
        {
            if (model.CriteriaResults == null || !model.CriteriaResults.Any())
            {
                model.CriteriaResults = new List<CriterionResult>
                {
                    new() { Name = "Стратегическая релевантность", Weight = 20, MaxScore = 20, Score = 12, Passed = true, Comment = "Оценка рассчитана сервером." },
                    new() { Name = "Цель и задачи", Weight = 10, MaxScore = 10, Score = 7, Passed = true, Comment = "Оценка рассчитана сервером." },
                    new() { Name = "Научная новизна", Weight = 15, MaxScore = 15, Score = 9, Passed = true, Comment = "Оценка рассчитана сервером." },
                    new() { Name = "Практическая применимость", Weight = 20, MaxScore = 20, Score = 14, Passed = true, Comment = "Оценка рассчитана сервером." },
                    new() { Name = "Ожидаемые результаты", Weight = 15, MaxScore = 15, Score = 10, Passed = true, Comment = "Оценка рассчитана сервером." },
                    new() { Name = "Социально-экономический эффект", Weight = 10, MaxScore = 10, Score = 6, Passed = true, Comment = "Оценка рассчитана сервером." },
                    new() { Name = "Реализуемость", Weight = 10, MaxScore = 10, Score = 7, Passed = true, Comment = "Оценка рассчитана сервером." }
                };
            }

            model.TotalScore = model.CriteriaResults.Sum(x => x.Score);
            model.CompletenessPercent = CalculateAveragePercent(model.CriteriaResults, "Цель и задачи", "Ожидаемые результаты", "Реализуемость");
            model.ClarityPercent = CalculateAveragePercent(model.CriteriaResults, "Стратегическая релевантность", "Научная новизна", "Практическая применимость", "Социально-экономический эффект");
            model.RiskLevel = model.TotalScore >= 60 ? "Проходит" : "Не проходит";
        }

        private static int CalculateAveragePercent(List<CriterionResult> criteria, params string[] names)
        {
            var filtered = criteria
                .Where(c => names.Contains(c.Name, StringComparer.OrdinalIgnoreCase) && c.MaxScore > 0)
                .ToList();

            if (!filtered.Any())
                return 0;

            var avg = filtered.Average(c => (double)c.Score / c.MaxScore * 100.0);
            return (int)Math.Round(avg);
        }

        private static string BuildFileName(string? fileName, string extensionOrSuffixWithExtension)
        {
            var baseName = string.IsNullOrWhiteSpace(fileName)
                ? $"Improved_TZ_{DateTime.Now:yyyyMMdd_HHmm}"
                : Path.GetFileNameWithoutExtension(fileName);

            return extensionOrSuffixWithExtension.StartsWith("_", StringComparison.Ordinal)
                ? $"{baseName}{extensionOrSuffixWithExtension}"
                : $"{baseName}{extensionOrSuffixWithExtension}";
        }

        private static string BuildChatDraftPrompt(HomeIndexViewModel model)
        {
            var lines = new List<string>
            {
                "Нужно продолжить улучшение ТЗ по результатам анализа.",
                ""
            };

            if (model.Problems.Any())
            {
                lines.Add("Проблемы, которые нужно исправить:");
                foreach (var p in model.Problems)
                    lines.Add($"- {p}");
                lines.Add("");
            }

            if (model.Improvements.Any())
            {
                lines.Add("Рекомендации, которые нужно учесть:");
                foreach (var i in model.Improvements)
                    lines.Add($"- {i}");
                lines.Add("");
            }

            if (model.ExtractedRequirements.Any())
            {
                lines.Add("Выделенные требования:");
                foreach (var item in model.ExtractedRequirements)
                    lines.Add($"- {item}");
                lines.Add("");
            }

            if (model.ExtractedDeadlines.Any())
            {
                lines.Add("Выделенные сроки:");
                foreach (var item in model.ExtractedDeadlines)
                    lines.Add($"- {item}");
                lines.Add("");
            }

            if (model.ExtractedKpis.Any())
            {
                lines.Add("Выделенные KPI:");
                foreach (var item in model.ExtractedKpis)
                    lines.Add($"- {item}");
                lines.Add("");
            }

            if (model.ExtractedExpectedResults.Any())
            {
                lines.Add("Ожидаемые результаты:");
                foreach (var item in model.ExtractedExpectedResults)
                    lines.Add($"- {item}");
                lines.Add("");
            }

            var questionable = ExtractQuestionableLines(model.ImprovedTzText);
            if (questionable.Any())
            {
                lines.Add("Спорные места и пункты, требующие уточнения:");
                foreach (var q in questionable)
                    lines.Add($"- {q}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static List<string> ExtractQuestionableLines(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            return text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Contains("требует подтверждения", StringComparison.OrdinalIgnoreCase)
                         || x.Contains("нужно уточнить", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .Take(20)
                .ToList();
        }
    }
}