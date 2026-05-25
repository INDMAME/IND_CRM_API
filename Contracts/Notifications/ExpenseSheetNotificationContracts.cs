using System.Collections.Generic;
using IND_CRM_API.Contracts.Responses;

namespace IND_CRM_API.Contracts.Notifications
{
    /// <summary>
    /// Status transition context used to decide and send expense sheet notifications.
    /// </summary>
    public class ExpenseSheetStatusChangedNotification
    {
        public string CompanyId { get; set; }
        public string HojaGastosId { get; set; }
        public string ActorAxUserId { get; set; }
        public string AuthenticatedUsername { get; set; }
        public string Language { get; set; }
        public string TraceId { get; set; }
        public ExpenseSheetDetailDto Before { get; set; }
        public ExpenseSheetDetailDto After { get; set; }
    }

    /// <summary>
    /// Resolved recipient for an expense sheet email notification.
    /// </summary>
    public class ExpenseSheetEmailRecipient
    {
        public string Email { get; set; }
        public string DisplayName { get; set; }
        public string Role { get; set; }
        public string UserId { get; set; }
    }

    /// <summary>
    /// Best-effort notification result for logging and diagnostics.
    /// </summary>
    public class ExpenseSheetNotificationResult
    {
        public bool Attempted { get; set; }
        public bool AcceptedByProvider { get; set; }
        public bool Skipped { get; set; }
        public string EventType { get; set; }
        public string Reason { get; set; }
        public string ErrorCode { get; set; }
        public int RecipientCount { get; set; }
        public string IdempotencyKey { get; set; }

        public static ExpenseSheetNotificationResult Skip(string reason)
        {
            return new ExpenseSheetNotificationResult
            {
                Attempted = false,
                AcceptedByProvider = false,
                Skipped = true,
                Reason = reason
            };
        }
    }
}
