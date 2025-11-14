using AxaptaCOMConnector;
using System;
using System.Collections.Generic;

public static class AxContainerHelper
{
    /// <summary>
    /// Convierte un IAxaptaContainer en object[] (convirtiendo subcontainers también).
    /// </summary>
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
                {
                    list.Add(ToArray(inner));
                }
                else
                {
                    list.Add(item);
                }
            }

            return list.ToArray();
        }

        // No es container → devolver objeto envuelto en array
        return new[] { obj };
    }


    /// <summary>
    /// Intenta convertir un objeto a IAxaptaContainer.
    /// </summary>
    public static IAxaptaContainer AsContainer(object obj)
    {
        return obj as IAxaptaContainer;
    }
}
