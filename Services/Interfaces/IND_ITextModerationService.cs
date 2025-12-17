using System.Threading;
using System.Threading.Tasks;

namespace IND_CRM_API.Services.Interfaces
{
    /// <summary>
    /// Contrato para validar texto con la API de moderacion de OpenAI.
    /// </summary>
    public interface IND_ITextModerationService
    {
        /// <summary>
        /// Evalua el texto y devuelve si esta marcado como ofensivo o ilicito.
        /// </summary>
        /// <param name="text">Texto a evaluar.</param>
        /// <param name="openAiApiKey">API key de OpenAI (solo servidor).</param>
        /// <param name="model">Modelo de moderacion (opcional).</param>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        Task<ModerationResult> ModerateAsync(string text, string openAiApiKey, string model, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Resultado resumido de moderacion.
    /// </summary>
    public sealed class ModerationResult
    {
        /// <summary>Indica si OpenAI marco el contenido.</summary>
        public bool IsFlagged { get; set; }

        /// <summary>Resumen corto de categorias marcadas (si aplica).</summary>
        public string CategorySummary { get; set; }
    }
}
