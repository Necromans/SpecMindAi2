using System.Net.Http.Headers;
using System.Text.Json;

namespace SpecMind.Services.AI
{
    public class WhisperService : IWhisperService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model;

        public WhisperService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(5)
            };

            _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                      ?? throw new Exception("OPENAI_API_KEY не найден");

            _model = Environment.GetEnvironmentVariable("OPENAI_WHISPER_MODEL") ?? "whisper-1";
        }

        public async Task<string> TranscribeAsync(IFormFile audioFile, CancellationToken cancellationToken = default)
        {
            if (audioFile == null || audioFile.Length == 0)
                throw new Exception("Аудиофайл пустой.");

            await using var stream = audioFile.OpenReadStream();

            using var form = new MultipartFormDataContent();
            using var fileContent = new StreamContent(stream);

            fileContent.Headers.ContentType = new MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(audioFile.ContentType) ? "audio/webm" : audioFile.ContentType);

            form.Add(fileContent, "file", audioFile.FileName);
            form.Add(new StringContent(_model), "model");
            form.Add(new StringContent("text"), "response_format");
            form.Add(new StringContent("ru"), "language");

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://api.openai.com/v1/audio/transcriptions");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Content = form;

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Ошибка распознавания: {responseText}");

            return responseText.Trim();
        }
    }
}