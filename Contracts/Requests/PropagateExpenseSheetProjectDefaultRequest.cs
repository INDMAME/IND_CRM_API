namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Optional target used to atomically update the expense sheet header and its lines.
    /// </summary>
    public class PropagateExpenseSheetProjectDefaultRequest
    {
        /// <summary>
        /// Target project. Empty is a valid explicit target when projIdProvided is true.
        /// </summary>
        public string projId { get; set; }

        /// <summary>
        /// When true, AX uses projId instead of the current header project. When omitted, a non-null projId remains explicit for legacy clients.
        /// </summary>
        public bool? projIdProvided { get; set; }
    }
}
