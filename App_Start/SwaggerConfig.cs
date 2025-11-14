using System.Web.Http;
using WebActivatorEx;
using IND_CRM_APIs;
using Swashbuckle.Application;

[assembly: PreApplicationStartMethod(typeof(SwaggerConfig), "Register")]

namespace IND_CRM_APIs
{
    /// <summary>
    /// Configura la inicialización y los parámetros de Swagger
    /// para exponer la documentación interactiva de la API REST.
    /// </summary>
    public class SwaggerConfig
    {        
        /// <summary>
        /// Registra Swagger y SwaggerUI dentro de la configuración global de WebAPI.
        /// </summary>
        public static void Register()
        {
            var thisAssembly = typeof(SwaggerConfig).Assembly;

            GlobalConfiguration.Configuration
                .EnableSwagger(c =>
                    {
                        // Define versión y metadatos del API
                        c.SingleApiVersion("v1", "IND_CRM_APIs");
                      
                    })
                .EnableSwaggerUi(c =>
                    {
                        // Configuración visual de la UI (vacía, usa por defecto) 
                    });
        }
    }
}
