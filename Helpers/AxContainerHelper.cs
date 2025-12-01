using AxaptaCOMConnector;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace IND_CRM_API.Helpers
{
    /// <summary>
    /// Utilidades para convertir contenedores de Axapta a estructuras .NET.
    /// </summary>
    public static class AxContainerHelper
    {
        public static object[] ToArray(object obj)
        {
            if (obj == null)
                return Array.Empty<object>();

            if (obj is IAxaptaContainer con)
            {
                int len = 0;
                try
                {
                    len = con.Length();
                }
                catch (COMException cex)
                {
                    // If we can't get length, return an array with the error info
                    return new object[] { $"<COMException Length(): {cex.ErrorCode} - {cex.Message}>" };
                }
                catch (Exception ex)
                {
                    return new object[] { $"<Error Length(): {ex.Message}>" };
                }

                var list = new List<object>(len);

                for (int i = 1; i <= len; i++)
                {
                    try
                    {
                        object item = con.Peek(i);

                        if (item is IAxaptaContainer inner)
                            list.Add(ToArray(inner));
                        else
                            list.Add(item);
                    }
                    catch (COMException cex)
                    {
                        // Don't throw; capture error info in the array so caller can inspect
                        list.Add($"<COMException Peek({i}): {cex.ErrorCode} - {cex.Message}>");
                    }
                    catch (Exception ex)
                    {
                        list.Add($"<Error Peek({i}): {ex.Message}>");
                    }
                }

                return list.ToArray();
            }

            return new[] { obj }; // no es container
        }

        public static IAxaptaContainer AsContainer(object obj)
        {
            return obj as IAxaptaContainer;
        }
    }
}
