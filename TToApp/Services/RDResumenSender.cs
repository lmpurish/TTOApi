namespace TToApp.Services.Scheduled
{
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.EntityFrameworkCore;
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Data.SqlClient;
    using System.Net.Mail;

    using TToApp.Model;
  
    public class RDResumenSender
    {
        private readonly IServiceProvider _services;
        private readonly TimeSpan _interval = TimeSpan.FromHours(12); // Puedes ajustarlo a diario o menos
        private readonly EmailService _emailService;
        private readonly IConfiguration _configuration;
        public RDResumenSender(IServiceProvider services, EmailService emailService, IConfiguration configuration)
        {
            _services = services;
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _configuration = configuration;
        }
     /*   protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var scope = _services.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var packages = await context.Packages
                    .Include(p => p.Routes)
                        .ThenInclude(r => r.Zone)
                    .Where(p => p.Status == PackageStatus.RD && p.DaysElapsed >= 3 && !p.Notified)
                    .ToListAsync(stoppingToken);

                foreach (var package in packages)
                {
                    var warehouseId = package.Routes?.Zone?.IdWarehouse;

                    if (warehouseId == null) continue;

                    // 🔍 Buscar manager del warehouse
                    var manager = await context.Users
                        .Where(u => u.UserRole == Role.Manager && u.WarehouseId == warehouseId)
                        .FirstOrDefaultAsync(stoppingToken);

                    // 🎯 Si no hay manager → buscar admin
                    User? recipient = manager;

                    if (recipient == null)
                    {
                        recipient = await context.Users
                            .Where(u => u.UserRole == Role.Admin)
                            .OrderBy(u => u.Id) // Puedes cambiar por criterio específico
                            .FirstOrDefaultAsync(stoppingToken);
                    }

                    if (recipient == null) continue; // 🔒 Seguridad: si no hay nadie, no hacer nada

                    var notification = new Notification
                    {
                        UserId = recipient.Id,
                        Title = "📦 Pending RD package",
                        Message = $"The package {package.Tracking} has 3 days in RD status.",
                        Type = NotificationType.Warning,
                        IsRead = false,
                        CreatedAt = DateTime.Now
                    };

                    context.Notifications.Add(notification);

                    package.Notified = true;
                }

                await context.SaveChangesAsync(stoppingToken);

                await Task.Delay(_interval, stoppingToken);
            }
        }*/

        public async Task EnviarCorreosResumenAsync()
        {
            string connectionString = _configuration.GetConnectionString("DevConnection"); // O ajústalo según cómo obtienes la cadena

            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(); // 🔴 ESTO FALTABA

            var cmd = new SqlCommand(@"
                WITH Datos AS (
            SELECT 
                u.Id AS UserId,
                u.Name,
                u.LastName,
                u.Email,
                CAST(r.[Date] AS DATE) AS Fecha,
                SUM(r.Volumen) AS Volumen,
                SUM(r.DeliveryStops) AS DeliveryStops
            FROM 
                Routes r
            JOIN Users u ON r.UserId = u.Id
            JOIN Warehouses w ON u.WarehouseId = w.Id
            WHERE 
                r.[Date] BETWEEN CAST(DATEADD(DAY, -8, GETDATE()) AS DATE) AND CAST(DATEADD(DAY, -2, GETDATE()) AS DATE)
                AND r.Volumen > 0 
                AND u.UserRole = 3
                AND w.SendPayroll = 1
            GROUP BY 
                u.Id, u.Name, u.LastName, u.Email, CAST(r.[Date] AS DATE)
        )
        SELECT 
            d.UserId,
            d.Name,
            d.LastName,
            d.Email,
            (
                '<h3>Hola ' + d.Name + ' ' + d.LastName + ', este es tu resumen de la semana:</h3>' +
                '<table border=""1"" cellpadding=""5"" cellspacing=""0"">' +
                '<tr><th>Fecha</th><th>Volumen</th><th>Paradas</th></tr>' +
                (
                    SELECT 
                        '<tr><td>' + CONVERT(varchar, Fecha, 23) + '</td><td>' + 
                        CAST(SUM(Volumen) AS varchar) + '</td><td>' + 
                        CAST(SUM(DeliveryStops) AS varchar) + '</td></tr>'
                    FROM Datos as d2
                    WHERE d2.UserId = d.UserId
                    GROUP BY d2.Fecha
                    ORDER BY d2.Fecha
                    FOR XML PATH(''), TYPE
                ).value('.', 'varchar(max)') +
                (
                    SELECT 
                        '<tr style=""font-weight:bold;""><td>Total</td><td>' +
                        CAST(SUM(Volumen) AS varchar) + '</td><td>' +
                        CAST(SUM(DeliveryStops) AS varchar) + '</td></tr>'
                    FROM Datos as d3
                    WHERE d3.UserId = d.UserId
                ) +
                '</table>'
            ) AS HtmlBody
        FROM Datos d
        GROUP BY d.UserId, d.Name, d.LastName, d.Email;
            ", conn);

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string nombre = reader.GetString(1);
                string apellido = reader.GetString(2);
                string email = reader.GetString(3);
                string html = reader.GetString(4);

                try
                {
                    await _emailService.SendEmailAsync(
                        toEmail: email,
                        subject: "Weekly Sumary!!",
                        templateFileName: "WeeklySumary.cshtml",
                        placeholders: new Dictionary<string, string> {
                    { "tablaResumen", html }
                        },
                        copy: false
                    );

                    Console.WriteLine($"✅ Enviado a {nombre} {apellido} ({email})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error al enviar a {email}: {ex.Message}");
                }
            }
        }

    }
}
