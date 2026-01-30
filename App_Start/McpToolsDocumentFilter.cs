using System;
using System.Collections.Generic;
using IND_CRM_API.Helpers;
using Swashbuckle.Swagger;

namespace IND_CRM_API.App_Start
{
    /// <summary>
    /// Injects MCP tools data and a link into the Swagger document.
    /// </summary>
    public class McpToolsDocumentFilter : IDocumentFilter
    {
        public void Apply(SwaggerDocument swaggerDoc, SchemaRegistry schemaRegistry, System.Web.Http.Description.IApiExplorer apiExplorer)
        {
            if (swaggerDoc == null)
                return;

            EnsureInfoDescription(swaggerDoc);
            EnsureVendorExtensions(swaggerDoc);
        }

        private static void EnsureInfoDescription(SwaggerDocument swaggerDoc)
        {
            if (swaggerDoc.info == null)
                return;

            const string linkLine = "MCP tools: /api/mcp/tools";
            var description = swaggerDoc.info.description ?? string.Empty;
            if (description.IndexOf(linkLine, StringComparison.OrdinalIgnoreCase) < 0)
            {
                swaggerDoc.info.description = string.IsNullOrWhiteSpace(description)
                    ? linkLine
                    : description + Environment.NewLine + linkLine;
            }
        }

        private static void EnsureVendorExtensions(SwaggerDocument swaggerDoc)
        {
            if (swaggerDoc.vendorExtensions == null)
                swaggerDoc.vendorExtensions = new Dictionary<string, object>();

            object payload;
            if (McpToolsLoader.TryLoad(out var tools, out var error))
            {
                payload = new
                {
                    source = "file",
                    tools
                };
            }
            else
            {
                payload = new
                {
                    source = "file",
                    error = string.IsNullOrWhiteSpace(error) ? "MCP_TOOLS.json not found." : error,
                    toolsUrl = "/api/mcp/tools"
                };
            }

            swaggerDoc.vendorExtensions["x-mcp"] = payload;
        }
    }
}
