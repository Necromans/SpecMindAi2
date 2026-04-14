using ClosedXML.Excel;
using NPOI.SS.UserModel;
using NPOI.XWPF.UserModel;
using PdfSharpCore;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using SpecMind.Models;
using System.Globalization;
using System.Text;

namespace SpecMind.Services
{
    public interface IDocumentExportService
    {
        byte[] CreateImprovedTzDocx(string improvedText, string? documentTitle = null);
        byte[] CreateImprovedTzPdf(string improvedText, string? documentTitle = null);
        byte[] CreateImprovedTzTxt(string improvedText, string? documentTitle = null);

        byte[] CreateAnalysisReportDocx(HomeIndexViewModel model, string? documentTitle = null);
        byte[] CreateAnalysisReportPdf(HomeIndexViewModel model, string? documentTitle = null);
        byte[] CreateAnalysisReportExcel(HomeIndexViewModel model, string? documentTitle = null);
    }

    public class DocumentExportService : IDocumentExportService
    {
        public byte[] CreateImprovedTzDocx(string improvedText, string? documentTitle = null)
        {
            if (string.IsNullOrWhiteSpace(improvedText))
                throw new InvalidOperationException("Нет исправленного текста для экспорта.");

            using var memoryStream = new MemoryStream();
            using var document = new XWPFDocument();

            AddDocTitle(document, documentTitle ?? "Исправленное техническое задание");
            AddDocParagraphs(document, NormalizeLines(improvedText));

            document.Write(memoryStream);
            return memoryStream.ToArray();
        }

        public byte[] CreateImprovedTzPdf(string improvedText, string? documentTitle = null)
        {
            if (string.IsNullOrWhiteSpace(improvedText))
                throw new InvalidOperationException("Нет исправленного текста для экспорта.");

            var contentLines = NormalizeLines(improvedText);
            return CreatePdfFromLines(documentTitle ?? "Исправленное техническое задание", contentLines);
        }

        public byte[] CreateImprovedTzTxt(string improvedText, string? documentTitle = null)
        {
            if (string.IsNullOrWhiteSpace(improvedText))
                throw new InvalidOperationException("Нет исправленного текста для экспорта.");

            var title = string.IsNullOrWhiteSpace(documentTitle)
                ? "Исправленное техническое задание"
                : documentTitle;

            var content = $"{title}{Environment.NewLine}{Environment.NewLine}{improvedText}";
            return Encoding.UTF8.GetBytes(content);
        }

        public byte[] CreateAnalysisReportDocx(HomeIndexViewModel model, string? documentTitle = null)
        {
            using var memoryStream = new MemoryStream();
            using var document = new XWPFDocument();

            AddDocTitle(document, documentTitle ?? "Полный отчет по анализу НТЗ");

            AddSection(document, "1. Общая информация");
            AddBullet(document, $"Основная проблема: {Safe(model.MainProblem)}");
            AddBullet(document, $"Рекомендация AI: {Safe(model.AiRecommendation)}");
            AddBullet(document, $"Общая оценка: {model.TotalScore}");
            AddBullet(document, $"Полнота: {model.CompletenessPercent}%");
            AddBullet(document, $"Ясность: {model.ClarityPercent}%");
            AddBullet(document, $"Вердикт: {Safe(model.RiskLevel)}");

            AddSection(document, "2. Проблемы");
            AddStringList(document, model.Problems, "Проблемы не выявлены.");

            AddSection(document, "3. Рекомендации и улучшения");
            AddStringList(document, model.Improvements, "Рекомендации отсутствуют.");

            AddSection(document, "4. Оценка по критериям");
            AddCriteriaTable(document, model.CriteriaResults);

            AddSection(document, "5. Проверка соответствия шаблону");
            AddBullet(document, $"Совпадает с шаблоном: {(model.TemplateCompliance?.MatchesTemplate == true ? "Да" : "Нет")}");
            AddBullet(document, $"Комментарий по структуре: {Safe(model.TemplateCompliance?.StructureComment)}");
            AddSubsection(document, "Отсутствующие разделы");
            AddStringList(document, model.TemplateCompliance?.MissingSections, "Все ключевые разделы найдены.");

            AddSection(document, "6. Классификация разделов ТЗ");
            AddSectionClassificationTable(document, model.SectionClassifications);

            AddSection(document, "7. Выделенные требования");
            AddStringList(document, model.ExtractedRequirements, "Не выделены.");

            AddSection(document, "8. Выделенные сроки");
            AddStringList(document, model.ExtractedDeadlines, "Не выделены.");

            AddSection(document, "9. Выделенные KPI");
            AddStringList(document, model.ExtractedKpis, "Не выделены.");

            AddSection(document, "10. Ожидаемые результаты");
            AddStringList(document, model.ExtractedExpectedResults, "Не выделены.");

            AddSection(document, "11. Исходный текст");
            AddDocParagraphs(document, NormalizeLines(model.OriginalTzText));

            AddSection(document, "12. Улучшенный текст");
            AddDocParagraphs(document, NormalizeLines(model.ImprovedTzText));

            document.Write(memoryStream);
            return memoryStream.ToArray();
        }

        public byte[] CreateAnalysisReportPdf(HomeIndexViewModel model, string? documentTitle = null)
        {
            var lines = BuildReportLines(model);
            return CreatePdfFromLines(documentTitle ?? "Полный отчет по анализу НТЗ", lines);
        }

        public byte[] CreateAnalysisReportExcel(HomeIndexViewModel model, string? documentTitle = null)
        {
            using var workbook = new XLWorkbook();

            var summary = workbook.Worksheets.Add("Сводка");
            summary.Cell(1, 1).Value = documentTitle ?? "Полный отчет по анализу НТЗ";
            summary.Cell(3, 1).Value = "Основная проблема";
            summary.Cell(3, 2).Value = model.MainProblem;

            summary.Cell(4, 1).Value = "Рекомендация AI";
            summary.Cell(4, 2).Value = model.AiRecommendation;

            summary.Cell(5, 1).Value = "Общая оценка";
            summary.Cell(5, 2).Value = model.TotalScore;

            summary.Cell(6, 1).Value = "Полнота";
            summary.Cell(6, 2).Value = model.CompletenessPercent;

            summary.Cell(7, 1).Value = "Ясность";
            summary.Cell(7, 2).Value = model.ClarityPercent;

            summary.Cell(8, 1).Value = "Вердикт";
            summary.Cell(8, 2).Value = model.RiskLevel;

            StyleHeader(summary.Range("A1:B1"));
            StyleHeader(summary.Range("A3:A8"));
            summary.Columns().AdjustToContents();

            var criteria = workbook.Worksheets.Add("Критерии");
            criteria.Cell(1, 1).Value = "Критерий";
            criteria.Cell(1, 2).Value = "Вес";
            criteria.Cell(1, 3).Value = "Балл";
            criteria.Cell(1, 4).Value = "Макс. балл";
            criteria.Cell(1, 5).Value = "Пройден";
            criteria.Cell(1, 6).Value = "Комментарий";
            StyleHeader(criteria.Range("A1:F1"));

            var row = 2;
            foreach (var item in model.CriteriaResults ?? new List<CriterionResult>())
            {
                criteria.Cell(row, 1).Value = item.Name;
                criteria.Cell(row, 2).Value = item.Weight;
                criteria.Cell(row, 3).Value = item.Score;
                criteria.Cell(row, 4).Value = item.MaxScore;
                criteria.Cell(row, 5).Value = item.Passed ? "Да" : "Нет";
                criteria.Cell(row, 6).Value = item.Comment;
                row++;
            }
            criteria.Columns().AdjustToContents();

            AddListWorksheet(workbook, "Проблемы", model.Problems, "Проблема");
            AddListWorksheet(workbook, "Рекомендации", model.Improvements, "Рекомендация");
            AddListWorksheet(workbook, "Требования", model.ExtractedRequirements, "Требование");
            AddListWorksheet(workbook, "Сроки", model.ExtractedDeadlines, "Срок");
            AddListWorksheet(workbook, "KPI", model.ExtractedKpis, "KPI");
            AddListWorksheet(workbook, "Результаты", model.ExtractedExpectedResults, "Ожидаемый результат");

            var sections = workbook.Worksheets.Add("Разделы");
            sections.Cell(1, 1).Value = "Название раздела";
            sections.Cell(1, 2).Value = "Категория";
            sections.Cell(1, 3).Value = "Комментарий";
            StyleHeader(sections.Range("A1:C1"));

            row = 2;
            foreach (var item in model.SectionClassifications ?? new List<SectionClassificationResult>())
            {
                sections.Cell(row, 1).Value = item.SectionName;
                sections.Cell(row, 2).Value = item.Category;
                sections.Cell(row, 3).Value = item.Comment;
                row++;
            }
            sections.Columns().AdjustToContents();

            var texts = workbook.Worksheets.Add("Тексты");
            texts.Cell(1, 1).Value = "Исходный текст";
            texts.Cell(1, 2).Value = "Улучшенный текст";
            StyleHeader(texts.Range("A1:B1"));
            texts.Cell(2, 1).Value = model.OriginalTzText;
            texts.Cell(2, 2).Value = model.ImprovedTzText;
            texts.Column(1).Width = 60;
            texts.Column(2).Width = 60;
            texts.Row(2).Height = 220;
            texts.Cells().Style.Alignment.WrapText = true;
            texts.Cells().Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static void AddListWorksheet(XLWorkbook workbook, string name, List<string>? items, string header)
        {
            var ws = workbook.Worksheets.Add(name);
            ws.Cell(1, 1).Value = header;
            StyleHeader(ws.Range("A1:A1"));

            var row = 2;
            if (items != null && items.Any())
            {
                foreach (var item in items)
                {
                    ws.Cell(row, 1).Value = item;
                    row++;
                }
            }
            else
            {
                ws.Cell(2, 1).Value = "Нет данных";
            }

            ws.Column(1).Width = 90;
            ws.Cells().Style.Alignment.WrapText = true;
        }

        private static void StyleHeader(IXLRange range)
        {
            range.Style.Font.Bold = true;
            range.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCE6F1");
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        private static List<string> BuildReportLines(HomeIndexViewModel model)
        {
            var lines = new List<string>
            {
                "1. Общая информация",
                $"Основная проблема: {Safe(model.MainProblem)}",
                $"Рекомендация AI: {Safe(model.AiRecommendation)}",
                $"Общая оценка: {model.TotalScore}",
                $"Полнота: {model.CompletenessPercent}%",
                $"Ясность: {model.ClarityPercent}%",
                $"Вердикт: {Safe(model.RiskLevel)}",
                "",
                "2. Проблемы"
            };

            AppendList(lines, model.Problems, "Проблемы не выявлены.");

            lines.Add("");
            lines.Add("3. Рекомендации и улучшения");
            AppendList(lines, model.Improvements, "Рекомендации отсутствуют.");

            lines.Add("");
            lines.Add("4. Оценка по критериям");
            if (model.CriteriaResults != null && model.CriteriaResults.Any())
            {
                foreach (var item in model.CriteriaResults)
                {
                    lines.Add($"- {item.Name}: {item.Score}/{item.MaxScore}. {Safe(item.Comment)}");
                }
            }
            else
            {
                lines.Add("Критерии отсутствуют.");
            }

            lines.Add("");
            lines.Add("5. Проверка соответствия шаблону");
            lines.Add($"Совпадает с шаблоном: {(model.TemplateCompliance?.MatchesTemplate == true ? "Да" : "Нет")}");
            lines.Add($"Комментарий по структуре: {Safe(model.TemplateCompliance?.StructureComment)}");
            lines.Add("Отсутствующие разделы:");
            AppendList(lines, model.TemplateCompliance?.MissingSections, "Все ключевые разделы найдены.");

            lines.Add("");
            lines.Add("6. Классификация разделов");
            if (model.SectionClassifications != null && model.SectionClassifications.Any())
            {
                foreach (var item in model.SectionClassifications)
                {
                    lines.Add($"- {item.SectionName} | {item.Category} | {Safe(item.Comment)}");
                }
            }
            else
            {
                lines.Add("Нет данных.");
            }

            lines.Add("");
            lines.Add("7. Выделенные требования");
            AppendList(lines, model.ExtractedRequirements, "Не выделены.");

            lines.Add("");
            lines.Add("8. Выделенные сроки");
            AppendList(lines, model.ExtractedDeadlines, "Не выделены.");

            lines.Add("");
            lines.Add("9. Выделенные KPI");
            AppendList(lines, model.ExtractedKpis, "Не выделены.");

            lines.Add("");
            lines.Add("10. Ожидаемые результаты");
            AppendList(lines, model.ExtractedExpectedResults, "Не выделены.");

            lines.Add("");
            lines.Add("11. Исходный текст");
            lines.AddRange(NormalizeLines(model.OriginalTzText));

            lines.Add("");
            lines.Add("12. Улучшенный текст");
            lines.AddRange(NormalizeLines(model.ImprovedTzText));

            return lines;
        }

        private static void AppendList(List<string> lines, List<string>? items, string emptyText)
        {
            if (items != null && items.Any())
            {
                foreach (var item in items)
                    lines.Add($"- {item}");
            }
            else
            {
                lines.Add(emptyText);
            }
        }

        private static byte[] CreatePdfFromLines(string title, List<string> contentLines)
        {
            using var stream = new MemoryStream();
            using var document = new PdfDocument();

            var fontRegular = new XFont("Arial", 10, XFontStyle.Regular);
            var fontBold = new XFont("Arial", 10, XFontStyle.Bold);
            var titleFont = new XFont("Arial", 14, XFontStyle.Bold);

            PdfPage page = document.AddPage();
            page.Size = PageSize.A4;

            XGraphics gfx = XGraphics.FromPdfPage(page);

            const double margin = 35;
            const double lineHeight = 16;
            double y = margin;
            var maxWidth = page.Width - margin * 2;

            gfx.DrawString(title, titleFont, XBrushes.Black,
                new XRect(margin, y, maxWidth, 25), XStringFormats.TopCenter);

            y += 32;

            foreach (var line in contentLines)
            {
                var font = IsHeading(line) ? fontBold : fontRegular;
                var wrapped = WrapText(gfx, line, font, maxWidth);

                foreach (var part in wrapped)
                {
                    if (y > page.Height - margin)
                    {
                        page = document.AddPage();
                        page.Size = PageSize.A4;
                        gfx = XGraphics.FromPdfPage(page);
                        y = margin;
                    }

                    gfx.DrawString(part, font, XBrushes.Black,
                        new XRect(margin, y, maxWidth, lineHeight), XStringFormats.TopLeft);

                    y += lineHeight;
                }

                y += 3;
            }

            document.Save(stream, false);
            return stream.ToArray();
        }

        private static void AddDocTitle(XWPFDocument document, string title)
        {
            var titleParagraph = document.CreateParagraph();
            titleParagraph.Alignment = ParagraphAlignment.CENTER;
            titleParagraph.SpacingAfter = 300;

            var titleRun = titleParagraph.CreateRun();
            titleRun.IsBold = true;
            titleRun.FontSize = 14;
            titleRun.FontFamily = "Times New Roman";
            titleRun.SetText(title);
        }

        private static void AddSection(XWPFDocument document, string text)
        {
            var p = document.CreateParagraph();
            p.SpacingBefore = 160;
            p.SpacingAfter = 120;

            var r = p.CreateRun();
            r.IsBold = true;
            r.FontFamily = "Times New Roman";
            r.FontSize = 12;
            r.SetText(text);
        }

        private static void AddSubsection(XWPFDocument document, string text)
        {
            var p = document.CreateParagraph();
            p.SpacingBefore = 80;
            p.SpacingAfter = 80;

            var r = p.CreateRun();
            r.IsBold = true;
            r.FontFamily = "Times New Roman";
            r.FontSize = 12;
            r.SetText(text);
        }

        private static void AddBullet(XWPFDocument document, string text)
        {
            var p = document.CreateParagraph();
            p.IndentationLeft = 300;
            p.SpacingAfter = 60;

            var r = p.CreateRun();
            r.FontFamily = "Times New Roman";
            r.FontSize = 12;
            r.SetText("• " + text);
        }

        private static void AddStringList(XWPFDocument document, List<string>? items, string emptyText)
        {
            if (items == null || !items.Any())
            {
                AddBullet(document, emptyText);
                return;
            }

            foreach (var item in items)
                AddBullet(document, item);
        }

        private static void AddDocParagraphs(XWPFDocument document, List<string> lines)
        {
            foreach (var line in lines)
            {
                var paragraph = document.CreateParagraph();
                paragraph.SpacingAfter = 100;
                paragraph.Alignment = ParagraphAlignment.BOTH;

                var run = paragraph.CreateRun();
                run.FontFamily = "Times New Roman";
                run.FontSize = 12;
                run.IsBold = IsHeading(line);
                run.SetText(line);
            }
        }

        private static void AddCriteriaTable(XWPFDocument document, List<CriterionResult>? criteria)
        {
            if (criteria == null || !criteria.Any())
            {
                AddBullet(document, "Нет данных по критериям.");
                return;
            }

            var table = document.CreateTable(criteria.Count + 1, 6);
            table.Width = 5000;

            SetCell(table, 0, 0, "Критерий", true);
            SetCell(table, 0, 1, "Вес", true);
            SetCell(table, 0, 2, "Балл", true);
            SetCell(table, 0, 3, "Макс.", true);
            SetCell(table, 0, 4, "Пройден", true);
            SetCell(table, 0, 5, "Комментарий", true);

            for (int i = 0; i < criteria.Count; i++)
            {
                var item = criteria[i];
                SetCell(table, i + 1, 0, item.Name);
                SetCell(table, i + 1, 1, item.Weight.ToString(CultureInfo.InvariantCulture));
                SetCell(table, i + 1, 2, item.Score.ToString(CultureInfo.InvariantCulture));
                SetCell(table, i + 1, 3, item.MaxScore.ToString(CultureInfo.InvariantCulture));
                SetCell(table, i + 1, 4, item.Passed ? "Да" : "Нет");
                SetCell(table, i + 1, 5, item.Comment);
            }
        }

        private static void AddSectionClassificationTable(XWPFDocument document, List<SectionClassificationResult>? sections)
        {
            if (sections == null || !sections.Any())
            {
                AddBullet(document, "Нет данных по классификации разделов.");
                return;
            }

            var table = document.CreateTable(sections.Count + 1, 3);
            table.Width = 5000;

            SetCell(table, 0, 0, "Раздел", true);
            SetCell(table, 0, 1, "Категория", true);
            SetCell(table, 0, 2, "Комментарий", true);

            for (int i = 0; i < sections.Count; i++)
            {
                var item = sections[i];
                SetCell(table, i + 1, 0, item.SectionName);
                SetCell(table, i + 1, 1, item.Category);
                SetCell(table, i + 1, 2, item.Comment);
            }
        }

        private static void SetCell(XWPFTable table, int row, int col, string text, bool bold = false)
        {
            var cell = table.GetRow(row).GetCell(col);
            cell.RemoveParagraph(0);
            var paragraph = cell.AddParagraph();
            var run = paragraph.CreateRun();
            run.FontFamily = "Times New Roman";
            run.FontSize = 11;
            run.IsBold = bold;
            run.SetText(text ?? "");
        }

        private static List<string> NormalizeLines(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string> { "" };

            return text.Replace("\r\n", "\n")
                .Split('\n', StringSplitOptions.None)
                .Select(x => x?.TrimEnd() ?? "")
                .ToList();
        }

        private static bool IsHeading(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            return char.IsDigit(line[0]) ||
                   line.StartsWith("Техническое задание", StringComparison.OrdinalIgnoreCase) ||
                   line.StartsWith("Основная проблема", StringComparison.OrdinalIgnoreCase) ||
                   line.StartsWith("Рекомендация", StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> WrapText(XGraphics gfx, string text, XFont font, double maxWidth)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
            {
                result.Add("");
                return result;
            }

            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var currentLine = "";

            foreach (var word in words)
            {
                var testLine = string.IsNullOrWhiteSpace(currentLine) ? word : $"{currentLine} {word}";
                var size = gfx.MeasureString(testLine, font);

                if (size.Width <= maxWidth)
                {
                    currentLine = testLine;
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(currentLine))
                        result.Add(currentLine);

                    currentLine = word;
                }
            }

            if (!string.IsNullOrWhiteSpace(currentLine))
                result.Add(currentLine);

            return result;
        }

        private static string Safe(string? value)
            => string.IsNullOrWhiteSpace(value) ? "Нет данных" : value.Trim();
    }
}