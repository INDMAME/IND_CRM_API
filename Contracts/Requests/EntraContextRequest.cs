using System.ComponentModel.DataAnnotations;

namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// DTO para solicitar el contexto de Entra.
    /// </summary>
    public class EntraContextRequest
    {
        [Required]
        public string entraOid { get; set; }

        [Required]
        public string appCode { get; set; }
    }
}
