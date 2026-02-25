using System.IO;

namespace IND_CRM_API.Services.Interfaces
{
    /// <summary>
    /// Contrato para almacenar y eliminar imagenes de tickets en Azure Blob Storage.
    /// </summary>
    public interface IExpenseTicketBlobStorageService
    {
        /// <summary>
        /// Sube una imagen de ticket al contenedor configurado y devuelve su URL final.
        /// </summary>
        TicketBlobUploadResult UploadTicketFile(
            string companyId,
            string axUserId,
            string fileId,
            string fileName,
            Stream content,
            string contentType);

        /// <summary>
        /// Elimina una imagen de ticket desde su URL absoluta.
        /// </summary>
        bool DeleteTicketFileByUrl(string blobUrl);
    }

    /// <summary>
    /// Resultado de la subida de imagen a Azure Blob.
    /// </summary>
    public sealed class TicketBlobUploadResult
    {
        /// <summary>Nombre interno del blob dentro del contenedor.</summary>
        public string BlobName { get; set; }

        /// <summary>URL absoluta del blob almacenado.</summary>
        public string BlobUrl { get; set; }

        /// <summary>Nombre del contenedor utilizado.</summary>
        public string ContainerName { get; set; }
    }
}
