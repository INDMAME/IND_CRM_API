using System.Collections.Generic;

namespace IND_CRM_API.Models.Responses
{
    /// <summary>
    /// Respuesta estandar para endpoints de listado o paginados.
    /// </summary>
    /// <typeparam name="T">Tipo de cada elemento devuelto.</typeparam>
    public class IndPagedResponse<T>
    {
        /// <summary>Indica si la operacion termino correctamente.</summary>
        public bool Success { get; set; }

        /// <summary>Mensaje corto y legible con el resultado.</summary>
        public string Message { get; set; }

        /// <summary>Total de elementos disponibles para la consulta.</summary>
        public int Total { get; set; }

        /// <summary>Numero de pagina actual (1-based).</summary>
        public int Page { get; set; }

        /// <summary>Tamano de pagina solicitado.</summary>
        public int PageSize { get; set; }

        /// <summary>Elementos devueltos en la pagina actual.</summary>
        public List<T> Items { get; set; }

        /// <summary>Identificador opcional de traza o correlacion.</summary>
        public string TraceId { get; set; }
    }
}
