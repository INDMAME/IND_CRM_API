using System;
using System.Collections.Generic;

namespace IND_CRM_API.Helpers
{
    /// <summary>
    /// Paginacion simple en memoria para listas ya materializadas.
    /// </summary>
    public static class PagingHelper
    {
        /// <summary>
        /// Aplica page/pageSize a una coleccion indexada.
        /// </summary>
        public static List<T> Apply<T>(IReadOnlyList<T> items, int page, int pageSize)
        {
            var result = new List<T>();
            if (items == null || items.Count == 0)
                return result;

            if (page <= 0 || pageSize <= 0)
            {
                for (var i = 0; i < items.Count; i++)
                    result.Add(items[i]);
                return result;
            }

            var skipLong = ((long)page - 1L) * pageSize;
            if (skipLong < 0L)
                skipLong = 0L;

            if (skipLong >= items.Count)
                return result;

            var start = (int)skipLong;
            var take = Math.Min(pageSize, items.Count - start);
            for (var i = 0; i < take; i++)
                result.Add(items[start + i]);

            return result;
        }
    }
}
