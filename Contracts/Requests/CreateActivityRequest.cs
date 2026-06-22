using System.ComponentModel.DataAnnotations;

namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// DTO para crear actividades en Axapta.
    /// </summary>
    public class CreateActivityRequest
    {
        [Required] public string accountNum { get; set; } 
        /// <summary>
        /// Numeric AX enum value for the visit type. Resolve active options through /api/crm/enums.
        /// </summary>
        [Required] public int? visitType { get; set; }
        public string userId { get; set; }
        public string createdByUserId { get; set; }
        [Required] public string description { get; set; } 
        [Required] public string transDate { get; set; }
        /// <summary>
        /// Numeric AX enum value for INDContactMethod. Resolve active options through /api/crm/enums.
        /// </summary>
        public int? contactMethod { get; set; }
        public string comentarios { get; set; }
        public string antecedentes { get; set; }
        public string conclusiones { get; set; }
    }
}
