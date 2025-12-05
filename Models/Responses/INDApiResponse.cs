using System.Collections.Generic;

namespace IND_CRM_API.Models.Responses
{
    /// <summary>
    /// Respuesta estandar para operaciones de comando (crear, actualizar, borrar).
    /// </summary>
    /// <typeparam name="T">Tipo del dato devuelto en Data.</typeparam>
    public class IndApiResponse<T>
    {
        /// <summary>Indica si la operacion termino correctamente.</summary>
        public bool Success { get; set; }

        /// <summary>Mensaje corto y legible con el resultado.</summary>
        public string Message { get; set; }

        /// <summary>Codigo de error de negocio o tecnico cuando Success es false.</summary>
        public string ErrorCode { get; set; }

        /// <summary>Dato devuelto por la operacion.</summary>
        public T Data { get; set; }

        /// <summary>Listado de errores de validacion asociados al request.</summary>
        public List<IndValidationError> Errors { get; set; }

        /// <summary>Identificador opcional de traza o correlacion.</summary>
        public string TraceId { get; set; }
    }
}
