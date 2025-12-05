using System;
using System.IO;
using System.Web.Http;
using Swashbuckle.Application;

namespace IND_CRM_API.App_Start 
{
    /// <summary>
    /// Provides a single entry point to configure Swagger on the given HttpConfiguration.
    /// </summary>
    public static class INDSwaggerConfig
    {
        /// <summary>
        /// Applies Swagger and Swagger UI configuration using the supplied HttpConfiguration.
        /// </summary>
        /// <param name="config">Http configuration used by the OWIN pipeline.</param>
        public static void Configure(HttpConfiguration config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            config
                .EnableSwagger(c =>
                {
                    c.SingleApiVersion("v1", "INDTestAPIs SelfHost (Axapta Integration)");
                    // Usa el nombre completo del tipo para evitar colisiones de schemaId.
                    c.UseFullTypeNameInSchemaIds();

                    c.ApiKey("Bearer")
                        .Description("JWT authentication. Format: Bearer {token}")
                        .Name("Authorization")
                        .In("header");

                    // Swagger usa los XML comments generados en la compilacion para enriquecer el OpenAPI.
                    var xmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "IND_CRM_API.xml");
                    if (File.Exists(xmlPath))
                        c.IncludeXmlComments(xmlPath);
                })
                .EnableSwaggerUi(c =>
                {
                    c.EnableApiKeySupport("Authorization", "header");
                });
        }
    }
}
