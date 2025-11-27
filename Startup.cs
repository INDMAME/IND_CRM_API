using Microsoft.IdentityModel.Tokens;
using Microsoft.Owin;
using Microsoft.Owin.Cors;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Jwt;
using Owin;
using Swashbuckle.Application;
using IND_CRM_API.App_Start;
using System;
using System.Configuration;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Web.Cors;
using System.Web.Http;
using System.Threading.Tasks;

[assembly: OwinStartup(typeof(IND_CRM_API.Startup))]

namespace IND_CRM_API
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
            DependencyConfig.Register(config);

            // CORS restringido a orígenes permitidos
            var corsPolicy = new CorsPolicy
            {
                AllowAnyHeader = true,
                AllowAnyMethod = true,
                SupportsCredentials = true
            };
            corsPolicy.Origins.Add("http://localhost:7776");
            corsPolicy.Origins.Add("http://212.142.143.182:7776");

            app.UseCors(new CorsOptions
            {
                PolicyProvider = new CorsPolicyProvider
                {
                    PolicyResolver = _ => Task.FromResult(corsPolicy)
                }
            });

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
                    var xmlPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "IND_CRM_API.xml");
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
                // Authorization header deliberately not logged

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

            // Por seguridad, forzar inicializacion de rutas
            config.EnsureInitialized();

        }
    }
}


