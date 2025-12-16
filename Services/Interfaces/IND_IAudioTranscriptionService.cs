using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IND_CRM_API.Services.Interfaces
{
    /// <summary>
    /// Audio transcription service abstraction.
    /// This is used by the API controller to call OpenAI speech-to-text without exposing the API key to clients.
    /// </summary>
    public interface IND_IAudioTranscriptionService
    {
        /// <summary>
        /// Sends an audio stream to OpenAI /v1/audio/transcriptions and returns only the transcribed text.
        /// </summary>
        /// <param name="audioStream">Input audio stream (mp3/m4a). The stream must be readable.</param>
        /// <param name="fileName">Original file name (used as multipart file name).</param>
        /// <param name="openAiApiKey">OpenAI API key (server-side only). Never log this value.</param>
        /// <param name="languageId">Language hint or "auto" to let the model detect it.</param>
        /// <param name="cancellationToken">Request cancellation token.</param>
        Task<string> TranscribeAsync(
            Stream audioStream,
            string fileName,
            string openAiApiKey,
            string languageId,
            CancellationToken cancellationToken);
    }
}

