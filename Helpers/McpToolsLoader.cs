using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace IND_CRM_API.Helpers
{
    /// <summary>
    /// Loads MCP tools JSON from disk using a small parent directory search.
    /// </summary>
    public static class McpToolsLoader
    {
        private const string ToolsFileName = "MCP_TOOLS.json";
        private const string ToolsFolderName = ".codex";
        private const int MaxParentHops = 6;

        /// <summary>
        /// Tries to load MCP tools as a JToken.
        /// </summary>
        public static bool TryLoad(out JToken tools, out string error)
        {
            tools = null;
            error = null;

            var path = FindToolsPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "MCP_TOOLS.json not found.";
                return false;
            }

            try
            {
                var json = File.ReadAllText(path);
                tools = JToken.Parse(json);
                return true;
            }
            catch (Exception ex)
            {
                error = "Failed to parse MCP_TOOLS.json: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Finds MCP_TOOLS.json by walking up parent directories from the app base.
        /// </summary>
        public static string FindToolsPath()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrWhiteSpace(baseDir))
                    return null;

                var current = new DirectoryInfo(baseDir);
                for (var i = 0; i < MaxParentHops && current != null; i++)
                {
                    var codexPath = Path.Combine(current.FullName, ToolsFolderName, ToolsFileName);
                    if (File.Exists(codexPath))
                        return codexPath;

                    var rootPath = Path.Combine(current.FullName, ToolsFileName);
                    if (File.Exists(rootPath))
                        return rootPath;

                    current = current.Parent;
                }
            }
            catch
            {
                // Ignore path discovery errors.
            }

            return null;
        }
    }
}
