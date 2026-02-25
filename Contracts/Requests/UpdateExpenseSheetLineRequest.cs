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
        /// <summary>
        /// FileId del ticket asociado a la linea (INDFileId).
        /// </summary>
        public string fileId { get; set; }
        [Required] public decimal? qty { get; set; }
        /// <summary>
        /// Unit price. Amount is calculated internally in AX as qty * price.
        /// </summary>
        [Required] public decimal? price { get; set; }
        public string projId { get; set; }
        public string indAttachFiles { get; set; }
    }
}
