using System.Web.Http;
using IND_CRM_API.App_Start;

namespace IND_CRM_API
{
    /// <summary>
    /// General route and output format configuration for WebAPI controllers.
    /// Defines attribute routes and the base convention "api/{controller}/{action}/{id}".
    /// </summary>
    public static class WebApiConfig
    {
        /// <summary>
        /// Registers global routes and formatters for the API.
        /// </summary>
        /// <param name="config">WebAPI configuration instance.</param>
        public static void Register(HttpConfiguration config)
        {
            // Enable attribute routing in controllers.
            config.MapHttpAttributeRoutes();

            // Define default route.
            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{action}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );

            // Global error filter (standard 500 response envelope).
            config.Filters.Add(new IndGlobalExceptionFilter());
            config.Filters.Add(new IndAuthorizationDiagnosticsFilter());
            config.Filters.Add(new IndRouteDiagnosticsActionFilter());
            config.Services.Add(typeof(System.Web.Http.ExceptionHandling.IExceptionLogger), new IndGlobalExceptionLogger());

            // Do not expose detailed errors in responses.
            config.IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Never;

            // Standard JSON serialization (include null fields for stable contracts).
            config.Formatters.JsonFormatter.SerializerSettings = new Newtonsoft.Json.JsonSerializerSettings
            {
                NullValueHandling = Newtonsoft.Json.NullValueHandling.Include,
                Formatting = Newtonsoft.Json.Formatting.None
            };
        }
    }
}
