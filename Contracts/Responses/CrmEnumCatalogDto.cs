using System.Collections.Generic;

namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// Catalogo de valores disponibles para un enum AX en una company y aplicativo.
    /// </summary>
    public class CrmEnumCatalogDto
    {
        /// <summary>Company usada para resolver la configuracion per-company.</summary>
        public string Company { get; set; }

        /// <summary>Aplicativo consumidor del catalogo.</summary>
        public string AppCode { get; set; }

        /// <summary>Nombre tecnico del enum AX.</summary>
        public string AxEnumName { get; set; }

        /// <summary>Id tecnico del enum AX.</summary>
        public int? AxEnumId { get; set; }

        /// <summary>Indica si el enum solicitado existe en INDAxEnumsTable.</summary>
        public bool Found { get; set; }

        /// <summary>Opciones activas configuradas para este enum.</summary>
        public List<CrmEnumOptionDto> Options { get; set; } = new List<CrmEnumOptionDto>();
    }

    /// <summary>
    /// Opcion publica de un enum AX configurada para un aplicativo.
    /// </summary>
    public class CrmEnumOptionDto
    {
        /// <summary>Indice numerico AX que deben usar los endpoints de negocio.</summary>
        public int? Value { get; set; }

        /// <summary>Indice numerico del valor dentro del enum AX.</summary>
        public int? EnumIndex { get; set; }

        /// <summary>Etiqueta efectiva visible para el usuario final.</summary>
        public string Label { get; set; }

        /// <summary>Descripcion efectiva del valor.</summary>
        public string Description { get; set; }

        /// <summary>Indica si la opcion esta activa en el catalogo.</summary>
        public bool Active { get; set; }

        /// <summary>Orden configurado de presentacion. El valor 0 es valido.</summary>
        public int? SortOrder { get; set; }

        /// <summary>Referencia tecnica a INDAxEnumsTable.RecId.</summary>
        public long? AxEnumsTableRefRecId { get; set; }
    }
}
