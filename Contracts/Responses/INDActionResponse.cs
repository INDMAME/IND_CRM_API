using System.Xml.Serialization;

namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// Standard response for commands like create, update, delete.
    /// </summary>
    public class INDActionResponse
    {
        /// <summary>True when the operation succeeds.</summary>
        public bool Success { get; set; }

        /// <summary>Human readable outcome message.</summary>
        public string Message { get; set; }

        /// <summary>Optional application error code.</summary>
        public string ErrorCode { get; set; }

        /// <summary>Optional payload returned by the operation.</summary>
        public object Data { get; set; }
    }

    /// <summary>
    /// Standard response for commands that return a typed payload.
    /// </summary>
    /// <typeparam name="T">Type of the payload.</typeparam>
    public class INDActionResponse<T>
    {
        /// <summary>True when the operation succeeds.</summary>
        public bool Success { get; set; }

        /// <summary>Human readable outcome message.</summary>
        public string Message { get; set; }

        /// <summary>Optional application error code.</summary>
        public string ErrorCode { get; set; }

        /// <summary>Typed payload returned by the operation.</summary>
        public T Data { get; set; }
    }
}
