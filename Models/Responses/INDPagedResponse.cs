using System.Collections.Generic;

namespace IND_CRM_API.Models.Responses
{
    /// <summary>
    /// Standard envelope for list or paged endpoints.
    /// </summary>
    /// <typeparam name="T">Type of each item returned.</typeparam>
    public class INDPagedResponse<T>
    {
        /// <summary>True when the operation succeeds.</summary>
        public bool Success { get; set; }

        /// <summary>Short human readable message.</summary>
        public string Message { get; set; }

        /// <summary>Total items available for the query.</summary>
        public int Total { get; set; }

        /// <summary>Current page number (1-based).</summary>
        public int Page { get; set; }

        /// <summary>Size of the page requested.</summary>
        public int PageSize { get; set; }

        /// <summary>Items returned for the current page.</summary>
        public List<T> Items { get; set; }

        /// <summary>Optional business or technical error code.</summary>
        public string ErrorCode { get; set; }

        /// <summary>Validation errors when applicable.</summary>
        public List<INDValidationError> Errors { get; set; }

        /// <summary>Optional trace identifier for correlation.</summary>
        public string TraceId { get; set; }
    }
}
