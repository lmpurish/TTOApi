using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using TToApp.Services.EarlyWarnings;

namespace TToApp.Services.Scheduled
{
    public class RDMonitorService : IHostedService, IDisposable
    {
        private readonly ILogger<RDMonitorService> _logger;
        private readonly IServiceProvider _services;
        private Timer _weeklyTimer;
        private Timer _dailyUnassignedZonesTimer;
        private Timer _earlyWarningsTimer;
        private Timer _missingDailyPackagesTimer;

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
           _weeklyTimer = new Timer(EjecutarResumenSemanal, null, weeklyDelay, TimeSpan.FromDays(1));
            
           var dailyDelay = GetDelayUntil(new TimeSpan(6, 0, 0));
           //var dailyDelay = GetDelayUntil(DateTime.Now.AddMinutes(1).TimeOfDay); // esto es para prrobar futuros cron
            _dailyUnassignedZonesTimer = new Timer(
                EjecutarUnassignedZones,
                null,
                dailyDelay,
                TimeSpan.FromDays(1)
            );

            var earlyWarningsDelay = GetDelayUntil(new TimeSpan(6, 0, 0));

            _earlyWarningsTimer = new Timer(
                EjecuteEarlyWarnings,
                null,
                earlyWarningsDelay,
                TimeSpan.FromDays(1)
            );
            //  _earlyWarningsTimer = new Timer(
            //     EjecuteEarlyWarnings,
            //     null,
            //     TimeSpan.FromSeconds(10),
            //     Timeout.InfiniteTimeSpan // solo una vez
            // );
            var missingPackagesDelay = GetDelayUntil(new TimeSpan(11, 30, 0));

            _missingDailyPackagesTimer = new Timer(
                EjecuteMissingDailyPackages,
                null,
                missingPackagesDelay,
                TimeSpan.FromDays(1)
            );
            // _missingDailyPackagesTimer = new Timer(
            //     EjecuteMissingDailyPackages,
            //     null,
            //     TimeSpan.FromSeconds(10),
            //     Timeout.InfiniteTimeSpan // solo una vez
            // );
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

        private async void EjecuteEarlyWarnings(object state)
        {
            try
            {
                _logger.LogInformation("⚠️ Running Early Warnings to check hiring capacity...");

                using var scope = _services.CreateScope();

                var earlyWarningService =
                    scope.ServiceProvider.GetRequiredService<IEarlyWarningService>();
                var earlyWarningNotificationService =
                    scope.ServiceProvider.GetRequiredService<IEarlyWarningNotificationService>();

               // var referenceDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));

                await earlyWarningService.CheckHiringCapacityAsync(null);
                await earlyWarningNotificationService.NotifyPendingHiringWarningsAsync();

                _logger.LogInformation("✅ Early Warnings processed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error executing Early Warnings.");
            }
        }

        private async void EjecuteMissingDailyPackages(object state)
        {
            try
            {
                _logger.LogInformation("📦 Checking missing daily packages from the previous day...");

                using var scope = _services.CreateScope();

                var earlyWarningService =
                    scope.ServiceProvider.GetRequiredService<IEarlyWarningService>();

                var notificationService =
                    scope.ServiceProvider.GetRequiredService<IEarlyWarningNotificationService>();

                var yesterday = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));

                await earlyWarningService.CheckMissingDailyPackagesAsync(yesterday);
                await notificationService.NotifyPendingMissingPackagesWarningsAsync();

                _logger.LogInformation("✅ Checking of missing daily packages completed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error checking missing daily packages.");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("🛑 RDMonitorService detenido.");
            _weeklyTimer?.Dispose();
            _dailyUnassignedZonesTimer?.Dispose();
            _earlyWarningsTimer?.Dispose();
            _missingDailyPackagesTimer?.Dispose();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _weeklyTimer?.Dispose();
            _dailyUnassignedZonesTimer?.Dispose();
            _earlyWarningsTimer?.Dispose();
            _missingDailyPackagesTimer?.Dispose();
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
