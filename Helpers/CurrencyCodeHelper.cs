using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace IND_CRM_API.Helpers
{
    /// <summary>
    /// Normalizes common currency hints and symbols to ISO-4217 codes.
    /// </summary>
    internal static class CurrencyCodeHelper
    {
        private static readonly Regex IsoTokenRegex = new Regex(@"\b[A-Z]{3}\b", RegexOptions.Compiled);

        private static readonly Dictionary<string, string> TextMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["euro"] = "EUR",
            ["euros"] = "EUR",
            ["eur"] = "EUR",
            ["dollar"] = "USD",
            ["dollars"] = "USD",
            ["dolar"] = "USD",
            ["dolares"] = "USD",
            ["usd"] = "USD",
            ["us dollar"] = "USD",
            ["us dollars"] = "USD",
            ["pound"] = "GBP",
            ["pounds"] = "GBP",
            ["libra"] = "GBP",
            ["libras"] = "GBP",
            ["gbp"] = "GBP",
            ["yen"] = "JPY",
            ["jpy"] = "JPY",
            ["yuan"] = "CNY",
            ["renminbi"] = "CNY",
            ["cny"] = "CNY",
            ["rmb"] = "CNY",
            ["franco suizo"] = "CHF",
            ["francos suizos"] = "CHF",
            ["chf"] = "CHF",
            ["peso mexicano"] = "MXN",
            ["pesos mexicanos"] = "MXN",
            ["mxn"] = "MXN",
            ["peso chileno"] = "CLP",
            ["pesos chilenos"] = "CLP",
            ["clp"] = "CLP",
            ["peso colombiano"] = "COP",
            ["pesos colombianos"] = "COP",
            ["cop"] = "COP",
            ["peso argentino"] = "ARS",
            ["pesos argentinos"] = "ARS",
            ["ars"] = "ARS",
            ["nuevo sol"] = "PEN",
            ["sol"] = "PEN",
            ["soles"] = "PEN",
            ["pen"] = "PEN",
            ["real"] = "BRL",
            ["reales"] = "BRL",
            ["brl"] = "BRL",
            ["aud"] = "AUD",
            ["cad"] = "CAD"
        };

        private static readonly KeyValuePair<string, string>[] SymbolMappings =
        {
            new KeyValuePair<string, string>("MX$", "MXN"),
            new KeyValuePair<string, string>("Mex$", "MXN"),
            new KeyValuePair<string, string>("US$", "USD"),
            new KeyValuePair<string, string>("U$S", "USD"),
            new KeyValuePair<string, string>("A$", "AUD"),
            new KeyValuePair<string, string>("C$", "CAD"),
            new KeyValuePair<string, string>("CA$", "CAD"),
            new KeyValuePair<string, string>("R$", "BRL"),
            new KeyValuePair<string, string>("S/", "PEN"),
            new KeyValuePair<string, string>("\u20AC", "EUR"),
            new KeyValuePair<string, string>("\u00A3", "GBP"),
            new KeyValuePair<string, string>("\u00A5", "JPY"),
            new KeyValuePair<string, string>("\u5143", "CNY")
        };

        private static readonly HashSet<string> IsoCurrencyCodes = BuildIsoCurrencyCodes();

        /// <summary>
        /// Converts a raw currency hint to a valid ISO-4217 code when possible.
        /// </summary>
        public static string NormalizeToIso4217(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var trimmed = value.Trim();
            var upper = trimmed.ToUpperInvariant();
            if (upper.Length == 3 && IsoCurrencyCodes.Contains(upper))
                return upper;

            foreach (var mapping in SymbolMappings)
            {
                if (trimmed.IndexOf(mapping.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return mapping.Value;
            }

            foreach (Match match in IsoTokenRegex.Matches(upper))
            {
                var token = match.Value;
                if (IsoCurrencyCodes.Contains(token))
                    return token;
            }

            var folded = FoldToAsciiLetters(trimmed);
            foreach (var mapping in TextMappings.OrderByDescending(m => m.Key.Length))
            {
                if (ContainsWordHint(folded, mapping.Key))
                    return mapping.Value;
            }

            return string.Empty;
        }

        /// <summary>
        /// Extracts compact raw hints that can help an LLM map the receipt currency.
        /// </summary>
        public static List<string> ExtractHints(string value)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(value))
                return results;

            var trimmed = value.Trim();

            foreach (var mapping in SymbolMappings)
            {
                if (trimmed.IndexOf(mapping.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    AddDistinct(results, mapping.Key);
            }

            var upper = trimmed.ToUpperInvariant();
            foreach (Match match in IsoTokenRegex.Matches(upper))
            {
                if (IsoCurrencyCodes.Contains(match.Value))
                    AddDistinct(results, match.Value);
            }

            var folded = FoldToAsciiLetters(trimmed);
            foreach (var mapping in TextMappings.OrderByDescending(m => m.Key.Length))
            {
                if (ContainsWordHint(folded, mapping.Key))
                    AddDistinct(results, mapping.Key);
            }

            return results;
        }

        /// <summary>
        /// Returns the first valid ISO-4217 code found in a sequence of candidates.
        /// </summary>
        public static string ResolveToIso4217(params string[] candidates)
        {
            if (candidates == null)
                return string.Empty;

            foreach (var candidate in candidates)
            {
                var normalized = NormalizeToIso4217(candidate);
                if (!string.IsNullOrWhiteSpace(normalized))
                    return normalized;
            }

            return string.Empty;
        }

        /// <summary>
        /// Returns the best compact raw hint found in a sequence of candidates.
        /// </summary>
        public static string ResolveRawHint(params string[] candidates)
        {
            if (candidates == null)
                return string.Empty;

            foreach (var candidate in candidates)
            {
                var hint = GetBestRawHint(candidate);
                if (!string.IsNullOrWhiteSpace(hint))
                    return hint;
            }

            return string.Empty;
        }

        private static HashSet<string> BuildIsoCurrencyCodes()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
            {
                try
                {
                    var region = new RegionInfo(culture.LCID);
                    if (!string.IsNullOrWhiteSpace(region.ISOCurrencySymbol))
                        set.Add(region.ISOCurrencySymbol.ToUpperInvariant());
                }
                catch
                {
                    // Ignore invalid culture-region combinations.
                }
            }

            return set;
        }

        private static string GetBestRawHint(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var hints = ExtractHints(value);
            if (hints.Count > 0)
                return hints[0];

            var iso = NormalizeToIso4217(value);
            return string.IsNullOrWhiteSpace(iso) ? string.Empty : iso;
        }

        private static string FoldToAsciiLetters(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);
            foreach (var ch in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category == UnicodeCategory.NonSpacingMark)
                    continue;

                builder.Append(ch);
            }

            return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }

        private static bool ContainsWordHint(string foldedValue, string foldedHint)
        {
            if (string.IsNullOrWhiteSpace(foldedValue) || string.IsNullOrWhiteSpace(foldedHint))
                return false;

            var pattern = @"\b" + Regex.Escape(foldedHint).Replace("\\ ", "\\s+") + @"\b";
            return Regex.IsMatch(foldedValue, pattern, RegexOptions.IgnoreCase);
        }

        private static void AddDistinct(List<string> target, string value)
        {
            if (target == null || string.IsNullOrWhiteSpace(value))
                return;

            var trimmed = value.Trim();
            if (target.Any(existing => string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)))
                return;

            target.Add(trimmed);
        }
    }
}
