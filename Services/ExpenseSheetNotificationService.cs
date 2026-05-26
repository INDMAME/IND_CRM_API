using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using AxaptaCOMConnector;
using IND_CRM_API.Contracts.Notifications;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Helpers;
using IND_CRM_API.Services.Interfaces;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Builds and sends best-effort expense sheet notification emails through the internal API.
    /// </summary>
    public class ExpenseSheetNotificationService : IExpenseSheetNotificationService
    {
        private const int StatusInReview = 1;
        private const int StatusApproved = 2;
        private const string EventApprovalRequested = "ExpenseSheetApprovalRequested";
        private const string EventApproved = "ExpenseSheetApproved";
        private const string SourceProcess = "ExpenseSheetNotifications";
        private const string AggregateType = "ExpenseSheet";

        private readonly IAxaptaSessionManager _sessionManager;
        private readonly IInternalMailClient _mailClient;
        private readonly IAxLogger _logger;
        private readonly string _webBaseUrl;
        private readonly HashSet<string> _enabledTransitions;

        public ExpenseSheetNotificationService(
            IAxaptaSessionManager sessionManager,
            IInternalMailClient mailClient,
            IAxLogger logger)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _mailClient = mailClient ?? throw new ArgumentNullException(nameof(mailClient));
            _logger = logger ?? new FileAxLogger();
            IsEnabled = AppSettingsHelper.GetBoolSetting(
                "ExpenseNotifications:Enabled",
                false,
                "INDCRM_EXPENSE_NOTIFICATIONS_ENABLED");
            _webBaseUrl = NormalizeBaseUrl(AppSettingsHelper.GetSetting("ApiSettings:WebBaseUrl", "INDCRM_WEB_BASE_URL"));
            _enabledTransitions = ReadEnabledTransitions();
        }

        public bool IsEnabled { get; private set; }

        public ExpenseSheetNotificationResult NotifyStatusChanged(ExpenseSheetStatusChangedNotification notification)
        {
            if (!IsEnabled)
                return ExpenseSheetNotificationResult.Skip("disabled");

            if (notification == null || notification.Before == null || notification.After == null)
                return ExpenseSheetNotificationResult.Skip("missing-transition-snapshot");

            var beforeStatus = notification.Before.ExpenseSheetStatus;
            var afterStatus = notification.After.ExpenseSheetStatus;
            if (!beforeStatus.HasValue || !afterStatus.HasValue)
                return ExpenseSheetNotificationResult.Skip("missing-status");

            if (beforeStatus.Value == afterStatus.Value)
                return ExpenseSheetNotificationResult.Skip("status-not-changed");

            var eventType = ResolveEventType(afterStatus.Value);
            if (string.IsNullOrWhiteSpace(eventType))
                return ExpenseSheetNotificationResult.Skip("transition-not-notifiable");

            if (!_enabledTransitions.Contains(eventType))
                return ExpenseSheetNotificationResult.Skip("transition-disabled");

            var idempotencyKey = BuildIdempotencyKey(eventType, notification.CompanyId, notification.HojaGastosId, afterStatus.Value);

            try
            {
                if (string.IsNullOrWhiteSpace(_webBaseUrl))
                {
                    LogSkip(notification, eventType, "web-base-url-missing");
                    return ExpenseSheetNotificationResult.Skip("web-base-url-missing");
                }

                if (!_mailClient.IsConfigured)
                {
                    LogSkip(notification, eventType, "internal-mail-not-configured");
                    return ExpenseSheetNotificationResult.Skip("internal-mail-not-configured");
                }

                var senderUserId = ResolveSenderUserId(eventType, notification);
                var sender = ResolvePersonaEmail(
                    notification.AuthenticatedUsername,
                    notification.CompanyId,
                    senderUserId,
                    notification.TraceId);

                if (sender == null || string.IsNullOrWhiteSpace(sender.Email) || !IsValidEmail(sender.Email))
                {
                    LogSkip(notification, eventType, "sender-email-missing");
                    return ExpenseSheetNotificationResult.Skip("sender-email-missing");
                }

                var recipients = ResolveRecipients(
                    notification.AuthenticatedUsername,
                    notification.CompanyId,
                    notification.HojaGastosId,
                    eventType,
                    notification.ActorAxUserId,
                    notification.TraceId,
                    sender.Email);

                if (recipients.Count == 0)
                {
                    LogSkip(notification, eventType, "recipient-email-missing");
                    return ExpenseSheetNotificationResult.Skip("recipient-email-missing");
                }

                var link = BuildExpenseSheetWebLink(notification.CompanyId, notification.HojaGastosId, "crm-api");
                var subject = BuildSubject(eventType, notification.HojaGastosId, notification.Language);
                var textBody = BuildTextBody(eventType, notification, link, notification.Language);
                var htmlBody = BuildHtmlBody(eventType, notification, link, notification.Language);

                var request = new InternalMailRequest
                {
                    Company = notification.CompanyId,
                    From = new InternalMailAddress { Email = sender.Email, DisplayName = sender.DisplayName },
                    To = recipients.Select(r => new InternalMailAddress { Email = r.Email, DisplayName = r.DisplayName }).ToList(),
                    Subject = subject,
                    HtmlBody = htmlBody,
                    TextBody = textBody,
                    SaveToSentItems = false,
                    SourceSystem = "IND_CRM_API",
                    SourceProcess = SourceProcess,
                    EventType = eventType,
                    AggregateType = AggregateType,
                    AggregateId = notification.HojaGastosId,
                    IdempotencyKey = idempotencyKey,
                    CorrelationId = notification.TraceId
                };

                var sendResult = _mailClient.Send(request);
                _logger.Log(
                    $"[EXPENSE-NOTIFY] event={eventType} hojaGastosId={notification.HojaGastosId} company={notification.CompanyId} attempted=true accepted={sendResult.AcceptedByProvider} recipients={recipients.Count} traceId={notification.TraceId}");

                return new ExpenseSheetNotificationResult
                {
                    Attempted = true,
                    AcceptedByProvider = sendResult.AcceptedByProvider,
                    Skipped = false,
                    EventType = eventType,
                    ErrorCode = sendResult.ErrorCode,
                    Reason = sendResult.Message,
                    RecipientCount = recipients.Count,
                    IdempotencyKey = idempotencyKey
                };
            }
            catch (Exception ex)
            {
                _logger.Log(
                    $"[EXPENSE-NOTIFY] event={eventType} hojaGastosId={notification.HojaGastosId} company={notification.CompanyId} failed error={ex.Message} traceId={notification.TraceId}",
                    AxaptaSessionManager.LogLevel.Warning);

                return new ExpenseSheetNotificationResult
                {
                    Attempted = true,
                    AcceptedByProvider = false,
                    Skipped = false,
                    EventType = eventType,
                    ErrorCode = "EXPENSE_NOTIFICATION_FAILED",
                    Reason = ex.Message,
                    RecipientCount = 0,
                    IdempotencyKey = idempotencyKey
                };
            }
        }

        private static string ResolveSenderUserId(string eventType, ExpenseSheetStatusChangedNotification notification)
        {
            // Approval request is sent by the expense sheet owner to the actor who will approve it.
            if (string.Equals(eventType, EventApprovalRequested, StringComparison.OrdinalIgnoreCase))
                return notification?.After?.UserId;

            // Approval confirmation is sent by the actor to the expense sheet owner.
            return notification?.ActorAxUserId;
        }

        private PersonaEmail ResolvePersonaEmail(string username, string companyId, string userId, string traceId)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(userId))
                return null;

            var ax = _sessionManager.GetAxInstanceForUser(username);
            var con = ax.CreateContainer();
            con.Append(companyId);
            con.Append(userId);

            var resultObj = ax.CallStaticClassMethod(
                "INDCRMExpenseSheetService",
                "getPersonaEmailByUserIdForApi",
                con);

            bool success;
            string message;
            List<string> extras;
            IAxaptaContainer lines;
            if (!TryReadHeader(resultObj as IAxaptaContainer, out success, out message, out extras, out lines) || !success)
            {
                _logger.Log($"[EXPENSE-NOTIFY] persona email not resolved user={userId} message={message} traceId={traceId}", AxaptaSessionManager.LogLevel.Warning);
                return null;
            }

            return new PersonaEmail
            {
                Email = extras.Count >= 1 ? extras[0] : string.Empty,
                DisplayName = extras.Count >= 2 ? extras[1] : string.Empty
            };
        }

        private List<ExpenseSheetEmailRecipient> ResolveRecipients(
            string username,
            string companyId,
            string hojaGastosId,
            string eventType,
            string actorAxUserId,
            string traceId,
            string senderEmail)
        {
            var resolved = new List<ExpenseSheetEmailRecipient>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var ax = _sessionManager.GetAxInstanceForUser(username);
            var con = ax.CreateContainer();
            con.Append(companyId);
            con.Append(hojaGastosId);
            con.Append(eventType);
            con.Append(actorAxUserId);

            var resultObj = ax.CallStaticClassMethod(
                "INDCRMExpenseSheetService",
                "getExpenseSheetNotificationRecipients",
                con);

            bool success;
            string message;
            List<string> extras;
            IAxaptaContainer recipientsCon;
            if (!TryReadHeader(resultObj as IAxaptaContainer, out success, out message, out extras, out recipientsCon) || !success)
            {
                _logger.Log($"[EXPENSE-NOTIFY] recipients not resolved event={eventType} hojaGastosId={hojaGastosId} message={message} traceId={traceId}", AxaptaSessionManager.LogLevel.Warning);
                return resolved;
            }

            var count = AxContainerReadHelper.SafeLength(recipientsCon);
            for (var i = 1; i <= count; i++)
            {
                var row = AxContainerReadHelper.SafePeekContainer(recipientsCon, i);
                if (row == null || AxContainerReadHelper.SafeLength(row) < 1)
                    continue;

                var email = AxContainerReadHelper.SafeString(row, 1);
                var displayName = AxContainerReadHelper.SafeLength(row) >= 2 ? AxContainerReadHelper.SafeString(row, 2) : string.Empty;
                var role = AxContainerReadHelper.SafeLength(row) >= 3 ? AxContainerReadHelper.SafeString(row, 3) : string.Empty;
                var userId = AxContainerReadHelper.SafeLength(row) >= 4 ? AxContainerReadHelper.SafeString(row, 4) : string.Empty;

                if (!IsValidEmail(email))
                    continue;

                if (string.Equals(email, senderEmail, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!seen.Add(email.Trim()))
                    continue;

                resolved.Add(new ExpenseSheetEmailRecipient
                {
                    Email = email.Trim(),
                    DisplayName = displayName,
                    Role = role,
                    UserId = userId
                });
            }

            return resolved;
        }

        private string BuildExpenseSheetWebLink(string companyId, string hojaGastosId, string source)
        {
            return _webBaseUrl.TrimEnd('/') +
                   "/Gastos/ExpenseSheetLink?hojaGastosId=" + Uri.EscapeDataString(hojaGastosId ?? string.Empty) +
                   "&targetCompanyId=" + Uri.EscapeDataString(companyId ?? string.Empty) +
                   "&source=" + Uri.EscapeDataString(source ?? string.Empty);
        }

        private static string ResolveEventType(int afterStatus)
        {
            if (afterStatus == StatusInReview)
                return EventApprovalRequested;
            if (afterStatus == StatusApproved)
                return EventApproved;
            return null;
        }

        private static string BuildIdempotencyKey(string eventType, string companyId, string hojaGastosId, int afterStatus)
        {
            return string.Format("{0}:{1}:{2}:{3}", eventType, companyId, hojaGastosId, afterStatus);
        }

        private static string BuildSubject(string eventType, string hojaGastosId, string language)
        {
            var lang = NormalizeLanguage(language);
            if (eventType == EventApprovalRequested)
            {
                if (lang == "en") return "Expense sheet " + hojaGastosId + " requires review";
                if (lang == "it") return "Nota spese " + hojaGastosId + " richiede revisione";
                if (lang == "pt") return "Folha de despesas " + hojaGastosId + " requer aprovacao";
                if (lang == "eu-ES") return "Gastu orria " + hojaGastosId + " berrikusi behar da";
                if (lang == "zh-Hans") return "Expense sheet " + hojaGastosId + " requires review";
                return "Hoja de gastos " + hojaGastosId + " requiere revision";
            }

            if (lang == "en") return "Expense sheet " + hojaGastosId + " was approved";
            if (lang == "it") return "Nota spese " + hojaGastosId + " approvata";
            if (lang == "pt") return "Folha de despesas " + hojaGastosId + " aprovada";
            if (lang == "eu-ES") return "Gastu orria " + hojaGastosId + " onartu da";
            if (lang == "zh-Hans") return "Expense sheet " + hojaGastosId + " was approved";
            return "Hoja de gastos " + hojaGastosId + " aprobada";
        }

        private static string BuildTextBody(string eventType, ExpenseSheetStatusChangedNotification notification, string link, string language)
        {
            var message = BuildMessage(eventType, notification.HojaGastosId, language);
            return message + Environment.NewLine + Environment.NewLine + link;
        }

        private static string BuildHtmlBody(string eventType, ExpenseSheetStatusChangedNotification notification, string link, string language)
        {
            var message = WebUtility.HtmlEncode(BuildMessage(eventType, notification.HojaGastosId, language));
            var safeLink = WebUtility.HtmlEncode(link);
            return "<p>" + message + "</p><p><a href=\"" + safeLink + "\">" + safeLink + "</a></p>";
        }

        private static string BuildMessage(string eventType, string hojaGastosId, string language)
        {
            var lang = NormalizeLanguage(language);
            if (eventType == EventApprovalRequested)
            {
                if (lang == "en") return "Expense sheet " + hojaGastosId + " is ready for review.";
                if (lang == "it") return "La nota spese " + hojaGastosId + " e pronta per la revisione.";
                if (lang == "pt") return "A folha de despesas " + hojaGastosId + " esta pronta para revisao.";
                if (lang == "eu-ES") return "Gastu orria " + hojaGastosId + " berrikusteko prest dago.";
                if (lang == "zh-Hans") return "Expense sheet " + hojaGastosId + " is ready for review.";
                return "La hoja de gastos " + hojaGastosId + " esta lista para revision.";
            }

            if (lang == "en") return "Expense sheet " + hojaGastosId + " has been approved.";
            if (lang == "it") return "La nota spese " + hojaGastosId + " e stata approvata.";
            if (lang == "pt") return "A folha de despesas " + hojaGastosId + " foi aprovada.";
            if (lang == "eu-ES") return "Gastu orria " + hojaGastosId + " onartu da.";
            if (lang == "zh-Hans") return "Expense sheet " + hojaGastosId + " has been approved.";
            return "La hoja de gastos " + hojaGastosId + " ha sido aprobada.";
        }

        private HashSet<string> ReadEnabledTransitions()
        {
            var raw = AppSettingsHelper.GetSetting(
                "ExpenseNotifications:NotifyTransitions",
                "INDCRM_EXPENSE_NOTIFY_TRANSITIONS");

            if (string.IsNullOrWhiteSpace(raw))
                raw = EventApprovalRequested + "," + EventApproved;

            return new HashSet<string>(
                raw.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(x => x.Trim())
                   .Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);
        }

        private void LogSkip(ExpenseSheetStatusChangedNotification notification, string eventType, string reason)
        {
            _logger.Log(
                $"[EXPENSE-NOTIFY] skipped reason={reason} event={eventType} hojaGastosId={notification?.HojaGastosId} company={notification?.CompanyId} traceId={notification?.TraceId}",
                AxaptaSessionManager.LogLevel.Warning);
        }

        private static string NormalizeLanguage(string language)
        {
            var value = (language ?? string.Empty).Trim();
            if (value.IndexOf(',') >= 0)
                value = value.Split(',')[0].Trim();

            if (value.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return "en";
            if (value.StartsWith("eu", StringComparison.OrdinalIgnoreCase)) return "eu-ES";
            if (value.StartsWith("it", StringComparison.OrdinalIgnoreCase)) return "it";
            if (value.StartsWith("pt", StringComparison.OrdinalIgnoreCase)) return "pt";
            if (value.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";
            return "es-ES";
        }

        private static string NormalizeBaseUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var trimmed = value.Trim();
            Uri uri;
            return Uri.TryCreate(trimmed, UriKind.Absolute, out uri) ? trimmed.TrimEnd('/') : null;
        }

        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                var parsed = new MailAddress(email.Trim());
                return string.Equals(parsed.Address, email.Trim(), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadHeader(IAxaptaContainer root, out bool success, out string message, out List<string> extras, out IAxaptaContainer linesCon)
        {
            success = false;
            message = string.Empty;
            extras = new List<string>();
            linesCon = null;

            if (root == null)
                return false;

            var rootLen = AxContainerReadHelper.SafeLength(root);
            IAxaptaContainer headerCon = rootLen >= 2 ? AxContainerReadHelper.SafePeekContainer(root, 1) : root;
            linesCon = rootLen >= 2 ? AxContainerReadHelper.SafePeekContainer(root, 2) : null;

            var rowCon = AxContainerReadHelper.SafePeekContainer(headerCon, 1) ?? headerCon;
            if (rowCon == null || AxContainerReadHelper.SafeLength(rowCon) < 2)
                return false;

            success = ToBool(AxContainerReadHelper.SafeString(rowCon, 1));
            message = AxContainerReadHelper.SafeString(rowCon, 2);

            var len = AxContainerReadHelper.SafeLength(rowCon);
            for (var i = 3; i <= len; i++)
                extras.Add(AxContainerReadHelper.SafeString(rowCon, i));

            return true;
        }

        private static bool ToBool(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var normalized = value.Trim();
            return normalized == "1" ||
                   normalized.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class PersonaEmail
        {
            public string Email { get; set; }
            public string DisplayName { get; set; }
        }
    }
}
