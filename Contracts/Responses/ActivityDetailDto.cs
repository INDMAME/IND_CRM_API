using System.Collections.Generic;

namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// Datos detallados de una actividad CRM devueltos por Axapta.
    /// </summary>
    public class ActivityDetailDto
    {
        /// <summary>Identificador alfanumérico de la actividad.</summary>
        public string ActividadId { get; set; }

        /// <summary>Identificador interno (RecId) de la actividad.</summary>
        public string RecId { get; set; }

        /// <summary>Nombre o asunto de la actividad.</summary>
        public string Nombre { get; set; }

        /// <summary>Número de cuenta asociada.</summary>
        public string AccountNum { get; set; }

        /// <summary>Fecha de la actividad en formato yyyymmdd.</summary>
        public string TransDate { get; set; }

        /// <summary>País asociado.</summary>
        public string Country { get; set; }

        /// <summary>Tipo de actividad.</summary>
        public string ActividadType { get; set; }

        /// <summary>Tipo de visita.</summary>
        public string TipoVisita { get; set; }

        /// <summary>Metodo de contacto de la visita (INDContactMethod: 0 presencial, 1 llamada, 2 reunion online).</summary>
        public int? ContactMethod { get; set; }

        /// <summary>Descripción de la actividad.</summary>
        public string Description { get; set; }

        /// <summary>Comentarios adicionales.</summary>
        public string Comentarios { get; set; }

        /// <summary>Antecedentes de la visita.</summary>
        public string Antecedentes { get; set; }

        /// <summary>Conclusiones de la visita.</summary>
        public string Conclusiones { get; set; }

        /// <summary>Lista de asistentes vinculados.</summary>
        public List<ActivityAssistantDto> Asistentes { get; set; }

        /// <summary>Functional AX owner user for the activity.</summary>
        public string OwnerAxUserId { get; set; }

        /// <summary>Display name for the functional activity owner.</summary>
        public string OwnerName { get; set; }
    }

    /// <summary>
    /// Datos de un asistente vinculado a la actividad.
    /// </summary>
    public class ActivityAssistantDto
    {
        /// <summary>Identificador del asistente.</summary>
        public string AsistenteId { get; set; }

        /// <summary>Tipo de asistente (rol).</summary>
        public string AsistenteTipo { get; set; }

        /// <summary>Cargo del asistente.</summary>
        public string AsistenteCargo { get; set; }
    }
}
