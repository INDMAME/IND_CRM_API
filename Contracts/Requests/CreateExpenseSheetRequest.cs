using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Request data to create an expense sheet with lines.
    /// </summary>
    public class CreateExpenseSheetRequest
    {
        public string userId { get; set; }
        [Required] public string description { get; set; }
        [Required] public string currencyCode { get; set; }
        public decimal? exchRate { get; set; }
        public string projId { get; set; }
        [Required] public List<CreateExpenseSheetLineRequest> lines { get; set; }
    }

    /// <summary>
    /// Line payload for expense sheet creation.
    /// </summary>
    public class CreateExpenseSheetLineRequest
    {
        [Required] public string transDate { get; set; }
        [Required] public int? typeValue { get; set; }
        [Required] public string description { get; set; }
        public bool? internacional { get; set; }
        public bool? ticket { get; set; }
        [Required] public decimal? qty { get; set; }
        [Required] public decimal? amount { get; set; }
        public string projId { get; set; }
        public string indAttachFiles { get; set; }
    }
}
