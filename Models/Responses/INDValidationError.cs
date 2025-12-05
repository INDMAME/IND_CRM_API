namespace IND_CRM_API.Models.Responses
{
    /// <summary>
    /// Describe un problema de validacion en un campo especifico.
    /// </summary>
    public class IndValidationError
    {
        /// <summary>Nombre del campo que fallo la validacion.</summary>
        public string Field { get; set; }

        /// <summary>Motivo del error de validacion.</summary>
        public string Message { get; set; }
    }
}
