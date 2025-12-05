using System.Collections.Generic;

namespace IND_CRM_API.Models.Responses
{
    /// <summary>
    /// Standard envelope for command endpoints (create, update, delete).
    /// </summary>
    /// <typeparam name="T">Type of the payload returned in Data.</typeparam>
    public class INDApiResponse<T>
    {
        /// <summary>True when the operation succeeds.</summary>
        public bool Success { get; set; }

        /// <summary>Short human readable message.</summary>
        public string Message { get; set; }

        /// <summary>Optional business or technical error code.</summary>
        public string ErrorCode { get; set; }

        /// <summary>Payload returned by the operation.</summary>
        public T Data { get; set; }

        /// <summary>List of validation errors when applicable.</summary>
        public List<INDValidationError> Errors { get; set; }

        /// <summary>Optional trace identifier for correlation.</summary>
        public string TraceId { get; set; }
    }
}
