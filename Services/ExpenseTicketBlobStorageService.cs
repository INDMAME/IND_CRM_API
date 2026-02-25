using System;
using System.IO;
using System.Linq;
using IND_CRM_API.Helpers;
using IND_CRM_API.Services.Interfaces;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Servicio de almacenamiento de imagenes de tickets sobre Azure Blob Storage.
    /// </summary>
    public class ExpenseTicketBlobStorageService : IExpenseTicketBlobStorageService
    {
        private const string ConnectionSettingKey = "AzureBlob:ConnectionString";
        private const string ConnectionEnvVar = "AZURE_BLOB_CONNECTION_STRING";
        private const string ContainerSettingKey = "AzureBlob:Container";
        private const string ContainerEnvVar = "AZURE_BLOB_CONTAINER";
        private const string PrefixSettingKey = "AzureBlob:BasePrefix";
        private const string PrefixEnvVar = "AZURE_BLOB_BASE_PREFIX";
        private const string DefaultContainer = "tickets";
        private const string DefaultPrefix = "tickets";

        private readonly IAxLogger _logger;

        /// <summary>
        /// Crea el servicio de almacenamiento de tickets.
        /// </summary>
        public ExpenseTicketBlobStorageService(IAxLogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Sube una imagen al blob y devuelve informacion de ubicacion.
        /// </summary>
        public TicketBlobUploadResult UploadTicketFile(
            string companyId,
            string axUserId,
            string fileId,
            string fileName,
            Stream content,
            string contentType)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("fileName es obligatorio.", nameof(fileName));

            var context = ResolveStorageContext();
            var blobName = BuildBlobName(context.BasePrefix, companyId, axUserId, fileName);
            var blob = context.Container.GetBlockBlobReference(blobName);

            blob.Properties.ContentType = ResolveContentType(contentType, fileName);
            blob.Metadata["companyId"] = SafeMetadataValue(companyId);
            blob.Metadata["axUserId"] = SafeMetadataValue(axUserId);
            blob.Metadata["fileId"] = SafeMetadataValue(fileId);

            if (content.CanSeek)
                content.Position = 0;

            blob.UploadFromStream(content);
            _logger?.Log($"[BLOB] UploadTicketFile fileId={fileId} blob={blobName}");

            return new TicketBlobUploadResult
            {
                BlobName = blobName,
                BlobUrl = blob.Uri.AbsoluteUri,
                ContainerName = context.Container.Name
            };
        }

        /// <summary>
        /// Elimina una imagen desde su URL absoluta.
        /// </summary>
        public bool DeleteTicketFileByUrl(string blobUrl)
        {
            if (string.IsNullOrWhiteSpace(blobUrl))
                return false;

            var context = ResolveStorageContext();
            if (!Uri.TryCreate(blobUrl.Trim(), UriKind.Absolute, out var uri))
                return false;

            if (!TryResolveBlobName(uri, context.Container.Name, out var blobName))
                return false;

            var blob = context.Container.GetBlockBlobReference(blobName);
            var deleted = blob.DeleteIfExists();
            _logger?.Log($"[BLOB] DeleteTicketFile blob={blobName} deleted={deleted}");
            return deleted;
        }

        private StorageContext ResolveStorageContext()
        {
            var connectionString = AppSettingsHelper.GetSetting(ConnectionSettingKey, ConnectionEnvVar);
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    "No se encontro configuracion de Azure Blob Storage. Defina AZURE_BLOB_CONNECTION_STRING.");

            if (!TryParseStorageConnectionString(connectionString, out var account))
                throw new InvalidOperationException("La cadena de conexion de Azure Blob Storage no es valida.");

            var containerNameRaw = AppSettingsHelper.GetSetting(ContainerSettingKey, ContainerEnvVar);
            var containerName = string.IsNullOrWhiteSpace(containerNameRaw)
                ? DefaultContainer
                : containerNameRaw.Trim().ToLowerInvariant();

            if (!IsValidContainerName(containerName))
                throw new InvalidOperationException("El nombre de contenedor de Azure Blob no es valido.");

            var prefixRaw = AppSettingsHelper.GetSetting(PrefixSettingKey, PrefixEnvVar);
            var basePrefix = NormalizePrefix(prefixRaw, DefaultPrefix);

            var client = account.CreateCloudBlobClient();
            var container = client.GetContainerReference(containerName);
            container.CreateIfNotExists();

            return new StorageContext
            {
                BasePrefix = basePrefix,
                Container = container
            };
        }

        // Tries to parse and auto-sanitize common copy/paste artifacts in the connection string.
        private bool TryParseStorageConnectionString(string rawConnectionString, out CloudStorageAccount account)
        {
            account = null;

            if (string.IsNullOrWhiteSpace(rawConnectionString))
                return false;

            if (CloudStorageAccount.TryParse(rawConnectionString, out account))
                return true;

            var sanitized = SanitizeConnectionString(rawConnectionString);
            if (string.Equals(sanitized, rawConnectionString, StringComparison.Ordinal))
                return false;

            if (!CloudStorageAccount.TryParse(sanitized, out account))
                return false;

            _logger?.Log("[BLOB] Connection string sanitized before parsing.", AxaptaSessionManager.LogLevel.Warning);
            return true;
        }

        // Normalizes key/value segments and removes accidental brackets around key names.
        private static string SanitizeConnectionString(string rawConnectionString)
        {
            if (string.IsNullOrWhiteSpace(rawConnectionString))
                return string.Empty;

            var segments = rawConnectionString
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => SanitizeConnectionSegment(segment))
                .Where(segment => !string.IsNullOrWhiteSpace(segment))
                .ToArray();

            return string.Join(";", segments);
        }

        private static string SanitizeConnectionSegment(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment))
                return string.Empty;

            var trimmed = segment.Trim();
            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex < 0)
                return trimmed.Trim('[', ']');

            var key = trimmed.Substring(0, separatorIndex).Trim().Trim('[', ']');
            var value = trimmed.Substring(separatorIndex + 1).Trim();

            if (value.Length >= 2)
            {
                var first = value[0];
                var last = value[value.Length - 1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                    value = value.Substring(1, value.Length - 2);
            }

            return string.Concat(key, "=", value);
        }

        private static string BuildBlobName(string basePrefix, string companyId, string axUserId, string fileName)
        {
            var safeCompany = NormalizePathSegment(companyId, "company");
            var safeUser = NormalizePathSegment(axUserId, "user");
            var safeFileName = NormalizeFileName(fileName);
            var nowUtc = DateTime.UtcNow;

            return string.Concat(
                basePrefix,
                "/",
                safeCompany,
                "/",
                safeUser,
                "/",
                nowUtc.ToString("yyyy"),
                "/",
                nowUtc.ToString("MM"),
                "/",
                safeFileName);
        }

        private static bool TryResolveBlobName(Uri blobUri, string expectedContainer, out string blobName)
        {
            blobName = string.Empty;
            if (blobUri == null)
                return false;

            var path = (blobUri.AbsolutePath ?? string.Empty).Trim('/');
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var segments = path.Split(new[] { '/' }, 2);
            if (segments.Length != 2)
                return false;

            var containerName = segments[0];
            if (!string.Equals(containerName, expectedContainer, StringComparison.OrdinalIgnoreCase))
                return false;

            blobName = segments[1];
            return !string.IsNullOrWhiteSpace(blobName);
        }

        private static string ResolveContentType(string contentType, string fileName)
        {
            if (!string.IsNullOrWhiteSpace(contentType))
                return contentType.Trim();

            var extension = Path.GetExtension(fileName ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
            switch (extension)
            {
                case "jpg":
                case "jpeg":
                    return "image/jpeg";
                case "png":
                    return "image/png";
                case "bmp":
                    return "image/bmp";
                case "gif":
                    return "image/gif";
                case "webp":
                    return "image/webp";
                case "pdf":
                    return "application/pdf";
                default:
                    return "application/octet-stream";
            }
        }

        private static string SafeMetadataValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return new string(value.Trim().Where(c => c >= 32 && c <= 126).ToArray());
        }

        private static string NormalizePrefix(string value, string fallback)
        {
            var raw = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            var segments = raw
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => NormalizePathSegment(s, string.Empty))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();

            if (segments.Length == 0)
                return fallback;

            return string.Join("/", segments);
        }

        private static string NormalizePathSegment(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            var raw = value.Trim();
            var chars = raw
                .Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_')
                .ToArray();

            var normalized = new string(chars);
            return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
        }

        private static string NormalizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "ticket.jpg";

            var fileName = Path.GetFileName(value.Trim());
            if (string.IsNullOrWhiteSpace(fileName))
                return "ticket.jpg";

            var invalidChars = Path.GetInvalidFileNameChars();
            var filtered = new string(fileName
                .Where(c => !invalidChars.Contains(c) && c != '/')
                .ToArray());

            return string.IsNullOrWhiteSpace(filtered) ? "ticket.jpg" : filtered;
        }

        private static bool IsValidContainerName(string containerName)
        {
            if (string.IsNullOrWhiteSpace(containerName))
                return false;

            if (containerName.Length < 3 || containerName.Length > 63)
                return false;

            if (!char.IsLetterOrDigit(containerName[0]) || !char.IsLetterOrDigit(containerName[containerName.Length - 1]))
                return false;

            for (int i = 0; i < containerName.Length; i++)
            {
                var c = containerName[i];
                var isValid = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-';
                if (!isValid)
                    return false;
            }

            return !containerName.Contains("--");
        }

        private sealed class StorageContext
        {
            public CloudBlobContainer Container { get; set; }
            public string BasePrefix { get; set; }
        }
    }
}
