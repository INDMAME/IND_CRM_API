using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IND_CRM_API.Helpers
{
    /// <summary>
    /// Shared validation and normalization rules for expense ticket images.
    /// </summary>
    public static class ExpenseTicketImageHelper
    {
        public const int MaxImageBytes = 50 * 1024 * 1024; // 50 MB

        private static readonly HashSet<string> AllowedExtensionsInternal = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp"
        };

        private static readonly HashSet<string> AllowedContentTypesInternal = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/pjpeg",
            "image/png",
            "image/webp"
        };

        public static bool IsAllowedExtension(string extension)
        {
            return !string.IsNullOrWhiteSpace(extension) && AllowedExtensionsInternal.Contains(extension.Trim());
        }

        public static bool IsAllowedContentType(string contentType)
        {
            return !string.IsNullOrWhiteSpace(contentType) && AllowedContentTypesInternal.Contains(contentType.Trim());
        }

        public static string BuildDescriptionFromFileName(string fileName)
        {
            var rawName = Path.GetFileNameWithoutExtension(fileName ?? string.Empty) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawName))
                return "Ticket";

            var normalized = new string(rawName
                .Select(c => char.IsLetterOrDigit(c) ? c : ' ')
                .ToArray());

            var collapsed = string.Join(" ", normalized
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

            return string.IsNullOrWhiteSpace(collapsed) ? "Ticket" : collapsed.Trim();
        }
    }
}
