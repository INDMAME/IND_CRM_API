namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Datos para eliminar un asistente vinculado a una actividad.
    /// </summary>
    public class DeleteVisitaAsistenteRequest
    {
        public string refRecIdActividad { get; set; }
        public string asistenteId { get; set; }
    }
}
