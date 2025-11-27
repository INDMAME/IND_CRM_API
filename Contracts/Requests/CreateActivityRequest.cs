using System.ComponentModel.DataAnnotations;

namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// DTO para crear actividades en Axapta.
    /// </summary>
    public class CreateActivityRequest
    {
        [Required] public string accountNum { get; set; } 
        [Required] public string visitType { get; set; }
        [Required] public string userId { get; set; }
        [Required] public string description { get; set; } 
        [Required] public string transDate { get; set; }
        public string comentarios { get; set; }
        public string antecedentes { get; set; }
        public string conclusiones { get; set; }
    }
}
