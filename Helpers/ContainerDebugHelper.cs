using System;
using AxaptaCOMConnector;
using System.Web.Http;

namespace IND_CRM_API.Helpers
{
    public static class ContainerDebugHelper
    {
        /// <summary>
        /// Imprime cualquier AxaptaContainer y sub-containers en cascada.
        /// </summary>
        public static void DumpContainer(IAxaptaContainer con, string title = "Container")
        {
            if (con == null)
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] {title}: NULL");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"============================");
            System.Diagnostics.Debug.WriteLine($"[DEBUG] {title} (len={con.Length()})");
            System.Diagnostics.Debug.WriteLine($"============================");

            DumpContainerInternal(con, 0);
        }

        private static void DumpContainerInternal(IAxaptaContainer con, int indent)
        {
            string pad = new string(' ', indent * 3);

            for (int i = 1; i <= con.Length(); i++)
            {
                object value = con.Peek(i);

                if (value == null)
                {
                    System.Diagnostics.Debug.WriteLine($"{pad}[{i}] = NULL");
                    continue;
                }

                var type = value.GetType().FullName;

                // --- SUBCONTAINER ---
                if (value is IAxaptaContainer sub)
                {
                    System.Diagnostics.Debug.WriteLine($"{pad}[{i}] → SUBCONTAINER (len={sub.Length()}) type={type}");
                    DumpContainerInternal(sub, indent + 1);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"{pad}[{i}] = {value}   (type={type})");
                }
            }
        }
    }
}
