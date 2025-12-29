using Microsoft.IdentityModel.Tokens;
using Microsoft.Owin;
using Microsoft.Owin.Cors;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Jwt;
using Owin;
using IND_CRM_API.App_Start;
using IND_CRM_API.Helpers;
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
    /// Clase de inicio de la aplicaci�n OWIN.
    /// Configura el pipeline principal de la API:
    /// <list type="number">
    /// <item>Inicializa WebAPI y Swagger.</item>
    /// <item>Habilita CORS global.</item>
    /// <item>Configura autenticaci�n JWT (Bearer Token).</item>
    /// <item>Agrega logging b�sico de solicitudes y compresi�n GZIP.</item>
    /// </list>
    /// </summary>
    public class Startup
    {
        /// <summary>
        /// Configura los middlewares y servicios del pipeline OWIN.
        /// Se ejecuta autom�ticamente al iniciar el servicio SelfHost.
        /// </summary>
        /// <param name="app">Instancia del constructor OWIN (<see cref="IAppBuilder"/>).</param>

        public void Configuration(IAppBuilder app)
        {
            // ===========================================
            //    Inicializaci�n de WebAPI y CORS
            // ===========================================
            var config = new HttpConfiguration();
            WebApiConfig.Register(config);
            DependencyConfig.Register(config);
            INDSwaggerConfig.Configure(config);

            // CORS opcional por configuracion (deshabilitado por defecto)
            var corsEnabled = false;
            var corsEnabledSetting = ConfigurationManager.AppSettings["CorsSettings:Enabled"];
            if (!string.IsNullOrWhiteSpace(corsEnabledSetting))
                bool.TryParse(corsEnabledSetting, out corsEnabled);

            if (corsEnabled)
            {
                var originsSetting = ConfigurationManager.AppSettings["CorsSettings:AllowedOrigins"] ?? string.Empty;
                var origins = originsSetting
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(o => o.Trim())
                    .Where(o => !string.IsNullOrWhiteSpace(o))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var corsPolicy = new CorsPolicy
                {
                    AllowAnyHeader = true,
                    AllowAnyMethod = true,
                    SupportsCredentials = false
                };

                if (origins.Any(o => o == "*"))
                {
                    corsPolicy.AllowAnyOrigin = true;
                }
                else
                {
                    foreach (var origin in origins)
                        corsPolicy.Origins.Add(origin);
                }

                if (corsPolicy.AllowAnyOrigin || corsPolicy.Origins.Count > 0)
                {
                    app.UseCors(new CorsOptions
                    {
                        PolicyProvider = new CorsPolicyProvider
                        {
                            PolicyResolver = _ => Task.FromResult(corsPolicy)
                        }
                    });
                }
            }

            // ===========================================
            //    Autenticaci�n JWT
            // ===========================================
            var issuer = ConfigurationManager.AppSettings["JwtSettings:Issuer"];
            var audience = ConfigurationManager.AppSettings["JwtSettings:Audience"];
            var secret = AppSettingsHelper.GetSetting("JwtSettings:SecretKey", "JWT_SECRET_KEY");

            if (string.IsNullOrEmpty(secret))
                throw new Exception("Falta JwtSettings:SecretKey en App.config o variable de entorno JWT_SECRET_KEY");

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
            //     Logging de solicitudes entrantes/salientes
            // ===========================================
            app.Use(async (context, next) =>
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {context.Request.Method} {context.Request.Uri}");

                // Redirigir ra�z a Swagger UI
                if (context.Request.Path.Value == "/" || context.Request.Path.Value == "")
                {
                    context.Response.Redirect("/swagger/ui/index");
                    return;
                }
                // Authorization header deliberately not logged

                await next.Invoke();

                Console.WriteLine($" ? {context.Response.StatusCode} {context.Response.ReasonPhrase}");
            });

            // =========================================================
            //     COMPRESI�N GZIP MANUAL
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


