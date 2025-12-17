using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IND_CRM_API.Services.Interfaces
{
    /// <summary>
    /// Contrato de servicio para transcribir audio a texto.
    /// Se usa desde el controlador para llamar a OpenAI sin exponer la API key al cliente.
    /// </summary>
    public interface IND_IAudioTranscriptionService
    {
        /// <summary>
        /// Envia el audio a OpenAI /v1/audio/transcriptions y devuelve solo el texto transcrito.
        /// </summary>
        /// <param name="audioStream">Stream de audio (debe ser legible).</param>
        /// <param name="fileName">Nombre original del archivo (se usa en el multipart).</param>
        /// <param name="openAiApiKey">API key de OpenAI (solo servidor). Nunca loguear este valor.</param>
        /// <param name="languageId">Codigo de idioma o "auto" para deteccion automatica.</param>
        /// <param name="temperature">Temperatura de muestreo (0-1). Usar 0 para salida mas determinista.</param>
        /// <param name="prompt">Prompt opcional de contexto para guiar vocabulario.</param>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        Task<string> TranscribeAsync(
            Stream audioStream,
            string fileName,
            string openAiApiKey,
            string languageId,
            double temperature,
            string prompt,
            CancellationToken cancellationToken);
    }
}
