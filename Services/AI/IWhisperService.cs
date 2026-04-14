namespace SpecMind.Services.AI
{
    public interface IWhisperService
    {
        Task<string> TranscribeAsync(IFormFile audioFile, CancellationToken cancellationToken = default);
    }
}