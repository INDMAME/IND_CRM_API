using AxaptaCOMConnector;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Helpers;
using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Centralizes AX enum catalog reads so controllers and internal flows share one mapper.
    /// </summary>
    public sealed class CrmEnumCatalogService : ICrmEnumCatalogService
    {
        private readonly IAxaptaSessionManager _sessionManager;
        private readonly IAxLogger _logger;

        /// <summary>
        /// Creates a catalog reader backed by the authenticated AX session manager.
        /// </summary>
        public CrmEnumCatalogService(IAxaptaSessionManager sessionManager, IAxLogger logger)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _logger = logger ?? new FileAxLogger();
        }

        /// <summary>
        /// Reads configured enum catalog groups by AX enum name.
        /// </summary>
        public IndPagedResponse<CrmEnumCatalogDto> GetByName(
            string username,
            string company,
            string appCode,
            string axEnumNames,
            string traceId)
        {
            return GetCatalog(username, company, appCode, axEnumNames, "getEnumValuesByName", traceId);
        }

        /// <summary>
        /// Reads configured enum catalog groups by AX enum id.
        /// </summary>
        public IndPagedResponse<CrmEnumCatalogDto> GetById(
            string username,
            string company,
            string appCode,
            string axEnumIds,
            string traceId)
        {
            return GetCatalog(username, company, appCode, axEnumIds, "getEnumValuesById", traceId);
        }

        /// <summary>
        /// Returns active numeric options for a single AX enum name.
        /// </summary>
        public IReadOnlyList<CrmEnumOptionDto> GetActiveOptionsByName(
            string username,
            string company,
            string appCode,
            string axEnumName,
            string traceId)
        {
            if (string.IsNullOrWhiteSpace(axEnumName))
                return new List<CrmEnumOptionDto>();

            var response = GetByName(username, company, appCode, axEnumName.Trim(), traceId);
            if (response?.Success != true || response.Items == null)
                return new List<CrmEnumOptionDto>();

            var group = response.Items.FirstOrDefault(item =>
                item != null &&
                string.Equals(item.AxEnumName, axEnumName.Trim(), StringComparison.OrdinalIgnoreCase));

            return group?.Options?
                       .Select(option => new
                       {
                           Option = option,
                           BusinessValue = ResolveBusinessEnumValue(option)
                       })
                       .Where(entry => entry.Option != null && entry.Option.Active && entry.BusinessValue.HasValue && entry.BusinessValue.Value >= 0)
                       .OrderBy(entry => entry.Option.SortOrder ?? entry.BusinessValue ?? int.MaxValue)
                       .Select(entry => entry.Option)
                       .ToList()
                   ?? new List<CrmEnumOptionDto>();
        }

        private static int? ResolveBusinessEnumValue(CrmEnumOptionDto option)
        {
            return option?.EnumIndex ?? option?.Value;
        }

        private IndPagedResponse<CrmEnumCatalogDto> GetCatalog(
            string username,
            string company,
            string appCode,
            string requestedEnums,
            string axMethodName,
            string traceId)
        {
            var ax = _sessionManager.GetAxInstanceForUser(username);
            var con = ax.CreateContainer();
            con.Append(company ?? string.Empty);
            con.Append(appCode ?? string.Empty);
            con.Append(requestedEnums ?? string.Empty);

            var resultObj = ax.CallStaticClassMethod("INDCRMUtilityService", axMethodName, con);
            var root = resultObj as IAxaptaContainer;
            if (root == null)
            {
                _logger.Log(
                    $"[ENUM-CATALOG] AX returned null container method={axMethodName} company={company} appCode={appCode} traceId={traceId}",
                    AxaptaSessionManager.LogLevel.Error);
                throw new InvalidOperationException("AX enum catalog method did not return a container.");
            }

            return MapEnumCatalog(root, company, appCode, traceId);
        }

        private static IndPagedResponse<CrmEnumCatalogDto> MapEnumCatalog(
            IAxaptaContainer root,
            string fallbackCompany,
            string fallbackAppCode,
            string traceId)
        {
            var headerWrap = AxContainerReadHelper.SafePeekContainer(root, 1);
            var header = AxContainerReadHelper.SafePeekContainer(headerWrap, 1) ?? headerWrap;
            var groups = AxContainerReadHelper.SafePeekContainer(root, 2);

            var success = ToBool(AxContainerReadHelper.SafeString(header, 1));
            var message = AxContainerReadHelper.SafeString(header, 2);
            var company = AxContainerReadHelper.SafeString(header, 3);
            var appCode = AxContainerReadHelper.SafeString(header, 4);
            var items = new List<CrmEnumCatalogDto>();

            if (string.IsNullOrWhiteSpace(company))
                company = fallbackCompany;
            if (string.IsNullOrWhiteSpace(appCode))
                appCode = fallbackAppCode;

            var groupCount = AxContainerReadHelper.SafeLength(groups);
            for (var i = 1; i <= groupCount; i++)
            {
                var group = AxContainerReadHelper.SafePeekContainer(groups, i);
                if (group == null)
                    continue;

                var options = AxContainerReadHelper.SafePeekContainer(group, 4);
                items.Add(new CrmEnumCatalogDto
                {
                    Company = company,
                    AppCode = appCode,
                    AxEnumName = AxContainerReadHelper.SafeString(group, 1),
                    AxEnumId = ToNullableInt(AxContainerReadHelper.SafeString(group, 2)),
                    Found = ToBool(AxContainerReadHelper.SafeString(group, 3)),
                    Options = MapOptions(options)
                });
            }

            return new IndPagedResponse<CrmEnumCatalogDto>
            {
                Success = success,
                Message = string.IsNullOrWhiteSpace(message) ? (success ? "OK" : "No se pudo resolver el catalogo de enums.") : message,
                Total = items.Count,
                Items = items,
                TraceId = traceId
            };
        }

        private static List<CrmEnumOptionDto> MapOptions(IAxaptaContainer options)
        {
            var result = new List<CrmEnumOptionDto>();
            var optionCount = AxContainerReadHelper.SafeLength(options);

            for (var i = 1; i <= optionCount; i++)
            {
                var row = AxContainerReadHelper.SafePeekContainer(options, i);
                if (row == null)
                    continue;

                result.Add(new CrmEnumOptionDto
                {
                    Value = ToNullableInt(AxContainerReadHelper.SafeString(row, 1)),
                    EnumIndex = ToNullableInt(AxContainerReadHelper.SafeString(row, 2)),
                    Label = AxContainerReadHelper.SafeString(row, 3),
                    Description = AxContainerReadHelper.SafeString(row, 4),
                    Active = ToBool(AxContainerReadHelper.SafeString(row, 5)),
                    SortOrder = ToNullableInt(AxContainerReadHelper.SafeString(row, 6)),
                    AxEnumsTableRefRecId = ToNullableLong(AxContainerReadHelper.SafeString(row, 7))
                });
            }

            return result;
        }

        private static int? ToNullableInt(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : (int?)null;
        }

        private static long? ToNullableLong(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : (long?)null;
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
    }
}
