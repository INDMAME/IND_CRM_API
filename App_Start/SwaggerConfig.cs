using WebActivatorEx;

[assembly: PreApplicationStartMethod(typeof(IND_CRM_API.SwaggerConfig), "Register")]

namespace IND_CRM_API
{
    /// <summary>
    /// Legacy entry point kept for WebActivatorEx. Swagger is configured in Startup via INDSwaggerConfig.
    /// </summary>
    public class SwaggerConfig
    {
        /// <summary>
        /// No-op to avoid duplicate Swagger setup. Startup.cs calls INDSwaggerConfig.Configure instead.
        /// </summary>
        public static void Register()
        {
            // Swagger is configured in Startup.cs using INDSwaggerConfig.Configure(HttpConfiguration).
        }
    }
}
