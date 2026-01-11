using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace TToApp.Services.Scheduled
{
    public class RDMonitorService : IHostedService, IDisposable
    {
        private readonly ILogger<RDMonitorService> _logger;
        private readonly IServiceProvider _services;
        private Timer _timer;

        public RDMonitorService(IServiceProvider services, ILogger<RDMonitorService> logger)
        {
            _services = services;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("⏰ RDMonitorService iniciado.");

            var now = DateTime.Now;
            var nextRun = now.Date.AddDays(1).AddMinutes(10);
            var delay = nextRun - now;

            _timer = new Timer(Ejecutar, null, delay, TimeSpan.FromDays(1));

            return Task.CompletedTask;
        }

        private async void Ejecutar(object state)
        {
            try
            {
                if (DateTime.Now.DayOfWeek == DayOfWeek.Sunday)
                {
                    _logger.LogInformation("📤 Ejecutando envío de resumen semanal...");

                    using var scope = _services.CreateScope();

                    var emailService = scope.ServiceProvider.GetRequiredService<EmailService>();
                    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                    var applicantService = scope.ServiceProvider.GetRequiredService<IApplicantContactService>();

                    // Puedes pasar el emailService o applicantService como necesites a RDResumenSender
                    var resumen = new RDResumenSender(scope.ServiceProvider, emailService, config);

                    await resumen.EnviarCorreosResumenAsync();

                    _logger.LogInformation("✅ Correos enviados correctamente.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error al ejecutar RDMonitorService.");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("🛑 RDMonitorService detenido.");
            _timer?.Dispose();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
