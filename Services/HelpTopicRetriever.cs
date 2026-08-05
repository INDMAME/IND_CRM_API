using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Performs deterministic weighted lexical retrieval with small typo tolerance.
    /// </summary>
    public sealed class HelpTopicRetriever : IHelpTopicRetriever
    {
        private const decimal MinimumAnswerScore = 0.34m;
        private const decimal AmbiguousRatio = 0.88m;
        private static readonly HashSet<string> StopWords = new HashSet<string>(
            new[]
            {
                "a", "about", "ai", "al", "and", "ayuda", "bat", "behar", "buruzko", "can", "como", "con",
                "da", "de", "del", "diferencia", "do", "donde", "duda", "dut", "e", "el", "en", "eta",
                "entre", "find", "for", "gaiari", "hay", "how", "i", "in", "la", "laguntza",
                "las", "le", "los", "me", "mi", "necesito", "nola", "o", "para", "por", "que", "the",
                "tengo", "to", "un", "una", "voglio", "where", "y"
            },
            StringComparer.OrdinalIgnoreCase);
        private static readonly IDictionary<string, string> CanonicalTokens =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "asignada", "asignado" }, { "assigned", "asignado" },
                { "confunde", "error" }, { "confundir", "error" }, { "incorrect", "error" },
                { "incorrectly", "error" }, { "incorrecta", "error" },
                { "data", "informacion" }, { "dato", "informacion" }, { "datos", "informacion" },
                { "date", "fecha" }, { "dates", "fecha" }, { "fechas", "fecha" },
                { "enviar", "enviar" }, { "envio", "enviar" }, { "mandar", "enviar" },
                { "image", "imagen" }, { "images", "imagen" }, { "imagenes", "imagen" },
                { "itxi", "logout" }, { "ixtea", "logout" }, { "cerrar", "logout" },
                { "lectura", "leer" }, { "read", "leer" }, { "reading", "leer" },
                { "manager", "responsable" }, { "undo", "deshacer" },
                { "rejected", "rechazo" }, { "rejection", "rechazo" },
                { "receipt", "ticket" }, { "receipts", "ticket" }
            };

        public HelpRetrievalResult Retrieve(HelpKnowledgeSnapshot snapshot, HelpRetrievalRequest request)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var locale = snapshot.ResolveLocale(request.ResponseLocale);
            HelpKnowledgeModule selectedModule = null;
            if (!string.IsNullOrWhiteSpace(request.SelectedModuleId) &&
                !snapshot.ModulesById.TryGetValue(request.SelectedModuleId.Trim(), out selectedModule))
            {
                return EmptyResult("notDocumented", "exact-module-missing");
            }

            if (!string.IsNullOrWhiteSpace(request.SelectedTopicId))
            {
                HelpKnowledgeTopic selected;
                if (!snapshot.TopicsById.TryGetValue(request.SelectedTopicId.Trim(), out selected))
                {
                    return EmptyResult("notDocumented", "exact-topic-missing");
                }
                if (selectedModule != null &&
                    !string.Equals(selected.moduleId, selectedModule.id, StringComparison.OrdinalIgnoreCase))
                {
                    return EmptyResult("notDocumented", "exact-topic-outside-module");
                }

                var exactTopic = new HelpRetrievedTopic { Topic = selected, Score = 1m };
                var exactResult = new HelpRetrievalResult
                {
                    Resolution = "answered",
                    Topics = new List<HelpRetrievedTopic> { exactTopic },
                    Candidates = new List<HelpTopicCandidateDto>(),
                    Ranking = new List<HelpRetrievedTopic> { exactTopic },
                    Confidence = 1m,
                    Mode = "exact-topic"
                };
                AttachQuickAnswer(
                    exactResult,
                    request.Question,
                    selected,
                    string.Equals(locale, snapshot.Bundle.defaultLocale, StringComparison.OrdinalIgnoreCase));
                return exactResult;
            }

            var normalizedQuestion = Normalize(request.Question);
            var queryTokens = Tokenize(normalizedQuestion);
            if (queryTokens.Count == 0)
                return EmptyResult("notDocumented", "empty-query");

            if (selectedModule == null)
            {
                var broadModuleResult = TryBuildBroadModuleResult(snapshot, queryTokens);
                if (broadModuleResult != null)
                    return broadModuleResult;
            }

            var scopedTopics = selectedModule == null
                ? snapshot.Bundle.topics
                : selectedModule.topicIds
                    .Where(snapshot.TopicsById.ContainsKey)
                    .Select(id => snapshot.TopicsById[id]);
            var scored = scopedTopics
                .Select(topic => new HelpRetrievedTopic
                {
                    Topic = topic,
                    Score = ScoreTopic(topic, locale, snapshot.Bundle.defaultLocale, normalizedQuestion, queryTokens)
                })
                .Where(item => item.Score > 0m)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Topic.id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var ranking = scored.Take(5).ToList();
            if (scored.Count == 0 || scored[0].Score < MinimumAnswerScore)
                return EmptyResult("notDocumented", "lexical-no-match", ranking);

            var top = scored[0];
            var hasCrossTopicConnector = ContainsCrossTopicConnector(normalizedQuestion);
            var strong = scored
                .Where(item => item.Score >= Math.Max(MinimumAnswerScore, top.Score * 0.62m))
                .Take(4)
                .ToList();

            if (selectedModule != null)
            {
                var scopedSelection = scored
                    .Where(item => item.Score >= Math.Max(MinimumAnswerScore, top.Score * 0.72m))
                    .Take(4)
                    .ToList();
                var scopedResult = new HelpRetrievalResult
                {
                    Resolution = "answered",
                    Topics = scopedSelection,
                    Candidates = new List<HelpTopicCandidateDto>(),
                    Ranking = ranking,
                    Confidence = top.Score,
                    Mode = scopedSelection.Count > 1 ? "module-multi-topic" : "module-topic"
                };
                if (scopedSelection.Count == 1)
                {
                    AttachQuickAnswer(
                        scopedResult,
                        request.Question,
                        top.Topic,
                        string.Equals(locale, snapshot.Bundle.defaultLocale, StringComparison.OrdinalIgnoreCase));
                }
                return scopedResult;
            }

            if (!hasCrossTopicConnector && top.Score < 0.90m && scored.Count > 1 &&
                scored[1].Score >= MinimumAnswerScore &&
                scored[1].Score >= top.Score * AmbiguousRatio)
            {
                return new HelpRetrievalResult
                {
                    Resolution = "needsSelection",
                    Topics = new List<HelpRetrievedTopic>(),
                    Candidates = ranking.Select(ToCandidate).ToList(),
                    Ranking = ranking,
                    Confidence = top.Score,
                    Mode = "lexical-ambiguous"
                };
            }

            var selectedTopics = hasCrossTopicConnector ? strong : new List<HelpRetrievedTopic> { top };
            var result = new HelpRetrievalResult
            {
                Resolution = "answered",
                Topics = selectedTopics,
                Candidates = new List<HelpTopicCandidateDto>(),
                Ranking = ranking,
                Confidence = top.Score,
                Mode = selectedTopics.Count > 1 ? "lexical-multi-topic" : "lexical-topic"
            };
            if (selectedTopics.Count == 1)
                AttachQuickAnswer(
                    result,
                    request.Question,
                    top.Topic,
                    string.Equals(locale, snapshot.Bundle.defaultLocale, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private static decimal ScoreTopic(
            HelpKnowledgeTopic topic,
            string locale,
            string fallbackLocale,
            string normalizedQuestion,
            IList<string> queryTokens)
        {
            var aliases = GetLocalized(topic.aliases, locale, fallbackLocale);
            var sampleQuestions = GetLocalized(topic.sampleQuestions, locale, fallbackLocale);
            var title = Normalize(topic.title);

            if (string.Equals(normalizedQuestion, title, StringComparison.Ordinal))
                return 1m;
            if (aliases.Any(value => string.Equals(normalizedQuestion, Normalize(value), StringComparison.Ordinal)))
                return 0.98m;
            if (sampleQuestions.Any(value => string.Equals(normalizedQuestion, Normalize(value), StringComparison.Ordinal)))
                return 0.96m;
            if (aliases.Any(value => ContainsLocalizedPhrase(normalizedQuestion, value)))
                return 0.94m;
            if (sampleQuestions.Any(value => ContainsLocalizedPhrase(normalizedQuestion, value)))
                return 0.92m;

            var score = 0m;
            score = Math.Max(score, WeightedCoverage(queryTokens, title, 0.82m));
            score = Math.Max(score, BestWeightedCoverage(queryTokens, aliases, 0.88m));
            score = Math.Max(score, BestWeightedCoverage(queryTokens, sampleQuestions, 0.86m));
            score = Math.Max(score, BestWeightedCoverage(queryTokens, topic.keywords, 0.78m));
            score = Math.Max(score, WeightedCoverage(queryTokens, Normalize(topic.summary), 0.58m));

            var chunkScore = BestWeightedCoverage(
                queryTokens,
                topic.chunks.Select(chunk => chunk.heading + " " + chunk.body),
                0.42m);
            score = Math.Max(score, chunkScore);
            if (queryTokens.Count >= 3)
            {
                var fullChunkIntent = BestWeightedCoverage(
                    queryTokens,
                    topic.chunks.Select(chunk => chunk.heading + " " + chunk.body),
                    0.72m);
                if (fullChunkIntent >= 0.70m)
                    score = Math.Max(score, 0.90m);
            }

            if (title.Length >= 4 && normalizedQuestion.Contains(title))
                score = Math.Max(score, 0.90m);
            return Math.Min(1m, score);
        }

        private static decimal WeightedCoverage(IList<string> queryTokens, string document, decimal weight)
        {
            var documentTokens = Tokenize(document);
            if (queryTokens.Count == 0 || documentTokens.Count == 0)
                return 0m;

            decimal total = 0m;
            foreach (var queryToken in queryTokens)
            {
                var best = documentTokens.Max(documentToken => TokenSimilarity(queryToken, documentToken));
                total += best;
            }

            var coverage = total / queryTokens.Count;
            var specificity = Math.Min(1m, queryTokens.Count / 3m);
            return coverage * weight * (0.72m + (0.28m * specificity));
        }

        private static decimal BestWeightedCoverage(
            IList<string> queryTokens,
            IEnumerable<string> documents,
            decimal weight)
        {
            return (documents ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => WeightedCoverage(queryTokens, Normalize(value), weight))
                .DefaultIfEmpty(0m)
                .Max();
        }

        private static decimal TokenSimilarity(string left, string right)
        {
            if (string.Equals(left, right, StringComparison.Ordinal))
                return 1m;
            if (left.Length >= 4 && right.Length >= 4 &&
                (left.StartsWith(right, StringComparison.Ordinal) || right.StartsWith(left, StringComparison.Ordinal)))
            {
                return 0.88m;
            }

            var maxLength = Math.Max(left.Length, right.Length);
            if (maxLength < 4 || Math.Abs(left.Length - right.Length) > 2)
                return 0m;
            var distance = LevenshteinDistance(left, right, 2);
            if (distance == 1)
                return 0.84m;
            if (distance == 2 && maxLength >= 7)
                return 0.68m;
            return 0m;
        }

        private static int LevenshteinDistance(string left, string right, int maximum)
        {
            var previous = Enumerable.Range(0, right.Length + 1).ToArray();
            var current = new int[right.Length + 1];
            for (var i = 1; i <= left.Length; i++)
            {
                current[0] = i;
                var rowMinimum = current[0];
                for (var j = 1; j <= right.Length; j++)
                {
                    var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                    current[j] = Math.Min(
                        Math.Min(current[j - 1] + 1, previous[j] + 1),
                        previous[j - 1] + cost);
                    rowMinimum = Math.Min(rowMinimum, current[j]);
                }

                if (rowMinimum > maximum)
                    return maximum + 1;
                var swap = previous;
                previous = current;
                current = swap;
            }
            return previous[right.Length];
        }

        private static void AttachQuickAnswer(
            HelpRetrievalResult result,
            string question,
            HelpKnowledgeTopic topic,
            bool allowCanonicalAnswer)
        {
            if (!allowCanonicalAnswer)
                return;
            var normalizedQuestion = Normalize(question);
            if (string.IsNullOrWhiteSpace(normalizedQuestion))
                return;

            var queryTokens = Tokenize(normalizedQuestion);
            var match = topic.quickAnswers
                .Select(answer => new
                {
                    Answer = answer,
                    Score = string.Equals(normalizedQuestion, Normalize(answer.question), StringComparison.Ordinal)
                        ? 1m
                        : WeightedCoverage(queryTokens, Normalize(answer.question), 1m)
                })
                .OrderByDescending(item => item.Score)
                .FirstOrDefault();
            if (match != null && match.Score >= 0.90m)
            {
                result.QuickAnswer = match.Answer;
                result.QuickAnswerTopic = topic;
                result.Mode = "canonical-quick-answer";
            }
        }

        private static HelpTopicCandidateDto ToCandidate(HelpRetrievedTopic item)
        {
            return new HelpTopicCandidateDto
            {
                TopicId = item.Topic.id,
                Title = item.Topic.title,
                Summary = item.Topic.summary,
                Score = Math.Round(item.Score, 3)
            };
        }

        // Routes intentionally broad one-topic queries to a useful module choice instead of guessing.
        private static HelpRetrievalResult TryBuildBroadModuleResult(
            HelpKnowledgeSnapshot snapshot,
            IList<string> queryTokens)
        {
            if (queryTokens.Count != 1)
                return null;

            string moduleId;
            switch (queryTokens[0])
            {
                case "gasto":
                case "gastos":
                    moduleId = "expenses";
                    break;
                case "ticket":
                case "tickets":
                    moduleId = "tickets";
                    break;
                case "visita":
                case "visitas":
                    moduleId = "visits";
                    break;
                default:
                    return null;
            }

            HelpKnowledgeModule module;
            if (!snapshot.ModulesById.TryGetValue(moduleId, out module))
                return null;
            var ranking = module.topicIds
                .Where(snapshot.TopicsById.ContainsKey)
                .Take(5)
                .Select((id, index) => new HelpRetrievedTopic
                {
                    Topic = snapshot.TopicsById[id],
                    Score = 0.75m - (index * 0.01m)
                })
                .ToList();
            var candidates = ranking.Select(ToCandidate).ToList();
            return new HelpRetrievalResult
            {
                Resolution = "needsSelection",
                Topics = new List<HelpRetrievedTopic>(),
                Candidates = candidates,
                Ranking = ranking,
                Confidence = candidates.Count == 0 ? 0m : candidates[0].Score,
                Mode = "lexical-broad-module"
            };
        }

        // Gives a strong signal to a localized multi-word alias embedded in a longer user question.
        private static bool ContainsLocalizedPhrase(string normalizedQuestion, string phrase)
        {
            var normalizedPhrase = Normalize(phrase);
            if (string.IsNullOrWhiteSpace(normalizedPhrase) || Tokenize(normalizedPhrase).Count < 2)
                return false;
            if (ContainsCjk(normalizedPhrase))
                return normalizedQuestion.Contains(normalizedPhrase);
            return (" " + normalizedQuestion + " ").Contains(" " + normalizedPhrase + " ");
        }

        private static HelpRetrievalResult EmptyResult(
            string resolution,
            string mode,
            IEnumerable<HelpRetrievedTopic> ranking = null)
        {
            return new HelpRetrievalResult
            {
                Resolution = resolution,
                Topics = new List<HelpRetrievedTopic>(),
                Candidates = new List<HelpTopicCandidateDto>(),
                Ranking = (ranking ?? Enumerable.Empty<HelpRetrievedTopic>()).Take(5).ToList(),
                Confidence = 0m,
                Mode = mode
            };
        }

        private static List<string> GetLocalized(
            IDictionary<string, List<string>> values,
            string locale,
            string fallbackLocale)
        {
            if (values == null)
                return new List<string>();
            List<string> localized;
            if (values.TryGetValue(locale, out localized) && localized != null && localized.Count > 0)
                return localized;
            if (values.TryGetValue(fallbackLocale, out localized) && localized != null)
                return localized;
            return new List<string>();
        }

        private static bool ContainsCrossTopicConnector(string value)
        {
            var padded = " " + value + " ";
            return padded.Contains(" y ") || padded.Contains(" and ") || padded.Contains(" eta ") ||
                   padded.Contains(" e ") || padded.Contains(" also ");
        }

        internal static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (var character in decomposed)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(character);
                if (category == UnicodeCategory.NonSpacingMark)
                    continue;
                builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
            }
            var normalized = string.Join(" ", builder.ToString()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            return normalized.Replace("打不开", "无法打开");
        }

        private static List<string> Tokenize(string value)
        {
            return Normalize(value)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(TokenizeSegment)
                .Where(token => (token.Length >= 2 || ContainsCjk(token)) && !StopWords.Contains(token))
                .Select(CanonicalizeToken)
                .Distinct(StringComparer.Ordinal)
                .Take(40)
                .ToList();
        }

        // Uses overlapping CJK bigrams because these languages do not delimit words with spaces.
        private static IEnumerable<string> TokenizeSegment(string segment)
        {
            if (!ContainsCjk(segment))
                return new[] { segment };
            if (segment.Length <= 1)
                return new[] { segment };
            return Enumerable.Range(0, segment.Length - 1)
                .Select(index => segment.Substring(index, 2));
        }

        private static string CanonicalizeToken(string token)
        {
            string canonical;
            return CanonicalTokens.TryGetValue(token, out canonical) ? canonical : token;
        }

        private static bool ContainsCjk(string value)
        {
            return !string.IsNullOrEmpty(value) && value.Any(character =>
                (character >= '\u3400' && character <= '\u4dbf') ||
                (character >= '\u4e00' && character <= '\u9fff'));
        }
    }
}
