using IND_CRM_API.Helpers;
using IND_CRM_API.Services.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Removes common direct identifiers before text reaches OpenAI or local review files.
    /// </summary>
    public static class HelpTextRedactor
    {
        private static readonly Regex EmailPattern = new Regex(
            @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex UrlPattern = new Regex(
            @"\b(?:https?://|www\.)\S+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex GuidPattern = new Regex(
            @"\b[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex LongNumberPattern = new Regex(
            @"(?<!\w)\+?[0-9][0-9 .()/-]{7,}[0-9](?!\w)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex BearerPattern = new Regex(
            @"\b(?:Bearer\s+)?[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{10,}(?:\.[A-Za-z0-9_-]{10,})?\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex WhitespacePattern = new Regex(
            @"[ \t]{2,}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string Redact(string value, int maxCharacters)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var redacted = BearerPattern.Replace(value, "[TOKEN]");
            redacted = EmailPattern.Replace(redacted, "[EMAIL]");
            redacted = UrlPattern.Replace(redacted, "[URL]");
            redacted = GuidPattern.Replace(redacted, "[ID]");
            redacted = LongNumberPattern.Replace(redacted, "[NUMBER]");
            redacted = WhitespacePattern.Replace(redacted, " ").Trim();
            return redacted.Length <= maxCharacters ? redacted : redacted.Substring(0, maxCharacters).TrimEnd();
        }
    }

    /// <summary>
    /// Issues short-lived HMAC tokens so feedback can only target the caller's answer.
    /// </summary>
    public sealed class HelpFeedbackTokenService : IHelpFeedbackTokenService
    {
        private const int MaxConsumedTokenHashes = 50000;
        private const int CleanupInspectionLimit = 256;

        private readonly byte[] _secret;
        private readonly int _lifetimeMinutes;
        private readonly object _consumptionSync = new object();
        private readonly ConcurrentDictionary<string, long> _consumedTokenHashes =
            new ConcurrentDictionary<string, long>(StringComparer.Ordinal);

        public HelpFeedbackTokenService()
            : this(
                AppSettingsHelper.GetSetting(
                    "HelpAssistant:FeedbackHmacSecret",
                    "INDCRM_HELP_FEEDBACK_HMAC_SECRET"),
                AppSettingsHelper.GetIntSetting("HelpAssistant:FeedbackTokenMinutes", 60))
        {
        }

        // Supports an isolated directed test without mutating machine configuration.
        internal HelpFeedbackTokenService(string secret, int lifetimeMinutes)
        {
            _secret = string.IsNullOrWhiteSpace(secret) || secret.Length < 32
                ? null
                : Encoding.UTF8.GetBytes(secret);
            _lifetimeMinutes = Math.Max(5, Math.Min(240,
                lifetimeMinutes));
        }

        public bool IsConfigured => _secret != null;

        public string Create(string interactionId, string userKey)
        {
            if (!IsConfigured || !Guid.TryParse(interactionId, out _) || string.IsNullOrWhiteSpace(userKey))
                return null;

            var payload = new JObject
            {
                ["i"] = interactionId,
                ["u"] = Fingerprint(userKey),
                ["e"] = DateTimeOffset.UtcNow.AddMinutes(_lifetimeMinutes).ToUnixTimeSeconds()
            }.ToString(Formatting.None);
            var encodedPayload = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
            var signature = Base64UrlEncode(Sign(encodedPayload));
            return encodedPayload + "." + signature;
        }

        public bool TryConsume(string token, string userKey, out HelpFeedbackTokenPayload payload)
        {
            payload = null;
            if (!IsConfigured || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(userKey))
                return false;
            try
            {
                var parts = token.Split('.');
                if (parts.Length != 2 || !FixedTimeEquals(Sign(parts[0]), Base64UrlDecode(parts[1])))
                    return false;
                var json = JObject.Parse(Encoding.UTF8.GetString(Base64UrlDecode(parts[0])));
                var interactionId = json.Value<string>("i");
                var userFingerprint = json.Value<string>("u");
                var expiresSeconds = json.Value<long?>("e");
                if (!Guid.TryParse(interactionId, out _) || !expiresSeconds.HasValue ||
                    !string.Equals(userFingerprint, Fingerprint(userKey), StringComparison.Ordinal) ||
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expiresSeconds.Value)
                {
                    return false;
                }

                var validatedPayload = new HelpFeedbackTokenPayload
                {
                    InteractionId = interactionId,
                    UserFingerprint = userFingerprint,
                    ExpiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(expiresSeconds.Value).UtcDateTime
                };
                if (!TryMarkConsumed(token, expiresSeconds.Value))
                    return false;
                payload = validatedPayload;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Stores only a token digest and atomically rejects replay within this process.
        private bool TryMarkConsumed(string token, long expiresAtUnixSeconds)
        {
            var tokenHash = ComputeTokenHash(token);
            var nowUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            lock (_consumptionSync)
            {
                CleanupExpiredHashes(nowUnixSeconds, CleanupInspectionLimit);
                if (_consumedTokenHashes.ContainsKey(tokenHash))
                    return false;
                if (_consumedTokenHashes.Count >= MaxConsumedTokenHashes)
                    return false;
                return _consumedTokenHashes.TryAdd(tokenHash, expiresAtUnixSeconds);
            }
        }

        // Inspects a bounded number of entries so cleanup cannot dominate feedback latency.
        private void CleanupExpiredHashes(long nowUnixSeconds, int inspectionLimit)
        {
            var inspected = 0;
            foreach (var entry in _consumedTokenHashes)
            {
                if (inspected++ >= inspectionLimit)
                    break;
                if (entry.Value <= nowUnixSeconds)
                    _consumedTokenHashes.TryRemove(entry.Key, out _);
            }
        }

        // Produces the non-reversible cache key; the signed token is never retained.
        private static string ComputeTokenHash(string token)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(token))).Replace("-", string.Empty);
        }

        private string Fingerprint(string userKey)
        {
            return Base64UrlEncode(Sign("feedback-user|" + userKey.Trim().ToLowerInvariant()));
        }

        private byte[] Sign(string value)
        {
            using (var hmac = new HMACSHA256(_secret))
                return hmac.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            var difference = 0;
            for (var index = 0; index < left.Length; index++)
                difference |= left[index] ^ right[index];
            return difference == 0;
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static byte[] Base64UrlDecode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            var normalized = value.Replace('-', '+').Replace('_', '/');
            switch (normalized.Length % 4)
            {
                case 2: normalized += "=="; break;
                case 3: normalized += "="; break;
            }
            return Convert.FromBase64String(normalized);
        }
    }

    /// <summary>
    /// Writes separate daily NDJSON metrics and redacted review samples on a best-effort basis.
    /// </summary>
    public sealed class HelpAnalyticsStore : IHelpAnalyticsStore
    {
        private const string SchemaVersion = "1.0";
        private static readonly object FileSync = new object();
        private static DateTime _lastPurgeDateUtc = DateTime.MinValue;
        private static DateTime _sampleCounterDateUtc = DateTime.MinValue;
        private static int _successSamplesToday;

        private readonly IAxLogger _logger;
        private readonly string _rootPath;
        private readonly byte[] _hmacSecret;
        private readonly bool _textCaptureEnabled;
        private readonly int _successSamplePercent;
        private readonly int _successSampleMaxPerDay;
        private readonly int _textRetentionDays;
        private readonly int _metricRetentionDays;
        private readonly int _aggregateRetentionDays;
        private readonly int _pendingQuestionMinutes;
        private readonly ConcurrentDictionary<string, PendingQuestion> _pendingQuestions =
            new ConcurrentDictionary<string, PendingQuestion>(StringComparer.OrdinalIgnoreCase);

        public HelpAnalyticsStore(IAxLogger logger)
        {
            _logger = logger ?? new FileAxLogger();
            _rootPath = ResolveRootPath();
            var secret = AppSettingsHelper.GetSetting(
                "HelpAssistant:AnalyticsHmacSecret",
                "INDCRM_HELP_ANALYTICS_HMAC_SECRET");
            _hmacSecret = string.IsNullOrWhiteSpace(secret) || secret.Length < 32
                ? null
                : Encoding.UTF8.GetBytes(secret);
            var captureRequested = AppSettingsHelper.GetBoolSetting(
                "HelpAssistant:AnalyticsTextCaptureEnabled",
                false,
                "INDCRM_HELP_ANALYTICS_TEXT_ENABLED");
            var aclReady = AppSettingsHelper.GetBoolSetting(
                "HelpAssistant:AnalyticsAclReady",
                false,
                "INDCRM_HELP_ANALYTICS_ACL_READY");
            var encrypted = AppSettingsHelper.GetBoolSetting(
                "HelpAssistant:AnalyticsVolumeEncrypted",
                false,
                "INDCRM_HELP_ANALYTICS_VOLUME_ENCRYPTED");
            _textCaptureEnabled = captureRequested && aclReady && encrypted && _hmacSecret != null;
            _successSamplePercent = Math.Max(0, Math.Min(100,
                AppSettingsHelper.GetIntSetting("HelpAssistant:AnalyticsSuccessSamplePercent", 10)));
            _successSampleMaxPerDay = Math.Max(0,
                AppSettingsHelper.GetIntSetting("HelpAssistant:AnalyticsSuccessSampleMaxPerDay", 200));
            _textRetentionDays = Math.Max(1,
                AppSettingsHelper.GetIntSetting("HelpAssistant:AnalyticsTextRetentionDays", 90));
            _metricRetentionDays = Math.Max(_textRetentionDays,
                AppSettingsHelper.GetIntSetting("HelpAssistant:AnalyticsMetricRetentionDays", 180));
            _aggregateRetentionDays = Math.Max(_metricRetentionDays,
                AppSettingsHelper.GetIntSetting("HelpAssistant:AnalyticsAggregateRetentionDays", 730));
            _pendingQuestionMinutes = Math.Max(5, Math.Min(240,
                AppSettingsHelper.GetIntSetting("HelpAssistant:FeedbackTokenMinutes", 60)));

            _logger.Log(
                "[HELP-ANALYTICS] Initialized textCapture=" + _textCaptureEnabled,
                AxaptaSessionManager.LogLevel.Info);
        }

        public void RecordInteraction(HelpInteractionAnalyticsEvent analyticsEvent)
        {
            if (analyticsEvent == null || string.IsNullOrWhiteSpace(analyticsEvent.InteractionId))
                return;
            try
            {
                var now = DateTime.UtcNow;
                var common = new JObject
                {
                    ["schemaVersion"] = SchemaVersion,
                    ["eventType"] = "interaction",
                    ["occurredAtUtc"] = now.ToString("o", CultureInfo.InvariantCulture),
                    ["interactionId"] = analyticsEvent.InteractionId,
                    ["userPseudonym"] = BuildMonthlyPseudonym(analyticsEvent.UserKey, now),
                    ["knowledgeVersion"] = analyticsEvent.KnowledgeVersion,
                    ["resolution"] = analyticsEvent.Resolution,
                    ["responseLocale"] = analyticsEvent.ResponseLocale,
                    ["retrievalMode"] = analyticsEvent.RetrievalMode,
                    ["confidence"] = analyticsEvent.Confidence,
                    ["topicIds"] = new JArray(analyticsEvent.TopicIds ?? new List<string>()),
                    ["candidateTopicIds"] = new JArray(analyticsEvent.CandidateTopicIds ?? new List<string>()),
                    ["quickAnswerUsed"] = analyticsEvent.QuickAnswerUsed,
                    ["inputTokens"] = analyticsEvent.InputTokens,
                    ["outputTokens"] = analyticsEvent.OutputTokens,
                    ["cachedInputTokens"] = analyticsEvent.CachedInputTokens,
                    ["latencyMilliseconds"] = analyticsEvent.LatencyMilliseconds
                };
                Append("events", "help-metrics-" + now.ToString("yyyyMMdd") + ".ndjson", common);

                if (_textCaptureEnabled && !analyticsEvent.IsProblematic &&
                    !string.IsNullOrWhiteSpace(analyticsEvent.RedactedQuestion))
                {
                    RememberPendingQuestion(analyticsEvent.InteractionId, analyticsEvent.RedactedQuestion, now);
                }

                var includeSuccessSample = !analyticsEvent.IsProblematic &&
                                           IsDeterministicSuccessSample(analyticsEvent.InteractionId) &&
                                           TryReserveSuccessSample(now);
                if (_textCaptureEnabled && (analyticsEvent.IsProblematic || includeSuccessSample))
                {
                    var review = (JObject)common.DeepClone();
                    review["eventType"] = "interaction-review";
                    review["sampleReason"] = analyticsEvent.IsProblematic ? "problematic" : "success-sample";
                    review["redactedQuestion"] = HelpTextRedactor.Redact(analyticsEvent.RedactedQuestion, 1200);
                    Append("review", "help-review-" + now.ToString("yyyyMMdd") + ".ndjson", review);
                }
                PurgeIfDue(now);
            }
            catch (Exception ex)
            {
                LogBestEffortFailure(ex);
            }
        }

        public void RecordFeedback(HelpFeedbackAnalyticsEvent analyticsEvent)
        {
            if (analyticsEvent == null || string.IsNullOrWhiteSpace(analyticsEvent.InteractionId))
                return;
            try
            {
                var now = DateTime.UtcNow;
                PendingQuestion pendingQuestion;
                _pendingQuestions.TryRemove(analyticsEvent.InteractionId, out pendingQuestion);
                var metric = new JObject
                {
                    ["schemaVersion"] = SchemaVersion,
                    ["eventType"] = "feedback",
                    ["occurredAtUtc"] = now.ToString("o", CultureInfo.InvariantCulture),
                    ["interactionId"] = analyticsEvent.InteractionId,
                    ["userPseudonym"] = BuildMonthlyPseudonym(analyticsEvent.UserKey, now),
                    ["helpful"] = analyticsEvent.Helpful,
                    ["reason"] = analyticsEvent.Reason
                };
                Append("events", "help-metrics-" + now.ToString("yyyyMMdd") + ".ndjson", metric);

                if (_textCaptureEnabled && !analyticsEvent.Helpful &&
                    (!string.IsNullOrWhiteSpace(analyticsEvent.RedactedComment) || pendingQuestion != null))
                {
                    var review = (JObject)metric.DeepClone();
                    review["eventType"] = "feedback-review";
                    review["redactedQuestion"] = pendingQuestion?.Question;
                    review["redactedComment"] = HelpTextRedactor.Redact(analyticsEvent.RedactedComment, 1000);
                    Append("review", "help-review-" + now.ToString("yyyyMMdd") + ".ndjson", review);
                }
                PurgeIfDue(now);
            }
            catch (Exception ex)
            {
                LogBestEffortFailure(ex);
            }
        }

        private void Append(string directoryName, string fileName, JObject value)
        {
            lock (FileSync)
            {
                var directory = Path.Combine(_rootPath, directoryName);
                Directory.CreateDirectory(directory);
                var target = Path.Combine(directory, fileName);
                using (var stream = new FileStream(target, FileMode.Append, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    writer.WriteLine(value.ToString(Formatting.None));
            }
        }

        private void PurgeIfDue(DateTime nowUtc)
        {
            lock (FileSync)
            {
                if (_lastPurgeDateUtc.Date == nowUtc.Date)
                    return;
                _lastPurgeDateUtc = nowUtc.Date;
                PurgeDirectory(Path.Combine(_rootPath, "review"), nowUtc.AddDays(-_textRetentionDays));
                PurgeDirectory(Path.Combine(_rootPath, "events"), nowUtc.AddDays(-_metricRetentionDays));
                PurgeDirectory(Path.Combine(_rootPath, "aggregates"), nowUtc.AddDays(-_aggregateRetentionDays));
                PurgeDirectory(Path.Combine(_rootPath, "quarantine"), nowUtc.AddDays(-_textRetentionDays));
            }
        }

        private static void PurgeDirectory(string directory, DateTime cutoffUtc)
        {
            if (!Directory.Exists(directory))
                return;
            foreach (var file in Directory.EnumerateFiles(directory, "help-*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoffUtc)
                        File.Delete(file);
                }
                catch
                {
                    // Retention remains best effort; operational monitoring reports repeated failures.
                }
            }
        }

        private string BuildMonthlyPseudonym(string userKey, DateTime nowUtc)
        {
            if (_hmacSecret == null || string.IsNullOrWhiteSpace(userKey))
                return null;
            using (var hmac = new HMACSHA256(_hmacSecret))
            {
                var input = nowUtc.ToString("yyyyMM", CultureInfo.InvariantCulture) + "|" + userKey.Trim().ToLowerInvariant();
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
                return BitConverter.ToString(hash, 0, 16).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private bool IsDeterministicSuccessSample(string interactionId)
        {
            if (_successSamplePercent <= 0)
                return false;
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(interactionId));
                return (BitConverter.ToUInt32(hash, 0) % 100) < _successSamplePercent;
            }
        }

        private bool TryReserveSuccessSample(DateTime nowUtc)
        {
            if (!_textCaptureEnabled || _successSampleMaxPerDay <= 0)
                return false;
            lock (FileSync)
            {
                if (_sampleCounterDateUtc.Date != nowUtc.Date)
                {
                    _sampleCounterDateUtc = nowUtc.Date;
                    _successSamplesToday = CountExistingSuccessSamples(nowUtc);
                }
                if (_successSamplesToday >= _successSampleMaxPerDay)
                    return false;
                _successSamplesToday++;
                return true;
            }
        }

        private void RememberPendingQuestion(string interactionId, string redactedQuestion, DateTime nowUtc)
        {
            foreach (var entry in _pendingQuestions.Where(item => item.Value.ExpiresAtUtc <= nowUtc).Take(200).ToList())
                _pendingQuestions.TryRemove(entry.Key, out _);
            if (_pendingQuestions.Count >= 5000)
            {
                foreach (var key in _pendingQuestions.Keys.Take(100).ToList())
                    _pendingQuestions.TryRemove(key, out _);
            }
            _pendingQuestions[interactionId] = new PendingQuestion
            {
                Question = HelpTextRedactor.Redact(redactedQuestion, 1200),
                ExpiresAtUtc = nowUtc.AddMinutes(_pendingQuestionMinutes)
            };
        }

        private int CountExistingSuccessSamples(DateTime nowUtc)
        {
            var file = Path.Combine(
                _rootPath,
                "review",
                "help-review-" + nowUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".ndjson");
            if (!File.Exists(file))
                return 0;
            try
            {
                return File.ReadLines(file)
                    .Count(line => line.IndexOf("\"sampleReason\":\"success-sample\"", StringComparison.Ordinal) >= 0);
            }
            catch
            {
                return _successSampleMaxPerDay;
            }
        }

        private void LogBestEffortFailure(Exception ex)
        {
            _logger.Log(
                "[HELP-ANALYTICS] Best-effort write failed type=" + ex.GetType().Name,
                AxaptaSessionManager.LogLevel.Warning);
        }

        private static string ResolveRootPath()
        {
            var configured = AppSettingsHelper.GetSetting(
                "HelpAssistant:AnalyticsPath",
                "INDCRM_HELP_ANALYTICS_PATH");
            var value = string.IsNullOrWhiteSpace(configured)
                ? @"C:\INDData\CRMHelpAnalytics"
                : configured.Trim();
            return Path.GetFullPath(value);
        }

        private sealed class PendingQuestion
        {
            public string Question { get; set; }

            public DateTime ExpiresAtUtc { get; set; }
        }
    }
}
