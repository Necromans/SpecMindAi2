using Microsoft.AspNetCore.Http;
using NPOI.XWPF.UserModel;
using System.Text;
using UglyToad.PdfPig;
using SystemPath = System.IO.Path;

namespace SpecMind.Services
{
    public class DocumentTextExtractorService : IDocumentTextExtractorService
    {
        private const long MaxFileSizeBytes = 10 * 1024 * 1024;

        public async Task<string> ExtractTextAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
                throw new InvalidOperationException("Файл пустой.");

            if (file.Length > MaxFileSizeBytes)
                throw new InvalidOperationException("Файл слишком большой. Максимум — 10 МБ.");

            var extension = SystemPath.GetExtension(file.FileName).ToLowerInvariant();

            return extension switch
            {
                ".txt" => await ExtractTxtAsync(file),
                ".docx" => await ExtractDocxAsync(file),
                ".pdf" => await ExtractPdfAsync(file),
                _ => throw new NotSupportedException(
                    $"Формат «{extension}» не поддерживается. Используйте .txt, .docx или .pdf.")
            };
        }

        private async Task<string> ExtractTxtAsync(IFormFile file)
        {
            try
            {
                using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
                return await reader.ReadToEndAsync();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Не удалось прочитать .txt: {ex.Message}", ex);
            }
        }

        private async Task<string> ExtractDocxAsync(IFormFile file)
        {
            try
            {
                using var memoryStream = new MemoryStream();
                await file.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                using var document = new XWPFDocument(memoryStream);
                var sb = new StringBuilder();

                foreach (var paragraph in document.Paragraphs)
                {
                    if (!string.IsNullOrWhiteSpace(paragraph.Text))
                        sb.AppendLine(paragraph.Text);
                }

                foreach (var table in document.Tables)
                {
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.GetTableCells())
                        {
                            foreach (var para in cell.Paragraphs)
                            {
                                if (!string.IsNullOrWhiteSpace(para.Text))
                                    sb.AppendLine(para.Text);
                            }
                        }
                    }
                }

                if (sb.Length == 0)
                    throw new InvalidOperationException("Документ не содержит текста или повреждён.");

                return sb.ToString();
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Не удалось открыть .docx: {ex.Message}", ex);
            }
        }

        private async Task<string> ExtractPdfAsync(IFormFile file)
        {
            try
            {
                using var memoryStream = new MemoryStream();
                await file.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                var sb = new StringBuilder();

                using var pdfDocument = PdfDocument.Open(memoryStream);

                foreach (var page in pdfDocument.GetPages())
                {
                    var pageText = string.Join(" ", page.GetWords().Select(w => w.Text));
                    if (!string.IsNullOrWhiteSpace(pageText))
                        sb.AppendLine(pageText);
                }

                if (sb.Length == 0)
                    throw new InvalidOperationException("PDF не содержит текста. Возможно, это сканированный документ.");

                return sb.ToString();
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Не удалось прочитать PDF: {ex.Message}", ex);
            }
        }
    }
}