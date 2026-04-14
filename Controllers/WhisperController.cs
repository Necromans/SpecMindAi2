using Microsoft.AspNetCore.Mvc;
using SpecMind.Services.AI;

namespace SpecMind.Controllers
{
    [Route("Whisper")]
    public class WhisperController : Controller
    {
        private readonly IWhisperService _whisperService;

        public WhisperController(IWhisperService whisperService)
        {
            _whisperService = whisperService;
        }

        [HttpPost("Transcribe")]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(25_000_000)]
        public async Task<IActionResult> Transcribe(IFormFile audioFile, CancellationToken cancellationToken)
        {
            try
            {
                if (audioFile == null || audioFile.Length == 0)
                {
                    return Json(new
                    {
                        success = false,
                        text = "",
                        error = "Аудио не было передано."
                    });
                }

                var text = await _whisperService.TranscribeAsync(audioFile, cancellationToken);

                return Json(new
                {
                    success = true,
                    text,
                    error = ""
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    text = "",
                    error = ex.Message
                });
            }
        }
    }
}