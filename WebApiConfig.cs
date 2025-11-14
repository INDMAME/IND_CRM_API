using System.Web.Http;

namespace IND_CRM_API
{
    /// <summary>
    /// Configuración general de rutas y formato de salida para los controladores WebAPI.
    /// Define las rutas por atributo y la convención base "api/{controller}/{action}/{id}".
    /// </summary>
    public static class WebApiConfig
    {
        /// <summary>
        /// Registra las rutas y formateadores globales de la API.
        /// </summary>
        /// <param name="config">Instancia de configuración de WebAPI.</param>
        public static void Register(HttpConfiguration config)
        {
            // Permite usar [Route()] en controladores
            config.MapHttpAttributeRoutes();

            // Define ruta por defecto
            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{action}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );

            // Formato JSON por defecto (indentado para lectura)
            config.Formatters.JsonFormatter.SerializerSettings.Formatting =
                Newtonsoft.Json.Formatting.Indented;

            // Permite mostrar detalles de error completos (solo para depuración)
            config.IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Always;

            // Serialización estándar (omite valores nulos)
            config.Formatters.JsonFormatter.SerializerSettings = new Newtonsoft.Json.JsonSerializerSettings
            {
                NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                Formatting = Newtonsoft.Json.Formatting.None
            };


        }
    }
}
