using IND_CRM_API.Contracts.Notifications;

namespace IND_CRM_API.Services.Interfaces
{
    /// <summary>
    /// Sends best-effort CRM expense sheet notifications after status transitions.
    /// </summary>
    public interface IExpenseSheetNotificationService
    {
        bool IsEnabled { get; }
        ExpenseSheetNotificationResult NotifyStatusChanged(ExpenseSheetStatusChangedNotification notification);
    }
}
