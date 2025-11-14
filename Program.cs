using System;
using System.Configuration;
using Topshelf;
using Microsoft.Owin.Hosting;

namespace IND_CRM_APIs
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
                x.SetServiceName("IND_CRM_APIs");
                x.SetDisplayName("IND Test APIs (Axapta SelfHost)");
                x.SetDescription("Servicio OWIN SelfHost para Axapta 3.0");
            });
        }
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
        /// Si no hay configuración en App.config, usa el puerto por defecto (7777).
        /// </summary>
        public void Start()
        {
            // Si está configurada la URL en App.config, úsala. Si no, usa el puerto por defecto.
            string baseUrl = ConfigurationManager.AppSettings["BaseUrl"] ?? "http://+:7777/";

            try
            {
                _webApp = WebApp.Start<Startup>(baseUrl);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] API iniciada correctamente en {baseUrl}");
                Console.WriteLine("Presiona Ctrl+C para detener manualmente.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al iniciar la API en {baseUrl}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Detiene el servicio OWIN y libera los recursos.
        /// </summary>
        public void Stop()
        {
            _webApp?.Dispose();
            Console.WriteLine("API detenida correctamente.");
        }
    }
}
