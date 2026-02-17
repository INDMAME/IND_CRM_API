using System.ComponentModel.DataAnnotations;

namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Request data to update one expense sheet line.
    /// </summary>
    public class UpdateExpenseSheetLineRequest
    {
        [Required] public string transDate { get; set; }
        [Required] public int? typeValue { get; set; }
        [Required] public string description { get; set; }
        public bool? internacional { get; set; }
        public bool? ticket { get; set; }
        [Required] public decimal? qty { get; set; }
        [Required] public decimal? Amount { get; set; }
        public string projId { get; set; }
        public string indAttachFiles { get; set; }
    }
}
