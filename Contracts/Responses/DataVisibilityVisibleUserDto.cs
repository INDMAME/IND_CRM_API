namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// Usuario visible resuelto por la capa global de visibilidad por modulo.
    /// </summary>
    public class DataVisibilityVisibleUserDto
    {
        /// <summary>Alias de persona en INDPersonaTable.</summary>
        public string Alias { get; set; }

        /// <summary>Usuario AX asociado a la persona visible.</summary>
        public string AxUserId { get; set; }

        /// <summary>Usuario CRM legacy, cuando existe mapeo.</summary>
        public string CrmUserId { get; set; }

        /// <summary>Nombre descriptivo de la persona visible.</summary>
        public string Name { get; set; }

        /// <summary>Origen de la resolucion de visibilidad.</summary>
        public string Source { get; set; }

        /// <summary>Politica AX que define el alcance de update/delete sobre este propietario.</summary>
        public string MutationPolicy { get; set; }

        /// <summary>Valor entero del enum AX INDMutationPolicy. Resolver catalogo completo mediante /api/crm/enums si se necesita pintar opciones.</summary>
        public int? MutationPolicyInt { get; set; }

        /// <summary>Etiqueta funcional de la politica de modificacion.</summary>
        public string MutationPolicyLabel { get; set; }

        /// <summary>Indica si el usuario actual puede modificar registros de este propietario visible.</summary>
        public bool CanMutate { get; set; }
    }
}
