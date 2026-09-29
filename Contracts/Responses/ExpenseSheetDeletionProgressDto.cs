namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>Public deletion state without ticket ids, file addresses, or actor details.</summary>
    public sealed class ExpenseSheetDeletionProgressDto
    {
        public bool Exists { get; set; }
        public bool DeleteAttempted { get; set; }
        public bool SheetDeleted { get; set; }
        public bool Complete { get; set; }
        public bool MetadataAvailable { get; set; }
    }
}
