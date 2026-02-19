namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// Datos basicos del subordinado disponible para hojas de gastos.
    /// </summary>
    public class ExpenseSheetSubordinateDto
    {
        /// <summary>
        /// Identificador CRM del subordinado.
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// Nombre descriptivo del subordinado.
        /// </summary>
        public string Name { get; set; }
    }
}
