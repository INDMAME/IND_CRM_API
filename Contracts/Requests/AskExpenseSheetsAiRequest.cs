using Newtonsoft.Json.Linq;
using IND_CRM_API.Contracts.Requests;

namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Request body for asking AI about expense sheet list data.
    /// </summary>
    public class AskExpenseSheetsAiRequest
    {
        /// <summary>
        /// User question to answer from the filtered records.
        /// </summary>
        public string question { get; set; }

        /// <summary>
        /// Optional answer style or formatting instructions.
        /// </summary>
        public string answerInstructions { get; set; }

        /// <summary>
        /// List filters captured from the expense sheet list screen.
        /// </summary>
        public GetExpenseSheetsListRequest listRequest { get; set; }

        /// <summary>
        /// Optional raw JSON payload captured from a previous expense sheet list response.
        /// Supports the full response envelope or a direct array of records.
        /// </summary>
        public JToken sourceJson { get; set; }
    }
}
