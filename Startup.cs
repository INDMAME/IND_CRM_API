using Microsoft.IdentityModel.Tokens;
using Microsoft.Owin;
using Microsoft.Owin.Cors;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Jwt;
using Owin;
using Swashbuckle.Application;
using System;
using System.Configuration;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Web.Http;

[assembly: OwinStartup(typeof(IND_CRM_APIs.Startup))]

namespace IND_CRM_APIs
{
    /// <summary>
    /// Clase de inicio de la aplicación OWIN.
    /// Configura el pipeline principal de la API:
    /// <list type="number">
    /// <item>Inicializa WebAPI y Swagger.</item>
    /// <item>Habilita CORS global.</item>
    /// <item>Configura autenticación JWT (Bearer Token).</item>
    /// <item>Agrega logging básico de solicitudes y compresión GZIP.</item>
    /// </list>
    /// </summary>
    public class Startup
    {
        /// <summary>
        /// Configura los middlewares y servicios del pipeline OWIN.
        /// Se ejecuta automáticamente al iniciar el servicio SelfHost.
        /// </summary>
        /// <param name="app">Instancia del constructor OWIN (<see cref="IAppBuilder"/>).</param>

        public void Configuration(IAppBuilder app)
        {
            // ===========================================
            //    Inicialización de WebAPI y CORS
            // ===========================================
            var config = new HttpConfiguration();
            WebApiConfig.Register(config);
            app.UseCors(CorsOptions.AllowAll);

            // ===========================================
            //    Autenticación JWT
            // ===========================================
            var issuer = ConfigurationManager.AppSettings["JwtSettings:Issuer"];
            var audience = ConfigurationManager.AppSettings["JwtSettings:Audience"];
            var secret = ConfigurationManager.AppSettings["JwtSettings:SecretKey"];

            if (string.IsNullOrEmpty(secret))
                throw new Exception("Falta JwtSettings:SecretKey en App.config");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

            app.UseJwtBearerAuthentication(new JwtBearerAuthenticationOptions
            {
                AuthenticationMode = AuthenticationMode.Active,
                TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    IssuerSigningKey = key,
                    ClockSkew = TimeSpan.FromMinutes(3)
                }
            });

            // ===========================================
            //     Documentación Swagger / SwaggerUI
            // ===========================================
            config
                .EnableSwagger(c =>
                {
                    c.SingleApiVersion("v1", "INDTestAPIs SelfHost (Axapta Integration)");

                    // Define esquema JWT para autenticación en Swagger
                    c.ApiKey("Bearer")
                     .Description("Autenticación JWT. Formato requerido: Bearer {token}")
                     .Name("Authorization")
                     .In("header");

                    // Incluir comentarios XML para documentar los endpoints
                    var xmlPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "IND_CRM_APIs.xml");
                    if (System.IO.File.Exists(xmlPath))
                        c.IncludeXmlComments(xmlPath);
                })

                .EnableSwaggerUi(c =>
                {
                    c.EnableApiKeySupport("Authorization", "header");

                });

            // ===========================================
            //     Logging de solicitudes entrantes/salientes
            // ===========================================
            app.Use(async (context, next) =>
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {context.Request.Method} {context.Request.Uri}");

                // Redirigir raíz a Swagger UI
                if (context.Request.Path.Value == "/" || context.Request.Path.Value == "")
                {
                    context.Response.Redirect("/swagger/ui/index");
                    return;
                }

                if (context.Request.Headers.ContainsKey("Authorization"))
                    Console.WriteLine($" → Auth Header: {context.Request.Headers["Authorization"]}");

                await next.Invoke();

                Console.WriteLine($" ← {context.Response.StatusCode} {context.Response.ReasonPhrase}");
            });

            // =========================================================
            //     COMPRESIÓN GZIP MANUAL
            // =========================================================
            app.Use(async (context, next) =>
            {
                await next.Invoke();

                if (context.Response.StatusCode == 200 &&
                    context.Response.Body != null &&
                    context.Response.Headers.ContainsKey("Content-Length"))
                {
                    string acceptEncoding = context.Request.Headers.Get("Accept-Encoding");

                    if (!string.IsNullOrEmpty(acceptEncoding) &&
                        acceptEncoding.IndexOf("gzip", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var originalBody = context.Response.Body;
                        var compressedBody = new MemoryStream();

                        context.Response.Headers.Remove("Content-Length");
                        context.Response.Headers["Content-Encoding"] = "gzip";

                        using (var gzip = new GZipStream(compressedBody, CompressionMode.Compress, true))
                        {
                            await originalBody.CopyToAsync(gzip);
                        }

                        compressedBody.Seek(0, SeekOrigin.Begin);
                        context.Response.Body = compressedBody;
                    }
                }
            });

            // ===========================================
            //    Registrar WebAPI en el pipeline OWIN
            // ===========================================
            app.UseWebApi(config);
        }
    }
}
