using System.ComponentModel.DataAnnotations;

namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// DTO para crear asistentes de visita.
    /// </summary>
    public class CreateVisitaAsistenteRequest
    {
        [Required] public string refRecIdActividad { get; set; }   // long en AX
        [Required] public string asistenteTipo { get; set; }       // enum AX como string
        [Required] public string asistenteId { get; set; }
        [Required] public string contactoRecId { get; set; }       // long en AX
        public string createdByUserId { get; set; }
    }
}
