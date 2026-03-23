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
using System.Linq;
using System.Text;
using System.Web.Cors;
using System.Web.Http;
using System.Threading.Tasks;

[assembly: OwinStartup(typeof(IND_CRM_API.Startup))]

namespace IND_CRM_API
{
    /// <summary>
    /// OWIN startup class.
    /// Configures the API pipeline:
    /// <list type="number">
    /// <item>Initializes WebAPI and Swagger.</item>
    /// <item>Enables optional CORS.</item>
    /// <item>Configures JWT authentication (Bearer Token).</item>
    /// <item>Adds basic request/response logging.</item>
    /// </list>
    /// </summary>
    public class Startup
    {
        /// <summary>
        /// Configures middlewares and services for the OWIN pipeline.
        /// Runs automatically when the self-host starts.
        /// </summary>
        /// <param name="app">OWIN app builder instance (<see cref="IAppBuilder"/>).</param>
        public void Configuration(IAppBuilder app)
        {
            // ===========================================
            //    WebAPI and CORS initialization
            // ===========================================
            var config = new HttpConfiguration();
            WebApiConfig.Register(config);
            DependencyConfig.Register(config);
            INDSwaggerConfig.Configure(config);

            // Optional CORS config (disabled by default)
            var corsEnabled = AppSettingsHelper.GetBoolSetting("CorsSettings:Enabled", false, "INDCRM_CORS_ENABLED");

            if (corsEnabled)
            {
                var originsSetting = AppSettingsHelper.GetSetting("CorsSettings:AllowedOrigins", "INDCRM_CORS_ALLOWED_ORIGINS") ?? string.Empty;
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
            //    JWT authentication
            // ===========================================
            var issuer = AppSettingsHelper.GetSetting("JwtSettings:Issuer", "INDCRM_JWT_ISSUER");
            var audience = AppSettingsHelper.GetSetting("JwtSettings:Audience", "INDCRM_JWT_AUDIENCE");
            var secret = AppSettingsHelper.GetSetting("JwtSettings:SecretKey", "JWT_SECRET_KEY");

            if (string.IsNullOrEmpty(secret))
                throw new Exception("Missing JwtSettings:SecretKey in App.config or environment variable JWT_SECRET_KEY");

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
            //     Basic request/response logging
            // ===========================================
            app.Use(async (context, next) =>
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {context.Request.Method} {context.Request.Uri}");

                // Redirect root to Swagger UI
                if (context.Request.Path.Value == "/" || context.Request.Path.Value == "")
                {
                    context.Response.Redirect("/swagger/ui/index");
                    return;
                }
                // Authorization header deliberately not logged

                await next.Invoke();

                Console.WriteLine($"-> {context.Response.StatusCode} {context.Response.ReasonPhrase}");
            });

            // ===========================================
            //    Register WebAPI in OWIN pipeline
            // ===========================================
            app.UseWebApi(config);

            // Force route initialization
            config.EnsureInitialized();
        }
    }
}
