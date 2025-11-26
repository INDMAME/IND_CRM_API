using AxaptaCOMConnector;
using System;
using System.Collections.Generic;

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
                int len = con.Length();
                var list = new List<object>(len);

                for (int i = 1; i <= len; i++)
                {
                    object item = con.Peek(i);

                    if (item is IAxaptaContainer inner)
                        list.Add(ToArray(inner));
                    else
                        list.Add(item);
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
