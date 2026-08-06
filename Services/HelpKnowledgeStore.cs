using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Helpers;
using IND_CRM_API.Services.Interfaces;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Loads a deterministic JSON bundle from local storage and keeps one validated snapshot in memory.
    /// </summary>
    public sealed class HelpKnowledgeStore : IHelpKnowledgeStore
    {
        private const long MaxBundleBytes = 16L * 1024L * 1024L;
        private static readonly Regex StableIdPattern = new Regex(
            "^[a-z0-9][a-z0-9._-]{0,79}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex RouteKeyPattern = new Regex(
            "^[A-Za-z0-9][A-Za-z0-9._:-]{0,99}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex Sha256Pattern = new Regex(
            "^[A-Fa-f0-9]{64}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly HashSet<string> AllowedRouteKeys = new HashSet<string>(
            new[] { "home", "visits.history", "expenses.sheets", "expenses.tickets" },
            StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> AllowedLocales = new HashSet<string>(
            new[] { "es-ES", "eu-ES", "en", "pt", "it", "zh-Hans" },
            StringComparer.OrdinalIgnoreCase);

        private readonly object _syncRoot = new object();
        private readonly IAxLogger _logger;
        private readonly string _bundlePath;
        private HelpKnowledgeSnapshot _snapshot;
        private DateTime _snapshotWriteTimeUtc;
        private long _snapshotLength;

        public HelpKnowledgeStore(IAxLogger logger)
        {
            _logger = logger ?? new FileAxLogger();
            IsEnabled = AppSettingsHelper.GetBoolSetting(
                "HelpAssistant:Enabled",
                false,
                "INDCRM_HELP_ENABLED");
            _bundlePath = ResolveBundlePath();
        }

        public bool IsEnabled { get; }

        public HelpKnowledgeSnapshot GetSnapshot()
        {
            if (!IsEnabled)
            {
                throw new HelpFeatureUnavailableException(
                    HelpErrorCodes.FeatureDisabled,
                    "El asistente de ayuda no esta habilitado.");
            }

            FileInfo file;
            try
            {
                file = new FileInfo(_bundlePath);
                if (!file.Exists)
                {
                    throw new HelpFeatureUnavailableException(
                        HelpErrorCodes.KnowledgeUnavailable,
                        "La documentacion de ayuda no esta disponible.");
                }

                if (file.Length <= 0 || file.Length > MaxBundleBytes)
                {
                    throw new HelpFeatureUnavailableException(
                        HelpErrorCodes.KnowledgeUnavailable,
                        "El bundle de ayuda no tiene un tamano valido.");
                }
            }
            catch (HelpFeatureUnavailableException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new HelpFeatureUnavailableException(
                    HelpErrorCodes.KnowledgeUnavailable,
                    "No se pudo acceder a la documentacion de ayuda.",
                    ex);
            }

            if (_snapshot != null &&
                file.LastWriteTimeUtc == _snapshotWriteTimeUtc &&
                file.Length == _snapshotLength)
            {
                return _snapshot;
            }

            lock (_syncRoot)
            {
                file.Refresh();
                if (_snapshot != null &&
                    file.LastWriteTimeUtc == _snapshotWriteTimeUtc &&
                    file.Length == _snapshotLength)
                {
                    return _snapshot;
                }

                try
                {
                    var loaded = LoadSnapshot(file);
                    _snapshot = loaded;
                    _snapshotWriteTimeUtc = file.LastWriteTimeUtc;
                    _snapshotLength = file.Length;
                    _logger.Log(
                        "[HELP-KNOWLEDGE] Bundle loaded version=" + loaded.Bundle.knowledgeVersion +
                        " topics=" + loaded.TopicsById.Count.ToString(CultureInfo.InvariantCulture) +
                        " hash=" + loaded.BundleHash.Substring(0, 12),
                        AxaptaSessionManager.LogLevel.Info);
                    return loaded;
                }
                catch (Exception ex)
                {
                    if (_snapshot != null)
                    {
                        _logger.Log(
                            "[HELP-KNOWLEDGE] Bundle reload rejected; previous snapshot retained type=" + ex.GetType().Name,
                            AxaptaSessionManager.LogLevel.Warning);
                        return _snapshot;
                    }

                    throw new HelpFeatureUnavailableException(
                        HelpErrorCodes.KnowledgeUnavailable,
                        "La documentacion de ayuda no se pudo validar.",
                        ex);
                }
            }
        }

        /// <summary>
        /// Loads the same validated snapshot used at runtime for deterministic retrieval evaluation.
        /// </summary>
        public static HelpKnowledgeSnapshot LoadForValidation(string bundlePath)
        {
            if (string.IsNullOrWhiteSpace(bundlePath))
                throw new ArgumentException("A bundle path is required.", nameof(bundlePath));
            var file = new FileInfo(Path.GetFullPath(bundlePath));
            if (!file.Exists)
                throw new FileNotFoundException("The help bundle was not found.", file.FullName);
            return LoadSnapshot(file);
        }

        private static HelpKnowledgeSnapshot LoadSnapshot(FileInfo file)
        {
            byte[] bytes;
            using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (stream.Length <= 0 || stream.Length > MaxBundleBytes)
                    throw new InvalidDataException("Invalid help bundle size.");

                bytes = new byte[(int)stream.Length];
                var offset = 0;
                while (offset < bytes.Length)
                {
                    var read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0)
                        throw new EndOfStreamException("Unexpected end of help bundle.");
                    offset += read;
                }
            }

            var json = new System.Text.UTF8Encoding(false, true).GetString(bytes);
            var bundle = JsonConvert.DeserializeObject<HelpKnowledgeBundle>(json);
            ValidateAndNormalize(bundle);

            var topics = bundle.topics.ToDictionary(item => item.id, StringComparer.OrdinalIgnoreCase);
            var modules = bundle.modules.ToDictionary(item => item.id, StringComparer.OrdinalIgnoreCase);

            return new HelpKnowledgeSnapshot
            {
                Bundle = bundle,
                TopicsById = topics,
                ModulesById = modules,
                BundleHash = ComputeSha256(bytes),
                LoadedAtUtc = DateTime.UtcNow
            };
        }

        private static void ValidateAndNormalize(HelpKnowledgeBundle bundle)
        {
            if (bundle == null ||
                (!string.Equals(bundle.schemaVersion, "1.0", StringComparison.Ordinal) &&
                 !string.Equals(bundle.schemaVersion, "1.1", StringComparison.Ordinal)))
                throw new InvalidDataException("Unsupported help bundle schema.");
            if (string.IsNullOrWhiteSpace(bundle.knowledgeVersion))
                throw new InvalidDataException("Missing knowledgeVersion.");
            bundle.knowledgeHash = RequireSha256(bundle.knowledgeHash, "knowledgeHash");
            if (bundle.source == null)
                throw new InvalidDataException("Missing source metadata.");
            bundle.source.path = RequireText(bundle.source.path, "source.path", 1000);
            bundle.source.sha256 = RequireSha256(bundle.source.sha256, "source.sha256");

            bundle.defaultLocale = NormalizeLocale(bundle.defaultLocale, "es-ES");
            if (!AllowedLocales.Contains(bundle.defaultLocale))
                throw new InvalidDataException("Unsupported defaultLocale.");
            if ((bundle.supportedResponseLocales ?? new List<string>())
                .Where(locale => !string.IsNullOrWhiteSpace(locale))
                .Any(locale => !AllowedLocales.Contains(locale.Trim())))
            {
                throw new InvalidDataException("Unsupported response locale.");
            }
            bundle.supportedResponseLocales = NormalizeLocales(bundle.supportedResponseLocales, bundle.defaultLocale);
            bundle.modules = bundle.modules ?? new List<HelpKnowledgeModule>();
            bundle.topics = bundle.topics ?? new List<HelpKnowledgeTopic>();
            bundle.assets = bundle.assets ?? new List<HelpKnowledgeAsset>();

            if (bundle.modules.Count == 0 || bundle.topics.Count == 0)
                throw new InvalidDataException("Help bundle must contain modules and topics.");

            EnsureUniqueIds(bundle.modules.Select(item => item?.id), "module");
            EnsureUniqueIds(bundle.topics.Select(item => item?.id), "topic");
            EnsureUniqueIds(bundle.assets.Select(item => item?.id), "asset");
            var moduleIds = new HashSet<string>(bundle.modules.Select(item => item.id), StringComparer.OrdinalIgnoreCase);
            var topicIds = new HashSet<string>(bundle.topics.Select(item => item.id), StringComparer.OrdinalIgnoreCase);
            var assetIds = new HashSet<string>(bundle.assets.Select(item => item.id), StringComparer.OrdinalIgnoreCase);

            foreach (var asset in bundle.assets)
            {
                asset.path = RequireText(asset.path, "asset.path", 1000);
                asset.mimeType = RequireText(asset.mimeType, "asset.mimeType", 100);
                asset.sha256 = RequireSha256(asset.sha256, "asset.sha256");
                asset.altText = NormalizeText(asset.altText, 1000);
                asset.sourcePart = NormalizeText(asset.sourcePart, 500);
            }

            foreach (var module in bundle.modules)
            {
                module.title = RequireText(module.title, "module.title", 200);
                module.description = NormalizeText(module.description, 1000);
                module.topicIds = NormalizeIdList(module.topicIds, topicIds, "module.topicIds");
                module.localizations = NormalizeModuleLocalizations(
                    module.localizations,
                    bundle.supportedResponseLocales);
            }

            foreach (var topic in bundle.topics)
            {
                if (!moduleIds.Contains(topic.moduleId ?? string.Empty))
                    throw new InvalidDataException("Topic references an unknown module.");
                if (!string.Equals(topic.status, "published", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Only published topics are allowed in the runtime bundle.");

                topic.title = RequireText(topic.title, "topic.title", 300);
                topic.summary = RequireText(topic.summary, "topic.summary", 2000);
                topic.aliases = NormalizeLocalizedLists(topic.aliases, bundle.supportedResponseLocales);
                topic.sampleQuestions = NormalizeLocalizedLists(topic.sampleQuestions, bundle.supportedResponseLocales);
                topic.keywords = NormalizeTextList(topic.keywords, 100, 100);
                topic.audiences = NormalizeTextList(topic.audiences, 50, 100);
                topic.prerequisiteTopicIds = NormalizeIdList(topic.prerequisiteTopicIds, topicIds, "prerequisiteTopicIds");
                topic.relatedTopicIds = NormalizeIdList(topic.relatedTopicIds, topicIds, "relatedTopicIds");
                topic.quickAnswers = topic.quickAnswers ?? new List<HelpKnowledgeQuickAnswer>();
                topic.chunks = topic.chunks ?? new List<HelpKnowledgeChunk>();
                if (topic.chunks.Count == 0)
                    throw new InvalidDataException("Published topics require at least one chunk.");
                topic.contentHash = RequireSha256(topic.contentHash, "topic.contentHash");

                if (!string.IsNullOrWhiteSpace(topic.routeKey) &&
                    (!RouteKeyPattern.IsMatch(topic.routeKey.Trim()) || !AllowedRouteKeys.Contains(topic.routeKey.Trim())))
                    throw new InvalidDataException("Invalid routeKey.");
                topic.routeKey = string.IsNullOrWhiteSpace(topic.routeKey) ? null : topic.routeKey.Trim();

                EnsureUniqueIds(topic.chunks.Select(item => item?.id), "chunk");
                EnsureUniqueIds(topic.quickAnswers.Select(item => item?.id), "quickAnswer");
                var chunkIds = new HashSet<string>(topic.chunks.Select(item => item.id), StringComparer.OrdinalIgnoreCase);
                foreach (var chunk in topic.chunks)
                {
                    chunk.heading = RequireText(chunk.heading, "chunk.heading", 300);
                    chunk.body = RequireText(chunk.body, "chunk.body", 32000);
                    chunk.imageRefs = NormalizeIdList(chunk.imageRefs, assetIds, "chunk.imageRefs");
                    if (chunk.estimatedTokens <= 0)
                        chunk.estimatedTokens = EstimateTokens(chunk.heading + "\n" + chunk.body);
                }

                foreach (var quickAnswer in topic.quickAnswers)
                {
                    quickAnswer.question = RequireText(quickAnswer.question, "quickAnswer.question", 1000);
                    quickAnswer.answer = RequireText(quickAnswer.answer, "quickAnswer.answer", 8000);
                    quickAnswer.sourceChunkIds = NormalizeIdList(
                        quickAnswer.sourceChunkIds,
                        chunkIds,
                        "quickAnswer.sourceChunkIds");
                    if (quickAnswer.sourceChunkIds.Count == 0)
                        throw new InvalidDataException("Quick answers require at least one source chunk.");
                }

                topic.localizations = NormalizeTopicLocalizations(
                    topic.localizations,
                    bundle.supportedResponseLocales,
                    topic.chunks,
                    topic.quickAnswers,
                    assetIds);
            }

            foreach (var module in bundle.modules)
            {
                if (module.topicIds.Any(id => !bundle.topics.Any(topic =>
                    string.Equals(topic.id, id, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(topic.moduleId, module.id, StringComparison.OrdinalIgnoreCase))))
                {
                    throw new InvalidDataException("Module topicIds are inconsistent.");
                }
            }
            var catalogTopicIds = new HashSet<string>(
                bundle.modules.SelectMany(module => module.topicIds),
                StringComparer.OrdinalIgnoreCase);
            if (!catalogTopicIds.SetEquals(topicIds))
                throw new InvalidDataException("Every published topic must appear in the catalog.");
        }

        private static void EnsureUniqueIds(IEnumerable<string> ids, string kind)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawId in ids ?? Enumerable.Empty<string>())
            {
                var id = rawId?.Trim();
                if (string.IsNullOrWhiteSpace(id) || !StableIdPattern.IsMatch(id) || !seen.Add(id))
                    throw new InvalidDataException("Invalid or duplicate " + kind + " id.");
            }
        }

        private static List<string> NormalizeIdList(IEnumerable<string> values, ISet<string> allowed, string field)
        {
            var result = (values ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (result.Any(value => !allowed.Contains(value)))
                throw new InvalidDataException(field + " references an unknown id.");
            return result;
        }

        private static Dictionary<string, List<string>> NormalizeLocalizedLists(
            Dictionary<string, List<string>> values,
            IList<string> supportedLocales)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var locale in supportedLocales)
            {
                List<string> localized;
                if (values != null && values.TryGetValue(locale, out localized))
                    result[locale] = NormalizeTextList(localized, 100, 1000);
                else
                    result[locale] = new List<string>();
            }
            return result;
        }

        // Validates optional module display localizations while preserving old bundles without them.
        private static Dictionary<string, HelpKnowledgeModuleLocalization> NormalizeModuleLocalizations(
            Dictionary<string, HelpKnowledgeModuleLocalization> values,
            IList<string> supportedLocales)
        {
            var result = NormalizeLocalizationDictionary(values, supportedLocales, "module.localizations");
            foreach (var entry in result)
            {
                entry.Value.title = RequireText(
                    entry.Value.title,
                    "module.localizations[" + entry.Key + "].title",
                    200);
                entry.Value.description = RequireText(
                    entry.Value.description,
                    "module.localizations[" + entry.Key + "].description",
                    1000);
            }
            return result;
        }

        // Validates localized topic content against the canonical chunk and quick-answer identities.
        private static Dictionary<string, HelpKnowledgeTopicLocalization> NormalizeTopicLocalizations(
            Dictionary<string, HelpKnowledgeTopicLocalization> values,
            IList<string> supportedLocales,
            IList<HelpKnowledgeChunk> canonicalChunks,
            IList<HelpKnowledgeQuickAnswer> canonicalQuickAnswers,
            ISet<string> assetIds)
        {
            var result = NormalizeLocalizationDictionary(values, supportedLocales, "topic.localizations");
            var canonicalChunkIds = new HashSet<string>(
                canonicalChunks.Select(item => item.id),
                StringComparer.OrdinalIgnoreCase);
            var canonicalQuickAnswerIds = new HashSet<string>(
                canonicalQuickAnswers.Select(item => item.id),
                StringComparer.OrdinalIgnoreCase);

            foreach (var entry in result)
            {
                var locale = entry.Key;
                var localization = entry.Value;
                localization.title = RequireText(
                    localization.title,
                    "topic.localizations[" + locale + "].title",
                    300);
                localization.summary = RequireText(
                    localization.summary,
                    "topic.localizations[" + locale + "].summary",
                    2000);
                localization.chunks = localization.chunks ?? new List<HelpKnowledgeChunk>();
                localization.quickAnswers = localization.quickAnswers ?? new List<HelpKnowledgeQuickAnswer>();

                EnsureUniqueIds(localization.chunks.Select(item => item?.id), "localized chunk");
                EnsureUniqueIds(localization.quickAnswers.Select(item => item?.id), "localized quickAnswer");
                var localizedChunkIds = new HashSet<string>(
                    localization.chunks.Select(item => item.id),
                    StringComparer.OrdinalIgnoreCase);
                var localizedQuickAnswerIds = new HashSet<string>(
                    localization.quickAnswers.Select(item => item.id),
                    StringComparer.OrdinalIgnoreCase);
                if (!localizedChunkIds.SetEquals(canonicalChunkIds))
                    throw new InvalidDataException("Localized chunk ids must match canonical chunk ids.");
                if (!localizedQuickAnswerIds.SetEquals(canonicalQuickAnswerIds))
                    throw new InvalidDataException("Localized quick-answer ids must match canonical quick-answer ids.");

                foreach (var chunk in localization.chunks)
                {
                    chunk.heading = RequireText(
                        chunk.heading,
                        "topic.localizations[" + locale + "].chunk.heading",
                        300);
                    chunk.body = RequireText(
                        chunk.body,
                        "topic.localizations[" + locale + "].chunk.body",
                        32000);
                    chunk.imageRefs = NormalizeIdList(
                        chunk.imageRefs,
                        assetIds,
                        "topic.localizations[" + locale + "].chunk.imageRefs");
                    if (chunk.estimatedTokens <= 0)
                        chunk.estimatedTokens = EstimateTokens(chunk.heading + "\n" + chunk.body);
                }

                foreach (var quickAnswer in localization.quickAnswers)
                {
                    quickAnswer.question = RequireText(
                        quickAnswer.question,
                        "topic.localizations[" + locale + "].quickAnswer.question",
                        1000);
                    quickAnswer.answer = RequireText(
                        quickAnswer.answer,
                        "topic.localizations[" + locale + "].quickAnswer.answer",
                        8000);
                    quickAnswer.sourceChunkIds = NormalizeIdList(
                        quickAnswer.sourceChunkIds,
                        canonicalChunkIds,
                        "topic.localizations[" + locale + "].quickAnswer.sourceChunkIds");
                    if (quickAnswer.sourceChunkIds.Count == 0)
                        throw new InvalidDataException("Localized quick answers require at least one source chunk.");
                }

                var chunksById = localization.chunks.ToDictionary(item => item.id, StringComparer.OrdinalIgnoreCase);
                var quickAnswersById = localization.quickAnswers.ToDictionary(item => item.id, StringComparer.OrdinalIgnoreCase);
                localization.chunks = canonicalChunks.Select(item => chunksById[item.id]).ToList();
                localization.quickAnswers = canonicalQuickAnswers.Select(item => quickAnswersById[item.id]).ToList();
            }

            return result;
        }

        // Keeps only explicitly supplied localizations and canonicalizes their supported locale keys.
        private static Dictionary<string, T> NormalizeLocalizationDictionary<T>(
            Dictionary<string, T> values,
            IList<string> supportedLocales,
            string field)
            where T : class
        {
            var result = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in values ?? new Dictionary<string, T>())
            {
                var locale = supportedLocales.FirstOrDefault(candidate =>
                    string.Equals(candidate, entry.Key?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(locale))
                    throw new InvalidDataException(field + " contains an unsupported locale.");
                if (entry.Value == null || result.ContainsKey(locale))
                    throw new InvalidDataException(field + " contains an invalid or duplicate locale.");
                result.Add(locale, entry.Value);
            }
            return result;
        }

        private static List<string> NormalizeLocales(IEnumerable<string> locales, string defaultLocale)
        {
            var result = (locales ?? Enumerable.Empty<string>())
                .Where(locale => !string.IsNullOrWhiteSpace(locale))
                .Select(locale => locale.Trim())
                .Where(AllowedLocales.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!result.Any(locale => string.Equals(locale, defaultLocale, StringComparison.OrdinalIgnoreCase)))
                result.Insert(0, defaultLocale);
            return result;
        }

        private static string NormalizeLocale(string locale, string fallback)
        {
            return string.IsNullOrWhiteSpace(locale) ? fallback : locale.Trim();
        }

        private static List<string> NormalizeTextList(IEnumerable<string> values, int maxItems, int maxLength)
        {
            return (values ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => NormalizeText(value, maxLength))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(maxItems)
                .ToList();
        }

        private static string RequireText(string value, string field, int maxLength)
        {
            var normalized = NormalizeText(value, maxLength);
            if (string.IsNullOrWhiteSpace(normalized))
                throw new InvalidDataException("Missing " + field + ".");
            return normalized;
        }

        // Normalizes one SHA-256 value and rejects incomplete integrity metadata.
        private static string RequireSha256(string value, string field)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (normalized == null || !Sha256Pattern.IsMatch(normalized))
                throw new InvalidDataException("Invalid " + field + ".");
            return normalized.ToLowerInvariant();
        }

        private static string NormalizeText(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            var normalized = value.Trim();
            return normalized.Length <= maxLength ? normalized : normalized.Substring(0, maxLength);
        }

        private static int EstimateTokens(string value)
        {
            return string.IsNullOrEmpty(value) ? 0 : Math.Max(1, (int)Math.Ceiling(value.Length / 4m));
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string ResolveBundlePath()
        {
            var configured = AppSettingsHelper.GetSetting(
                "HelpAssistant:KnowledgeBundlePath",
                "INDCRM_HELP_KNOWLEDGE_BUNDLE_PATH");
            var value = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine("Knowledge", "crm-help.bundle.json")
                : configured.Trim();
            return Path.GetFullPath(Path.IsPathRooted(value)
                ? value
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, value));
        }
    }

    /// <summary>
    /// Maps validated knowledge objects to stable public API contracts.
    /// </summary>
    public static class HelpKnowledgeProjection
    {
        public static HelpCatalogDto ToCatalog(HelpKnowledgeSnapshot snapshot, string requestedLocale)
        {
            var resolvedLocale = snapshot.ResolveLocale(requestedLocale);
            var locale = HasCompleteCatalogLocalization(snapshot, resolvedLocale)
                ? resolvedLocale
                : snapshot.Bundle.defaultLocale;
            var useLocalization = HasCompleteCatalogLocalization(snapshot, locale);
            var modules = snapshot.Bundle.modules
                .OrderBy(module => module.order)
                .ThenBy(module => module.id, StringComparer.OrdinalIgnoreCase)
                .Select(module => new HelpCatalogModuleDto
                {
                    Id = module.id,
                    Title = useLocalization ? module.localizations[locale].title : module.title,
                    Description = useLocalization ? module.localizations[locale].description : module.description,
                    Order = module.order,
                    Topics = module.topicIds
                        .Where(snapshot.TopicsById.ContainsKey)
                        .Select(id => snapshot.TopicsById[id])
                        .Select(topic => new HelpCatalogTopicDto
                        {
                            Id = topic.id,
                            ModuleId = topic.moduleId,
                            Title = useLocalization ? topic.localizations[locale].title : topic.title,
                            Summary = useLocalization ? topic.localizations[locale].summary : topic.summary,
                            RouteKey = topic.routeKey,
                            HasQuickAnswers = topic.quickAnswers.Count > 0
                        })
                        .ToList()
                })
                .ToList();

            return new HelpCatalogDto
            {
                KnowledgeVersion = snapshot.Bundle.knowledgeVersion,
                DefaultLocale = snapshot.Bundle.defaultLocale,
                ResponseLocale = locale,
                Modules = modules
            };
        }

        public static HelpTopicDto ToTopic(HelpKnowledgeSnapshot snapshot, HelpKnowledgeTopic topic, string requestedLocale)
        {
            var resolvedLocale = snapshot.ResolveLocale(requestedLocale);
            var locale = resolvedLocale;
            HelpKnowledgeTopicLocalization localization = null;
            if (topic.localizations != null)
            {
                topic.localizations.TryGetValue(resolvedLocale, out localization);
            }
            if (localization == null &&
                !string.Equals(resolvedLocale, snapshot.Bundle.defaultLocale, StringComparison.OrdinalIgnoreCase))
            {
                locale = snapshot.Bundle.defaultLocale;
                if (topic.localizations != null)
                    topic.localizations.TryGetValue(locale, out localization);
            }

            if (localization == null)
                locale = snapshot.Bundle.defaultLocale;
            var chunks = localization == null ? topic.chunks : localization.chunks;
            var quickAnswers = localization == null ? topic.quickAnswers : localization.quickAnswers;
            return new HelpTopicDto
            {
                Id = topic.id,
                ModuleId = topic.moduleId,
                Title = localization == null ? topic.title : localization.title,
                Summary = localization == null ? topic.summary : localization.summary,
                RouteKey = topic.routeKey,
                PrerequisiteTopicIds = new List<string>(topic.prerequisiteTopicIds),
                RelatedTopicIds = new List<string>(topic.relatedTopicIds),
                Chunks = chunks.Select(chunk => new HelpTopicChunkDto
                {
                    Id = chunk.id,
                    Heading = chunk.heading,
                    Body = chunk.body,
                    ImageRefs = new List<string>(chunk.imageRefs)
                }).ToList(),
                QuickAnswers = quickAnswers.Select(answer => new HelpQuickAnswerDto
                {
                    Id = answer.id,
                    Question = answer.question,
                    Answer = answer.answer,
                    SourceChunkIds = new List<string>(answer.sourceChunkIds)
                }).ToList(),
                KnowledgeVersion = snapshot.Bundle.knowledgeVersion,
                ResponseLocale = locale
            };
        }

        // Requires complete module and topic coverage before localizing a catalog response.
        private static bool HasCompleteCatalogLocalization(HelpKnowledgeSnapshot snapshot, string locale)
        {
            return snapshot.Bundle.modules.All(module =>
                       module.localizations != null && module.localizations.ContainsKey(locale)) &&
                   snapshot.Bundle.topics.All(topic =>
                       topic.localizations != null && topic.localizations.ContainsKey(locale));
        }
    }
}
