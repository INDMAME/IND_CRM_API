using IND_CRM_API.Contracts.Responses;
using System.Threading;
using System.Threading.Tasks;

namespace IND_CRM_API.Services.Interfaces
{
    /// <summary>
    /// Corrects and formats untrusted business text using server-owned instructions.
    /// </summary>
    public interface IND_ITextFormattingService
    {
        /// <summary>Formats the complete source text and rejects partial provider output.</summary>
        Task<FormatTextResponse> FormatAsync(string text, string languageId, CancellationToken cancellationToken);

        /// <summary>Gets the configured technical model profile for sanitized telemetry.</summary>
        string ModelProfile { get; }
    }
}
