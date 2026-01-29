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
        private Timer _weeklyTimer;
        private Timer _dailyUnassignedZonesTimer;


        public RDMonitorService(IServiceProvider services, ILogger<RDMonitorService> logger)
        {
            _services = services;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("⏰ RDMonitorService iniciado.");

            var now = DateTime.Now;
            var nextWeeklyRun = now.Date.AddDays(1).AddMinutes(10);
            var weeklyDelay = nextWeeklyRun - now;
            new Timer(EjecutarResumenSemanal, null, weeklyDelay, TimeSpan.FromDays(1));
            
           var dailyDelay = GetDelayUntil(new TimeSpan(6, 0, 0));
           //var dailyDelay = GetDelayUntil(DateTime.Now.AddMinutes(1).TimeOfDay); // esto es para prrobar futuros cron
            _dailyUnassignedZonesTimer = new Timer(
                EjecutarUnassignedZones,
                null,
                dailyDelay,
                TimeSpan.FromDays(1)
            );
            return Task.CompletedTask;
        }

        private async void EjecutarResumenSemanal(object state)
        {
            try
            {
                if (DateTime.Now.DayOfWeek != DayOfWeek.Sunday)
                    return;

                _logger.LogInformation("📤 Ejecutando envío de resumen semanal...");

                using var scope = _services.CreateScope();

                var emailService = scope.ServiceProvider.GetRequiredService<EmailService>();
                var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

                var resumen = new RDResumenSender(scope.ServiceProvider, emailService, config);
                await resumen.EnviarCorreosResumenAsync();

                _logger.LogInformation("✅ Correos semanales enviados.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en resumen semanal.");
            }
        }

        private async void EjecutarUnassignedZones(object state)
        {
            try
            {
                _logger.LogInformation("🚚 Ejecutando notificaciones de rutas sin zona (6:00 AM)...");

                using var scope = _services.CreateScope();

                var notificationService =
                    scope.ServiceProvider.GetRequiredService<INotificationService>();

                await notificationService.unassingnedZonesByManagerOntrac();

                _logger.LogInformation("✅ Notificaciones de rutas sin zona creadas.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en notificaciones de rutas sin zona.");
            }
        }


        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("🛑 RDMonitorService detenido.");
            _weeklyTimer?.Dispose();
            _dailyUnassignedZonesTimer?.Dispose();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _weeklyTimer?.Dispose();
            _dailyUnassignedZonesTimer?.Dispose();
        }

        private static TimeSpan GetDelayUntil(TimeSpan targetTime)
        {
            var now = DateTime.Now;
            var todayTarget = now.Date.Add(targetTime);

            return todayTarget > now
                ? todayTarget - now
                : todayTarget.AddDays(1) - now;
        }

    }
}
