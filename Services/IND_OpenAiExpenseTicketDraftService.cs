using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Services.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Extracts draft expense-sheet payload from ticket images using OpenAI Responses API.
    /// </summary>
    public sealed class IND_OpenAiExpenseTicketDraftService : IND_IExpenseTicketDraftService
    {
        private const string DefaultModel = "gpt-4o";
        private const int DefaultTimeoutSeconds = 180;
        private const int DefaultMaxImageBytes = 50 * 1024 * 1024;
        private const string ResponsesUrl = "https://api.openai.com/v1/responses";
        private const string ModelSettingKey = "OpenAI:ExpenseTicketModel";
        private const string TimeoutSettingKey = "OpenAI:ExpenseTicketTimeoutSeconds";
        private const string MaxImageBytesSettingKey = "OpenAI:ExpenseTicketMaxImageBytes";

        private static readonly HashSet<int> AllowedTypeValues = new HashSet<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 14 };
        private static readonly int TimeoutSeconds = ReadTimeoutFromConfig();
        private static readonly int MaxImageBytes = ReadMaxImageBytesFromConfig();
        private static readonly HttpClient _httpClient = CreateHttpClient();

        private readonly IAxLogger _logger;
        private readonly string _model;

        public IND_OpenAiExpenseTicketDraftService(IAxLogger logger)
        {
            _logger = logger ?? new FileAxLogger();
            _model = ReadModelFromConfig();
        }

        public async Task<ExpenseSheetDraftResponse> ExtractFromTicketImageAsync(
            byte[] imageBytes,
            string fileName,
            string contentType,
            CancellationToken cancellationToken)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                throw new ArgumentException("ticketImage no puede estar vacio.", nameof(imageBytes));

            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("fileName es obligatorio.", nameof(fileName));

            if (imageBytes.Length > MaxImageBytes)
                throw new ArgumentException($"La imagen supera el maximo permitido ({MaxImageBytes} bytes).", nameof(imageBytes));

            var openAiApiKey = GetOpenAiApiKey();
            if (string.IsNullOrWhiteSpace(openAiApiKey))
                throw new InvalidOperationException("OpenAI API key no esta configurada.");

            var imageBase64 = Convert.ToBase64String(imageBytes);
            var promptText = BuildPayloadPromptText();
            var payloadJson = BuildPayloadJson(imageBase64, GetNormalizedDataContentType(contentType), fileName, promptText, _model);

            using (var request = new HttpRequestMessage(HttpMethod.Post, ResponsesUrl))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", openAiApiKey);
                request.Headers.UserAgent.Clear();
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("IND_CRM_API", "1.0"));
                request.Headers.ExpectContinue = false;
                request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                HttpResponseMessage response = null;
                string responseBody = null;
                var sw = System.Diagnostics.Stopwatch.StartNew();

                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                    response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                        .ConfigureAwait(false);
                    responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        var summary = TryExtractOpenAiErrorSummary(responseBody);
                        var retryAfterSeconds = IND_OpenAiErrorHandling.GetRetryAfterSeconds(response);
                        _logger.Log(
                            $"[OPENAI] Expense draft failed status={(int)response.StatusCode} retryAfter={(retryAfterSeconds.HasValue ? retryAfterSeconds.Value.ToString(CultureInfo.InvariantCulture) : "na")} summary={summary}",
                            AxaptaSessionManager.LogLevel.Warning);

                        if (IND_OpenAiErrorHandling.IsRateLimit(response.StatusCode, responseBody))
                        {
                            throw new IND_OpenAiRateLimitException(
                                "OpenAI rate limit exceeded while extracting expense ticket draft.",
                                retryAfterSeconds,
                                summary);
                        }

                        throw new Exception("Error en servicio de extraccion de ticket.");
                    }

                    var extracted = TryParseExpenseDraft(responseBody);
                    if (extracted == null)
                    {
                        _logger.Log("[OPENAI] Respuesta sin json valido de draft de ticket.", AxaptaSessionManager.LogLevel.Warning);
                        throw new Exception("OpenAI no devolvio un JSON valido para el draft.");
                    }

                    _logger.Log($"[OPENAI] Draft extraido exitosamente ms={sw.ElapsedMilliseconds}", AxaptaSessionManager.LogLevel.Info);
                    return extracted;
                }
                catch (TaskCanceledException ex)
                {
                    _logger.Log("[OPENAI] Peticion cancelada: " + ex.Message, AxaptaSessionManager.LogLevel.Warning);
                    throw;
                }
                catch (Exception ex) when (!(ex is InvalidOperationException))
                {
                    _logger.Log("[OPENAI] Error extrayendo draft: " + ex.Message, AxaptaSessionManager.LogLevel.Warning);
                    throw;
                }
                finally
                {
                    response?.Dispose();
                }
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
            };
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        private static int ReadTimeoutFromConfig()
        {
            try
            {
                var value = ConfigurationManager.AppSettings[TimeoutSettingKey];
                if (int.TryParse(value, out var parsed) && parsed > 0)
                    return parsed;
            }
            catch
            {
                // Ignore and return default.
            }

            return DefaultTimeoutSeconds;
        }

        private static int ReadMaxImageBytesFromConfig()
        {
            try
            {
                var value = ConfigurationManager.AppSettings[MaxImageBytesSettingKey];
                if (int.TryParse(value, out var parsed) && parsed > 0)
                    return parsed;
            }
            catch
            {
                // Ignore and return default.
            }

            return DefaultMaxImageBytes;
        }

        private static string ReadModelFromConfig()
        {
            try
            {
                var value = ConfigurationManager.AppSettings[ModelSettingKey];
                return string.IsNullOrWhiteSpace(value) ? DefaultModel : value.Trim();
            }
            catch
            {
                return DefaultModel;
            }
        }

        private static string GetOpenAiApiKey()
        {
            try
            {
                var env = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                if (!string.IsNullOrWhiteSpace(env))
                    return env.Trim();

                var cfg = ConfigurationManager.AppSettings["OpenAI:ApiKey"];
                return string.IsNullOrWhiteSpace(cfg) ? null : cfg.Trim();
            }
            catch
            {
                return null;
            }
        }

        private static string GetNormalizedDataContentType(string contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
                return "image/jpeg";

            var normalized = contentType;
            if (normalized.IndexOf(';') >= 0)
                normalized = normalized.Split(';')[0];

            normalized = normalized.Trim().ToLowerInvariant();

            switch (normalized)
            {
                case "image/pjpeg":
                    return "image/jpeg";
                default:
                    return normalized;
            }
        }

        private static string BuildPayloadJson(string base64Image, string contentType, string fileName, string prompt, string model)
        {
            var format = new JObject
            {
                ["type"] = "json_schema",
                ["name"] = "expense_ticket_draft",
                ["schema"] = BuildResponseSchema(),
                ["strict"] = true
            };

            var payload = new JObject
            {
                ["model"] = model,
                ["input"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JArray
                        {
                            new JObject
                            {
                                ["type"] = "input_text",
                                ["text"] = prompt
                            },
                            new JObject
                            {
                                ["type"] = "input_image",
                                ["image_url"] = $"data:{contentType};base64,{base64Image}"
                            }
                        }
                    }
                },
                ["text"] = new JObject
                {
                    ["format"] = format
                },
                ["max_output_tokens"] = 1536
            };

            return JsonConvert.SerializeObject(payload);
        }

        private static string BuildPayloadPromptText()
        {
            return @"Eres un extractor para construir un borrador de hoja de gasto y lineas con este esquema.
- Responde SOLO JSON valido, sin markdown.
- Si un campo no se puede inferir con confianza, usa null y agrega advertencia en warnings.
- tipo de lineas:
  - 0: None
  - 1: Peaje
  - 2: Parking
  - 3: Km
  - 4: Desayuno
  - 5: Comida
  - 6: Cena
  - 7: Hotel
  - 8: Varios (solo si no coincide con ningun tipo anterior)
- 14: Taxi
- typeValue debe ser siempre un entero exacto de la lista anterior (0, 1, 2, 3, 4, 5, 6, 7, 8, 14).
- gastoType en cabecera debe usar el mismo enum fijo (0, 1, 2, 3, 4, 5, 6, 7, 8, 14).
- gastoType representa el tipo de gasto dominante del ticket.
- Si no hay evidencia clara para gastoType, usa 8.
- Si no hay evidencia clara de tipo, usa 8.
- qty debe ser la cantidad real de la linea (admite decimales) y nunca 0.
- price debe representar el precio unitario de la linea.
- lineTotal debe representar el total bruto de la linea (qty * price) cuando sea visible.
- Si detectas lineTotal y qty > 0, asegura coherencia: price = lineTotal / qty.
- Usa punto como separador decimal en todos los numeros del JSON (ej: 3.50, 12.00).
- No uses separadores de miles en los numeros del JSON.
- Si solo detectas un importe unico para la linea y qty=1, usa ese valor como price y lineTotal.
- transDate en formato DD.MM.YYYY o null si no se puede inferir.
- fileId debe ser null en todas las lineas (se asigna despues en backend).
- qty por defecto 1 salvo evidencia fuerte.
- internacional true solo si hay evidencia de gasto internacional.
- description corto y util para una linea de gasto.
- currencyCode en cabecera si se detecta; si no, deja null.
- metadata adicionales: confidence, warnings, rawCurrency y merchant.
- Deduce la moneda y el valor monetario de la imagen, sin soporte externo.
- Si un campo es imposible de inferir con calidad suficiente, usa null y deja una advertencia clara."
                .Trim();
        }

        private static string TryExtractOpenAiErrorSummary(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return string.Empty;

            try
            {
                var root = JObject.Parse(json);
                var err = root["error"] as JObject;
                if (err == null)
                    return string.Empty;

                var type = err["type"]?.ToString();
                var code = err["code"]?.ToString();
                var message = err["message"]?.ToString();
                return string.Join(" ", new[] { type, code, message }.Where(p => !string.IsNullOrWhiteSpace(p)));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static ExpenseSheetDraftResponse TryParseExpenseDraft(string responseBody)
        {
            var json = TryExtractOpenAiPayloadJson(responseBody);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var root = JObject.Parse(json);

            var request = new ExpenseSheetDraftResponse
            {
                mode = 0,
                userId = string.Empty,
                description = NormalizeText(root["description"]?.ToString(), "Ticket"),
                currencyCode = NormalizeText(root["currencyCode"]?.ToString(), null),
                gastoType = NormalizeTypeValue(root["gastoType"]),
                exchRate = TryParseDecimal(root["exchRate"]),
                projId = NormalizeText(root["projId"]?.ToString(), null),
                lines = new List<CreateExpenseSheetLineRequest>(),
                Confidence = NormalizeConfidence(root["confidence"]),
                Warnings = ExtractWarnings(root["warnings"]),
                RawCurrency = NormalizeText(root["rawCurrency"]?.ToString(), null),
                Merchant = NormalizeText(root["merchant"]?.ToString(), null)
            };

            var warnings = request.Warnings;
            if (string.IsNullOrWhiteSpace(request.currencyCode))
            {
                warnings = EnsureWarnings(warnings, "No se detecto currencyCode en el ticket. Revisar manualmente.");
            }

            var lines = root["lines"] as JArray;
            if (lines != null)
            {
                foreach (var line in lines)
                {
                    var mapped = TryMapLine(line as JObject, request);
                    if (mapped != null)
                        request.lines.Add(mapped);
                }
            }

            if (request.lines == null || request.lines.Count == 0)
            {
                request.lines.Add(MapFallbackLine(request));
                request.Warnings = EnsureWarnings(warnings, "No se detecto ninguna linea valida. Se genera una linea de respaldo para revision manual.");
                request.Confidence = request.Confidence.HasValue ? request.Confidence : 0m;
            }

            request.gastoType = ResolveDraftGastoType(request.gastoType, request.lines);

            if (request.Warnings == null || request.Warnings.Count == 0)
                request.Warnings = null;

            return request;
        }

        private static string TryExtractOpenAiPayloadJson(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return null;

            try
            {
                var root = JObject.Parse(responseBody);

                var direct = root["output_text"]?.ToString();
                if (!string.IsNullOrWhiteSpace(direct))
                    return TrimJsonBlock(direct);

                var output = root["output"] as JArray;
                if (output != null)
                {
                    foreach (var item in output)
                    {
                        var content = item["content"] as JArray;
                        if (content == null)
                            continue;

                        foreach (var part in content)
                        {
                            var type = part["type"]?.ToString();
                            if (!string.Equals(type, "output_text", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(type, "text", StringComparison.OrdinalIgnoreCase))
                                continue;

                            var text = part["text"]?.ToString();
                            var extracted = TrimJsonBlock(text);
                            if (!string.IsNullOrWhiteSpace(extracted))
                                return extracted;
                        }
                    }
                }

                return TrimJsonBlock(responseBody);
            }
            catch
            {
                return TrimJsonBlock(responseBody);
            }
        }

        private static string TrimJsonBlock(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var trimmed = text.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) && trimmed.EndsWith("```", StringComparison.Ordinal))
                trimmed = trimmed.Substring(3, trimmed.Length - 6).Trim();

            var start = trimmed.IndexOf('{');
            var end = trimmed.LastIndexOf('}');
            if (start < 0 || end <= start)
                return null;

            return trimmed.Substring(start, end - start + 1);
        }

        private static CreateExpenseSheetLineRequest TryMapLine(JObject lineToken, ExpenseSheetDraftResponse request)
        {
            if (lineToken == null)
                return null;

            var warnings = request.Warnings ?? new List<string>();

            var transDate = NormalizeDate(lineToken["transDate"]?.ToString());
            if (string.IsNullOrWhiteSpace(transDate))
                warnings = EnsureWarnings(warnings, "No se pudo inferir fecha de gasto. Se deja transDate null.");

            var qtyParsed = TryParseDecimal(lineToken["qty"]);
            var qty = qtyParsed.HasValue && qtyParsed.Value > 0m ? qtyParsed.Value : 1m;
            if (!qtyParsed.HasValue || qtyParsed.Value <= 0m)
                warnings = EnsureWarnings(warnings, "No se detecto qty valida. Se uso qty=1 por defecto.");

            var price = TryParseDecimal(lineToken["price"]);
            var lineTotal = TryParseDecimal(lineToken["lineTotal"]);

            if (!price.HasValue && lineTotal.HasValue && qty > 0m)
            {
                price = Math.Round(lineTotal.Value / qty, 4, MidpointRounding.AwayFromZero);
                warnings = EnsureWarnings(warnings, "price se calculo desde lineTotal/qty por falta de precio unitario explicito.");
            }

            if (price.HasValue && lineTotal.HasValue && qty > 0m)
            {
                var expectedTotal = price.Value * qty;
                if (Math.Abs(expectedTotal - lineTotal.Value) > 0.02m)
                {
                    var normalizedPrice = Math.Round(lineTotal.Value / qty, 4, MidpointRounding.AwayFromZero);
                    if (normalizedPrice > 0m)
                    {
                        price = normalizedPrice;
                        warnings = EnsureWarnings(warnings, "Se ajusto price para mantener coherencia con qty y lineTotal detectado.");
                    }
                }
            }

            if (!price.HasValue)
                warnings = EnsureWarnings(warnings, "No se detecto el price de la linea. Revisar manualmente.");

            var line = new CreateExpenseSheetLineRequest
            {
                transDate = transDate,
                typeValue = NormalizeTypeValue(lineToken["typeValue"]),
                description = NormalizeText(lineToken["description"]?.ToString(), "Ticket"),
                internacional = TryParseBool(lineToken["internacional"]),
                fileId = NormalizeText(lineToken["fileId"]?.ToString(), null),
                qty = qty,
                price = price,
                projId = NormalizeText(lineToken["projId"]?.ToString(), request?.projId)
            };

            request.Warnings = warnings;
            return line;
        }

        private static CreateExpenseSheetLineRequest MapFallbackLine(ExpenseSheetDraftResponse request)
        {
            return new CreateExpenseSheetLineRequest
            {
                transDate = null,
                typeValue = 8,
                description = NormalizeText(request?.description, "Ticket"),
                internacional = false,
                fileId = null,
                qty = 1m,
                price = null,
                projId = request?.projId
            };
        }

        private static List<string> ExtractWarnings(JToken warningsToken)
        {
            var warnings = new List<string>();

            if (warningsToken == null)
                return warnings;

            if (warningsToken.Type == JTokenType.Array)
            {
                foreach (var warning in (JArray)warningsToken)
                {
                    var text = warning?.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                        warnings.Add(text.Trim());
                }
            }
            else if (warningsToken.Type == JTokenType.String)
            {
                var text = warningsToken.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    warnings.Add(text.Trim());
            }

            return warnings;
        }

        private static List<string> EnsureWarnings(List<string> existing, string warning)
        {
            if (string.IsNullOrWhiteSpace(warning))
                return existing;

            if (existing == null)
                existing = new List<string>();

            existing.Add(warning.Trim());
            return existing;
        }

        private static decimal? NormalizeConfidence(JToken token)
        {
            var parsed = TryParseDecimal(token);
            if (!parsed.HasValue)
                return null;

            var value = parsed.Value;
            if (value < 0m)
                return 0m;
            if (value > 1m)
                return 1m;
            return value;
        }

        private static decimal? TryParseDecimal(JToken token)
        {
            if (token == null)
                return null;

            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                return token.Value<decimal>();

            if (token.Type == JTokenType.Boolean)
                return token.Value<bool>() ? 1m : 0m;

            return TryParseDecimalFromText(token.ToString());
        }

        private static decimal? TryParseDecimalFromText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var normalized = NormalizeNumericText(raw);
            if (string.IsNullOrWhiteSpace(normalized))
                return null;

            if (decimal.TryParse(
                normalized,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsed))
            {
                return parsed;
            }

            if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.GetCultureInfo("es-ES"), out parsed))
                return parsed;

            return null;
        }

        private static string NormalizeNumericText(string raw)
        {
            var trimmed = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return string.Empty;

            var filteredChars = trimmed
                .Where(ch => char.IsDigit(ch) || ch == '-' || ch == '+' || ch == '.' || ch == ',')
                .ToArray();
            var candidate = new string(filteredChars);
            if (string.IsNullOrWhiteSpace(candidate))
                return string.Empty;

            if (candidate.Length > 1)
            {
                var hasLeadingSign = candidate[0] == '-' || candidate[0] == '+';
                var sign = hasLeadingSign ? candidate[0].ToString() : string.Empty;
                var unsigned = hasLeadingSign ? candidate.Substring(1) : candidate;
                unsigned = unsigned.Replace("+", string.Empty).Replace("-", string.Empty);
                candidate = sign + unsigned;
            }

            var lastComma = candidate.LastIndexOf(',');
            var lastDot = candidate.LastIndexOf('.');

            if (lastComma >= 0 && lastDot >= 0)
            {
                if (lastComma > lastDot)
                {
                    candidate = candidate.Replace(".", string.Empty);
                    candidate = candidate.Replace(',', '.');
                }
                else
                {
                    candidate = candidate.Replace(",", string.Empty);
                }

                return candidate;
            }

            if (lastComma >= 0)
            {
                var commaCount = candidate.Count(ch => ch == ',');
                if (commaCount > 1)
                {
                    var decimalsLen = candidate.Length - candidate.LastIndexOf(',') - 1;
                    candidate = candidate.Replace(",", string.Empty);
                    if (decimalsLen > 0 && decimalsLen <= 2 && candidate.Length > decimalsLen)
                        candidate = candidate.Insert(candidate.Length - decimalsLen, ".");

                    return candidate;
                }

                var decimals = candidate.Length - lastComma - 1;
                if (decimals > 0 && decimals <= 2)
                    return candidate.Replace(',', '.');

                return candidate.Replace(",", string.Empty);
            }

            if (lastDot >= 0)
            {
                var dotCount = candidate.Count(ch => ch == '.');
                if (dotCount > 1)
                {
                    var decimalsLen = candidate.Length - candidate.LastIndexOf('.') - 1;
                    candidate = candidate.Replace(".", string.Empty);
                    if (decimalsLen > 0 && decimalsLen <= 2 && candidate.Length > decimalsLen)
                        candidate = candidate.Insert(candidate.Length - decimalsLen, ".");

                    return candidate;
                }

                var decimals = candidate.Length - lastDot - 1;
                if (decimals == 3 && lastDot > 1)
                    return candidate.Replace(".", string.Empty);
            }

            return candidate;
        }

        private static int? NormalizeTypeValue(JToken token)
        {
            if (token == null)
                return 8;

            int parsed;
            if (token.Type == JTokenType.Integer)
                parsed = token.Value<int>();
            else if (!int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                return 8;

            return AllowedTypeValues.Contains(parsed) ? parsed : 8;
        }

        private static int ResolveDraftGastoType(int? headerGastoType, List<CreateExpenseSheetLineRequest> lines)
        {
            if (headerGastoType.HasValue && AllowedTypeValues.Contains(headerGastoType.Value))
                return headerGastoType.Value;

            if (lines != null && lines.Count > 0)
            {
                var firstByType = new Dictionary<int, int>();
                for (int i = 0; i < lines.Count; i++)
                {
                    var typeValue = lines[i]?.typeValue;
                    if (!typeValue.HasValue || !AllowedTypeValues.Contains(typeValue.Value))
                        continue;

                    if (!firstByType.ContainsKey(typeValue.Value))
                        firstByType[typeValue.Value] = i;
                }

                var dominant = lines
                    .Where(l => l != null && l.typeValue.HasValue && AllowedTypeValues.Contains(l.typeValue.Value))
                    .GroupBy(l => l.typeValue.Value)
                    .Select(g => new
                    {
                        TypeValue = g.Key,
                        Count = g.Count(),
                        FirstIndex = firstByType.ContainsKey(g.Key) ? firstByType[g.Key] : int.MaxValue
                    })
                    .OrderByDescending(g => g.Count)
                    .ThenBy(g => g.FirstIndex)
                    .FirstOrDefault();

                if (dominant != null)
                    return dominant.TypeValue;
            }

            return 8;
        }

        private static bool TryParseBool(JToken token, bool defaultValue = false)
        {
            if (token == null)
                return defaultValue;

            if (token.Type == JTokenType.Boolean)
                return token.Value<bool>();

            if (token.Type == JTokenType.Integer)
                return token.Value<int>() != 0;

            var text = token.ToString();
            if (bool.TryParse(text, out var parsed))
                return parsed;

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                return intValue != 0;

            return defaultValue;
        }

        private static string NormalizeDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var trimmed = value.Trim();
            var acceptedFormats = new[] { "ddMMyyyy", "dd.MM.yyyy", "d.M.yyyy", "yyyyMMdd", "yyyy-MM-dd", "dd/MM/yyyy" };
            if (DateTime.TryParseExact(trimmed, acceptedFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return parsed.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

            if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var any))
                return any.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

            return null;
        }

        private static string NormalizeText(string value, string defaultValue)
        {
            var trimmed = (value ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? defaultValue : trimmed;
        }

        private static JObject BuildResponseSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["mode"] = new JObject
                    {
                        ["type"] = "integer",
                        ["enum"] = new JArray(0, 1, 2)
                    },
                    ["description"] = new JObject
                    {
                        ["type"] = "string"
                    },
                    ["currencyCode"] = new JObject
                    {
                        ["type"] = new JArray("string", "null")
                    },
                    ["gastoType"] = new JObject
                    {
                        ["type"] = new JArray("integer", "null"),
                        ["enum"] = new JArray(0, 1, 2, 3, 4, 5, 6, 7, 8, 14, null)
                    },
                    ["exchRate"] = new JObject
                    {
                        ["type"] = new JArray("number", "null")
                    },
                    ["projId"] = new JObject
                    {
                        ["type"] = new JArray("string", "null")
                    },
                    ["confidence"] = new JObject
                    {
                        ["type"] = new JArray("number", "null"),
                        ["minimum"] = 0,
                        ["maximum"] = 1
                    },
                    ["warnings"] = new JObject
                    {
                        ["type"] = new JArray("array", "null"),
                        ["items"] = new JObject
                        {
                            ["type"] = "string"
                        }
                    },
                    ["rawCurrency"] = new JObject
                    {
                        ["type"] = new JArray("string", "null")
                    },
                    ["merchant"] = new JObject
                    {
                        ["type"] = new JArray("string", "null")
                    },
                    ["lines"] = new JObject
                    {
                        ["type"] = "array",
                        ["minItems"] = 1,
                        ["items"] = BuildLineSchema()
                    }
                },
                ["required"] = new JArray(
                    "mode",
                    "description",
                    "currencyCode",
                    "gastoType",
                    "exchRate",
                    "projId",
                    "confidence",
                    "warnings",
                    "rawCurrency",
                    "merchant",
                    "lines")
            };
        }

        private static JObject BuildLineSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["transDate"] = new JObject
                    {
                        ["type"] = new JArray("string", "null")
                    },
                    ["typeValue"] = new JObject
                    {
                        ["type"] = "integer",
                        ["enum"] = new JArray(0, 1, 2, 3, 4, 5, 6, 7, 8, 14)
                    },
                    ["description"] = new JObject
                    {
                        ["type"] = "string"
                    },
                    ["internacional"] = new JObject
                    {
                        ["type"] = new JArray("boolean", "null")
                    },
                    ["fileId"] = new JObject
                    {
                        ["type"] = new JArray("string", "null")
                    },
                    ["qty"] = new JObject
                    {
                        ["type"] = "number"
                    },
                    ["price"] = new JObject
                    {
                        ["type"] = new JArray("number", "null")
                    },
                    ["lineTotal"] = new JObject
                    {
                        ["type"] = new JArray("number", "null")
                    },
                    ["projId"] = new JObject
                    {
                        ["type"] = new JArray("string", "null")
                    }
                },
                ["required"] = new JArray(
                    "transDate",
                    "typeValue",
                    "description",
                    "internacional",
                    "fileId",
                    "qty",
                    "price",
                    "lineTotal",
                    "projId")
            };
        }
    }
}
