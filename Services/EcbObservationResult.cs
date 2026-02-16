using System;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Resultado interno de una observacion obtenida desde ECB.
    /// </summary>
    public sealed class EcbObservationResult
    {
        /// <summary>
        /// Indica si se encontro observacion valida.
        /// </summary>
        public bool Found { get; set; }

        /// <summary>
        /// Tipo de cambio observado.
        /// </summary>
        public decimal Rate { get; set; }

        /// <summary>
        /// Fecha efectiva de la observacion.
        /// </summary>
        public DateTime ObservationDate { get; set; }

        /// <summary>
        /// Crea un resultado de no encontrado.
        /// </summary>
        public static EcbObservationResult NotFound()
        {
            return new EcbObservationResult
            {
                Found = false,
                Rate = 0m,
                ObservationDate = DateTime.MinValue
            };
        }
    }
}
