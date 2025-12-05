namespace IND_CRM_API.Models.Responses
{
    /// <summary>
    /// Describes a validation problem for a specific field.
    /// </summary>
    public class INDValidationError
    {
        /// <summary>Field name that failed validation.</summary>
        public string Field { get; set; }

        /// <summary>Reason of the validation error.</summary>
        public string Message { get; set; }
    }
}
