using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NPOI.SS.UserModel;
using NPOI.XWPF.UserModel;
using SpecMind.DataBase;
using SpecMind.Models;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace SpecMind.Services.ReferenceMaterials
{
    public class ReferenceMaterialService : IReferenceMaterialService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly ApplicationDbContext _db;

        private const string StandardFolder = "files";
        private const string CustomFolder = "custom-materials";

        public ReferenceMaterialService(IWebHostEnvironment environment, ApplicationDbContext db)
        {
            _environment = environment;
            _db = db;
        }

        public async Task<List<ReferenceMaterialViewModel>> GetMaterialsAsync()
        {
            var list = new List<ReferenceMaterialViewModel>
            {
                await BuildMaterialAsync("template", "Шаблон НТЗ"),
                await BuildMaterialAsync("example", "Пример НТЗ"),
                await BuildMaterialAsync("criteria", "Критерии оценки")
            };

            return list;
        }

        public async Task SaveCustomMaterialAsync(string materialType, IFormFile file)
        {
            if (file == null || file.Length == 0)
                throw new InvalidOperationException("Файл не выбран.");

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowed = new[] { ".txt", ".docx", ".pdf", ".xlsx" };

            if (!allowed.Contains(extension))
                throw new InvalidOperationException("Поддерживаются только .txt, .docx, .pdf, .xlsx.");

            var customDir = Path.Combine(_environment.WebRootPath, CustomFolder);
            Directory.CreateDirectory(customDir);

            var storedFileName = $"{materialType}_{Guid.NewGuid():N}{extension}";
            var fullPath = Path.Combine(customDir, storedFileName);

            await using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var existing = await _db.ReferenceMaterialFiles
                .Where(x => x.MaterialType == materialType && x.IsCustom)
                .ToListAsync();

            foreach (var item in existing)
            {
                var oldPath = Path.Combine(
                    _environment.WebRootPath,
                    item.RelativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

                if (File.Exists(oldPath))
                    File.Delete(oldPath);

                _db.ReferenceMaterialFiles.Remove(item);
            }

            _db.ReferenceMaterialFiles.Add(new ReferenceMaterialFile
            {
                MaterialType = materialType,
                OriginalFileName = file.FileName,
                StoredFileName = storedFileName,
                RelativePath = $"/{CustomFolder}/{storedFileName}",
                IsCustom = true,
                UploadedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
        }

        public async Task ResetMaterialAsync(string materialType)
        {
            var existing = await _db.ReferenceMaterialFiles
                .Where(x => x.MaterialType == materialType && x.IsCustom)
                .ToListAsync();

            foreach (var item in existing)
            {
                var oldPath = Path.Combine(
                    _environment.WebRootPath,
                    item.RelativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

                if (File.Exists(oldPath))
                    File.Delete(oldPath);

                _db.ReferenceMaterialFiles.Remove(item);
            }

            await _db.SaveChangesAsync();
        }

        public async Task<(string title, string html)> GetPreviewAsync(string materialType)
        {
            try
            {
                var resolved = await ResolveMaterialFileAsync(materialType);
                if (resolved == null)
                    return ("Файл не найден", "<p>Файл не найден.</p>");

                var title = resolved.OriginalFileName;
                var fullPath = Path.Combine(
                    _environment.WebRootPath,
                    resolved.RelativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(fullPath))
                    return ("Файл не найден", "<p>Файл не найден на сервере.</p>");

                var extension = Path.GetExtension(fullPath).ToLowerInvariant();

                string html = extension switch
                {
                    ".txt" => BuildTextHtml(await File.ReadAllTextAsync(fullPath, Encoding.UTF8)),
                    ".docx" => await BuildDocxHtmlAsync(fullPath),
                    ".pdf" => await BuildPdfHtmlAsync(fullPath),
                    ".xlsx" => await BuildXlsxHtmlAsync(fullPath),
                    _ => "<p>Предпросмотр для этого формата не поддерживается.</p>"
                };

                return (title, html);
            }
            catch (Exception ex)
            {
                return ("Ошибка", $"<p>{WebUtility.HtmlEncode(ex.Message)}</p>");
            }
        }

        public async Task<string> GetTemplateTextAsync()
        {
            var item = await ResolveMaterialFileAsync("template");
            return item == null ? "" : await ReadAnySupportedTextAsync(item);
        }

        public async Task<string> GetExampleTextAsync()
        {
            var item = await ResolveMaterialFileAsync("example");
            return item == null ? "" : await ReadAnySupportedTextAsync(item);
        }

        public async Task<string> GetCriteriaTextAsync()
        {
            var item = await ResolveMaterialFileAsync("criteria");
            return item == null ? "" : await ReadAnySupportedTextAsync(item);
        }

        private async Task<ReferenceMaterialViewModel> BuildMaterialAsync(string materialType, string title)
        {
            var resolved = await ResolveMaterialFileAsync(materialType);

            return new ReferenceMaterialViewModel
            {
                MaterialType = materialType,
                Title = title,
                IsCustom = resolved?.IsCustom == true,
                DownloadUrl = resolved?.RelativePath ?? "#",
                PreviewUrl = $"/Home/PreviewReferenceMaterial?materialType={materialType}",
                UploadAction = GetUploadAction(materialType),
                ResetAction = $"/Home/ResetReferenceMaterial?materialType={materialType}"
            };
        }

        private static string GetUploadAction(string materialType) => materialType switch
        {
            "template" => "UploadCustomTemplate",
            "example" => "UploadCustomExample",
            "criteria" => "UploadCustomCriteria",
            _ => "UploadCustomTemplate"
        };

        private async Task<ReferenceMaterialFile?> ResolveMaterialFileAsync(string materialType)
        {
            var custom = await _db.ReferenceMaterialFiles
                .Where(x => x.MaterialType == materialType && x.IsCustom)
                .OrderByDescending(x => x.UploadedAt)
                .FirstOrDefaultAsync();

            if (custom != null)
                return custom;

            var standardFolder = Path.Combine(_environment.WebRootPath, StandardFolder);
            if (!Directory.Exists(standardFolder))
                return null;

            var fileName = FindStandardFileName(materialType, standardFolder);
            if (fileName == null)
                return null;

            return new ReferenceMaterialFile
            {
                MaterialType = materialType,
                OriginalFileName = fileName,
                StoredFileName = fileName,
                RelativePath = $"/{StandardFolder}/{fileName}",
                IsCustom = false
            };
        }

        private static string? FindStandardFileName(string materialType, string folder)
        {
            var files = Directory.GetFiles(folder)
                .Select(Path.GetFileName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .ToList();

            if (!files.Any())
                return null;

            var candidates = materialType switch
            {
                "template" => new[]
                {
                    "template.docx",
                    "template.txt",
                    "template.pdf",
                    "Шаблон для ТЗ рус.docx",
                    "Шаблон для ТЗ рус(2).docx",
                    "Шаблон для ТЗ рус(3).docx",
                    "Шаблон для ТЗ рус(4).docx"
                },
                "example" => new[]
                {
                    "example.docx",
                    "example.txt",
                    "example.pdf",
                    "ТЗ Цифровой полигон.docx",
                    "ТЗ Цифровой полигон(2).docx",
                    "ТЗ_официальное.docx"
                },
                "criteria" => new[]
                {
                    "criteria.xlsx",
                    "criteria.docx",
                    "criteria.txt",
                    "Оценка_ТЗ_шаблон.xlsx"
                },
                _ => Array.Empty<string>()
            };

            foreach (var candidate in candidates)
            {
                var match = files.FirstOrDefault(x =>
                    string.Equals(x, candidate, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                    return match;
            }

            return files.FirstOrDefault();
        }

        private async Task<string> ReadAnySupportedTextAsync(ReferenceMaterialFile file)
        {
            var fullPath = Path.Combine(
                _environment.WebRootPath,
                file.RelativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

            var extension = Path.GetExtension(fullPath).ToLowerInvariant();

            return extension switch
            {
                ".txt" => await File.ReadAllTextAsync(fullPath, Encoding.UTF8),
                ".docx" => await ReadDocxTextAsync(fullPath),
                ".pdf" => await ReadPdfTextAsync(fullPath),
                ".xlsx" => await ReadXlsxTextAsync(fullPath),
                _ => ""
            };
        }

        private static async Task<string> ReadDocxTextAsync(string path)
        {
            var sb = new StringBuilder();

            using var stream = File.OpenRead(path);
            using var doc = new XWPFDocument(stream);

            foreach (var p in doc.Paragraphs)
            {
                if (!string.IsNullOrWhiteSpace(p.Text))
                    sb.AppendLine(p.Text);
                else
                    sb.AppendLine();
            }

            foreach (var table in doc.Tables)
            {
                sb.AppendLine();
                foreach (var row in table.Rows)
                {
                    var cells = row.GetTableCells().Select(c => c.GetText()).ToList();
                    sb.AppendLine(string.Join(" | ", cells));
                }
                sb.AppendLine();
            }

            await Task.CompletedTask;
            return NormalizeStructuredText(sb.ToString());
        }

        private static async Task<string> ReadPdfTextAsync(string path)
        {
            var sb = new StringBuilder();

            using var pdf = PdfDocument.Open(path);
            foreach (var page in pdf.GetPages())
            {
                var pageText = string.Join(" ", page.GetWords().Select(w => w.Text));
                if (!string.IsNullOrWhiteSpace(pageText))
                    sb.AppendLine(pageText);
                sb.AppendLine();
            }

            await Task.CompletedTask;
            return NormalizeStructuredText(sb.ToString());
        }

        private static async Task<string> ReadXlsxTextAsync(string path)
        {
            using var stream = File.OpenRead(path);
            var workbook = WorkbookFactory.Create(stream);
            var sb = new StringBuilder();

            for (int s = 0; s < workbook.NumberOfSheets; s++)
            {
                var sheet = workbook.GetSheetAt(s);
                sb.AppendLine($"Лист: {sheet.SheetName}");

                for (int i = sheet.FirstRowNum; i <= sheet.LastRowNum; i++)
                {
                    var row = sheet.GetRow(i);
                    if (row == null) continue;

                    var cells = new List<string>();
                    for (int j = 0; j < row.LastCellNum; j++)
                    {
                        var cell = row.GetCell(j);
                        cells.Add(cell?.ToString() ?? "");
                    }

                    sb.AppendLine(string.Join(" | ", cells));
                }

                sb.AppendLine();
            }

            await Task.CompletedTask;
            return sb.ToString();
        }

        private static string BuildTextHtml(string text)
        {
            var normalized = NormalizeStructuredText(text);
            var encoded = WebUtility.HtmlEncode(normalized);
            return $"<div class='doc-preview-text'>{encoded}</div>";
        }

        private static async Task<string> BuildDocxHtmlAsync(string path)
        {
            var sb = new StringBuilder();

            using var stream = File.OpenRead(path);
            using var doc = new XWPFDocument(stream);

            sb.AppendLine("<div class='doc-preview-docx'>");

            foreach (var p in doc.Paragraphs)
            {
                var text = p.Text ?? "";

                if (string.IsNullOrWhiteSpace(text))
                {
                    sb.AppendLine("<div class='doc-spacer'></div>");
                    continue;
                }

                foreach (var block in SplitStructuredBlocks(text))
                {
                    var cleaned = WebUtility.HtmlEncode(block);

                    if (IsHeading(block))
                        sb.AppendLine($"<h4 class='doc-heading'>{cleaned}</h4>");
                    else if (block.TrimStart().StartsWith("-"))
                        sb.AppendLine($"<div class='doc-bullet'>{cleaned}</div>");
                    else
                        sb.AppendLine($"<p class='doc-paragraph'>{cleaned}</p>");
                }
            }

            foreach (var table in doc.Tables)
            {
                sb.AppendLine("<div class='doc-table-wrap always-scroll'>");
                sb.AppendLine("<table class='doc-table'>");

                foreach (var row in table.Rows)
                {
                    sb.AppendLine("<tr>");

                    foreach (var cell in row.GetTableCells())
                    {
                        var cellText = NormalizeStructuredText(cell.GetText());
                        cellText = WebUtility.HtmlEncode(cellText);
                        sb.AppendLine($"<td>{cellText}</td>");
                    }

                    sb.AppendLine("</tr>");
                }

                sb.AppendLine("</table>");
                sb.AppendLine("</div>");
            }

            sb.AppendLine("</div>");

            await Task.CompletedTask;
            return sb.ToString();
        }

        private static async Task<string> BuildPdfHtmlAsync(string path)
        {
            var text = await ReadPdfTextAsync(path);
            return BuildTextHtml(text);
        }

        private static async Task<string> BuildXlsxHtmlAsync(string path)
        {
            using var stream = File.OpenRead(path);
            var workbook = WorkbookFactory.Create(stream);
            var sb = new StringBuilder();

            sb.AppendLine("<div class='doc-preview-xlsx'>");

            for (int s = 0; s < workbook.NumberOfSheets; s++)
            {
                var sheet = workbook.GetSheetAt(s);

                sb.AppendLine($"<h4 class='doc-heading'>{WebUtility.HtmlEncode(sheet.SheetName)}</h4>");
                sb.AppendLine("<div class='doc-table-wrap always-scroll'>");
                sb.AppendLine("<table class='doc-table'>");

                for (int i = sheet.FirstRowNum; i <= sheet.LastRowNum; i++)
                {
                    var row = sheet.GetRow(i);
                    if (row == null) continue;

                    sb.AppendLine("<tr>");
                    for (int j = 0; j < row.LastCellNum; j++)
                    {
                        var cell = row.GetCell(j);
                        sb.AppendLine($"<td>{WebUtility.HtmlEncode(cell?.ToString() ?? "")}</td>");
                    }
                    sb.AppendLine("</tr>");
                }

                sb.AppendLine("</table>");
                sb.AppendLine("</div>");
            }

            sb.AppendLine("</div>");

            await Task.CompletedTask;
            return sb.ToString();
        }

        private static string NormalizeStructuredText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            text = text.Replace("\r\n", "\n");

            text = Regex.Replace(text, @"(?<!\n)(\d+\.\d+\.)", "\n$1");
            text = Regex.Replace(text, @"(?<!\n)(\d+\.)\s", "\n$1 ");
            text = Regex.Replace(text, @"(?<!\n)(-\s)", "\n$1");
            text = Regex.Replace(text, @"(?<!\n)(По результатам программы должны быть получены:)", "\n$1");
            text = Regex.Replace(text, @"(?<!\n)(Показатели воздействия на экономику:)", "\n$1");
            text = Regex.Replace(text, @"(?<!\n)(Экологический эффект:)", "\n$1");
            text = Regex.Replace(text, @"(?<!\n)(Социальный эффект)", "\n$1");
            text = Regex.Replace(text, @"(?<!\n)(Целевыми потребителями полученных результатов)", "\n$1");

            text = Regex.Replace(text, @"\n{3,}", "\n\n");

            return text.Trim();
        }

        private static List<string> SplitStructuredBlocks(string text)
        {
            var normalized = NormalizeStructuredText(text);
            return normalized
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }

        private static bool IsHeading(string line)
        {
            return line.StartsWith("Техническое задание", StringComparison.OrdinalIgnoreCase)
                   || line.StartsWith("1.", StringComparison.OrdinalIgnoreCase)
                   || line.StartsWith("1.1.", StringComparison.OrdinalIgnoreCase)
                   || line.StartsWith("1.2.", StringComparison.OrdinalIgnoreCase)
                   || line.StartsWith("2.", StringComparison.OrdinalIgnoreCase)
                   || line.StartsWith("2.1.", StringComparison.OrdinalIgnoreCase)
                   || line.StartsWith("2.2.", StringComparison.OrdinalIgnoreCase)
                   || line.StartsWith("3.", StringComparison.OrdinalIgnoreCase)
                   || line.StartsWith("4.", StringComparison.OrdinalIgnoreCase)
                   || line.StartsWith("4.1", StringComparison.OrdinalIgnoreCase)
                   || line.StartsWith("4.2", StringComparison.OrdinalIgnoreCase)
                   || line.StartsWith("5.", StringComparison.OrdinalIgnoreCase);
        }
    }
}