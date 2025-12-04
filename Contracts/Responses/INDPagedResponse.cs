using System.Collections.Generic;

namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// Standard response for paged list endpoints.
    /// </summary>
    /// <typeparam name="T">Type of each item in the list.</typeparam>
    public class INDPagedResponse<T>
    {
        /// <summary>True when the operation succeeds.</summary>
        public bool Success { get; set; }

        /// <summary>Human readable outcome message.</summary>
        public string Message { get; set; }

        /// <summary>Total items available for the query.</summary>
        public int Total { get; set; }

        /// <summary>Items returned in the current page.</summary>
        public List<T> Items { get; set; }
    }
}
