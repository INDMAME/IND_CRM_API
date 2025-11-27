using System;
using System.Configuration;
using Topshelf;
using Microsoft.Owin.Hosting;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace IND_CRM_API
{
    /// <summary>
    /// Punto de entrada principal del servicio Windows SelfHost.
    /// Utiliza Topshelf para registrar y ejecutar el servicio OWIN
    /// que hospeda la API de integración con Axapta 3.0.
    /// </summary>
    internal class Program
    {
        /// <summary>
        /// Método principal de ejecución del servicio.
        /// Configura el servicio usando Topshelf y lo lanza en modo LocalSystem.
        /// </summary>
        static void Main(string[] args)
        {
            HostFactory.Run(x =>
            {
                // Registrar clase principal de servicio (WebApiService)
                x.Service<WebApiService>(s =>
                {
                    s.ConstructUsing(_ => new WebApiService());
                    s.WhenStarted(w => w.Start());
                    s.WhenStopped(w => w.Stop());
                });

                // Configuración general del servicio Windows
                x.RunAsLocalSystem();
                x.SetServiceName("IND_CRM_API");
                x.SetDisplayName("IND Test APIs (Axapta SelfHost)");
                x.SetDescription("Servicio OWIN SelfHost para Axapta 3.0");
            });
        }
    
        /// <summary>
        /// Servicio de hospedaje OWIN.
        /// Controla el ciclo de vida del servidor web y su ejecución dentro del proceso Windows.
        /// </summary>
        public class WebApiService
        {
            private IDisposable _webApp;

            /// <summary>
            /// Inicia el servicio OWIN en la URL configurada.
            /// Si no hay configuración en App.config, usa el puerto por defecto (7776).
            /// </summary>
            public void Start()
            {
                // Si está configurada la URL en App.config, úsala. Si no, usa el puerto por defecto.
                string baseUrl = ConfigurationManager.AppSettings["BaseUrl"] ?? "http://+:7776/";

                try
                {
                    // Intenta asegurar reserva de URL y regla de firewall para el puerto si es necesario.
                    TryEnsureUrlAcl(baseUrl);
                    TryEnsureFirewallRuleForBaseUrl(baseUrl);

                    _webApp = WebApp.Start<Startup>(baseUrl);
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] API iniciada correctamente en {baseUrl}");
                    Console.WriteLine("Presiona Ctrl+C para detener manualmente.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al iniciar la API en {baseUrl}: {ex}");
                    throw;
                }
            }

            /// <summary>
            /// Detiene el servicio OWIN y libera los recursos.
            /// </summary>
            public void Stop()
            {
                // 1. Detener el servidor Web (ya no entra tráfico)
                _webApp?.Dispose();

                // 2. NUEVO: Matar las sesiones de Axapta y liberar memoria COM
                // Accedemos a través de tu Singleton estático
                if (IND_CRM_API.Services.AxSession.Manager != null)
                {
                    IND_CRM_API.Services.AxSession.Manager.Dispose();
                }

                Console.WriteLine("API y Recursos COM liberados correctamente.");
            }

            private void TryEnsureUrlAcl(string baseUrl)
            {
                try
                {
                    // netsh requires exact prefix like http://+:7776/
                    var prefix = NormalizePrefix(baseUrl);
                    if (string.IsNullOrEmpty(prefix)) return;

                    // Check existing urlacl
                    var check = RunProcess("netsh", "http show urlacl");
                    if (check != null && check.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Console.WriteLine($"URL ACL already present for {prefix}");
                        return;
                    }

                    // Try to add urlacl for LocalSystem
                    var args = $"http add urlacl url={prefix} user=\"NT AUTHORITY\\SYSTEM\"";
                    var result = RunProcess("netsh", args);
                    Console.WriteLine($"netsh urlacl add result: {result}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"EnsureUrlAcl failed: {ex.Message}");
                }
            }

            private void TryEnsureFirewallRuleForBaseUrl(string baseUrl)
            {
                try
                {
                    int port = ExtractPort(baseUrl);
                    if (port <= 0) return;

                    // Check if a firewall rule likely exists (simple check)
                    var check = RunProcess("netsh", "advfirewall firewall show rule name=all");
                    if (check != null && check.IndexOf(port.ToString(), StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Console.WriteLine($"Firewall rule for port {port} may already exist.");
                        return;
                    }

                    var args = $"advfirewall firewall add rule name=\"IND_CRM_API {port}\" dir=in action=allow protocol=TCP localport={port}";
                    var result = RunProcess("netsh", args);
                    Console.WriteLine($"netsh firewall add result: {result}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"EnsureFirewallRule failed: {ex.Message}");
                }
            }

            private static string NormalizePrefix(string baseUrl)
            {
                if (string.IsNullOrWhiteSpace(baseUrl)) return null;
                // Ensure trailing slash
                if (!baseUrl.EndsWith("/")) baseUrl += "/";
                // Accept forms like http://+:7776/ or http://0.0.0.0:7776/
                // Return as-is
                return baseUrl;
            }

            private static int ExtractPort(string baseUrl)
            {
                if (string.IsNullOrWhiteSpace(baseUrl)) return -1;
                try
                {
                    // Try regex to grab :port
                    var m = Regex.Match(baseUrl, ":(\\d+)");
                    if (m.Success && int.TryParse(m.Groups[1].Value, out var p))
                        return p;
                }
                catch { }
                return -1;
            }

            private static string RunProcess(string fileName, string arguments)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using (var p = Process.Start(psi))
                    {
                        if (p == null) return null;
                        var outStr = p.StandardOutput.ReadToEnd();
                        var errStr = p.StandardError.ReadToEnd();
                        p.WaitForExit(5000);
                        return string.IsNullOrEmpty(outStr) ? errStr : outStr;
                    }
                }
                catch (Exception ex)
                {
                    return $"Process {fileName} failed: {ex.Message}";
                }
            }
        }
    }
}
