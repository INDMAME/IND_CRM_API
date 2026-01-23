using System.ComponentModel.DataAnnotations;

namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Request data to update an expense sheet header.
    /// </summary>
    public class UpdateExpenseSheetHeaderRequest
    {
        [Required] public string description { get; set; }
        [Required] public string currencyCode { get; set; }
        public decimal? exchRate { get; set; }
        [Required] public string projId { get; set; }
    }
}
