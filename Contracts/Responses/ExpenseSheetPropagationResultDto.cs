namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// Result of applying expense sheet header defaults to existing lines.
    /// </summary>
    public class ExpenseSheetPropagationResultDto
    {
        /// <summary>Expense sheet identifier.</summary>
        public string HojaGastosId { get; set; }

        /// <summary>Applied propagation type.</summary>
        public string PropagationType { get; set; }

        /// <summary>Number of lines updated by AX.</summary>
        public int UpdatedLines { get; set; }

        /// <summary>Indicates whether line AmountMST values were recalculated.</summary>
        public bool RecalculateAmountMST { get; set; }
    }
}
