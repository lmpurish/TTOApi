using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using TToApp.Model;

namespace TToApp.Services.Payroll
{
    public class PayRunApprovedSender
    {
        private readonly ApplicationDbContext _db;
        private readonly EmailService _emailService;
        private readonly ILogger<PayRunApprovedSender> _logger;

        public PayRunApprovedSender(ApplicationDbContext db, EmailService emailService, ILogger<PayRunApprovedSender> logger)
        {
            _db = db;
            _emailService = emailService;
            _logger = logger;
        }

        public async Task SendLatestPayRunLineAsync(long driverId)
        {
            try
            {
                // 1) Usuario (email)
                var user = await _db.Set<User>()
                    .AsNoTracking()
                    .Where(u => u.Id == driverId)
                    .Select(u => new
                    {
                        u.Id,
                        u.Email,
                        u.Name,
                        u.LastName
                    })
                    .FirstOrDefaultAsync();

                if (user is null)
                {
                    _logger.LogWarning("PayRunApproved email: User {DriverId} not found.", driverId);
                    return;
                }

                if (string.IsNullOrWhiteSpace(user.Email))
                {
                    _logger.LogWarning("PayRunApproved email: User {DriverId} has no email.", driverId);
                    return;
                }

                // 2) Último PayRun Approved
                var payRun = await _db.Set<PayRun>()
                    .AsNoTracking()
                    .Where(pr => pr.DriverId == driverId && pr.Status == "Approved")
                    .Join(
                        _db.Set<PayPeriod>().AsNoTracking(),
                        pr => pr.PayPeriodId,
                        pp => pp.Id,
                        (pr, pp) => new
                        {
                            pr.Id,
                            pr.GrossAmount,
                            pr.NetAmount,
                            pr.CalculatedAt,
                            pr.Status,
                            PeriodStartDate = pp.StartDate,
                            PeriodEndDate = pp.EndDate
                        }
                    )
                    .OrderByDescending(x => x.CalculatedAt ?? DateTime.MinValue)
                    .ThenByDescending(x => x.Id)
                    .FirstOrDefaultAsync();

                if (payRun is null)
                {
                    _logger.LogWarning("PayRunApproved email: No Approved PayRun for DriverId={DriverId}.", driverId);
                    return;
                }

                // 3) Líneas
                var lines = await _db.Set<PayRunLine>()
                    .AsNoTracking()
                    .Where(l => l.PayRunId == payRun.Id)
                    .OrderBy(l => l.RouteDate)
                    .ThenBy(l => l.Id)
                    .Select(l => new
                    {
                        l.SourceType,
                        l.Description,
                        l.Qty,
                        l.Rate,
                        l.Amount
                    })
                    .ToListAsync();

                var htmlTabla = BuildPayRunLinesHtml(lines, payRun);

                // 4) Enviar
                await _emailService.SendEmailAsync(
                    toEmail: user.Email!,
                    subject: $"PayRun Approved #{payRun.Id}",
                    templateFileName: "PayRunApproved.cshtml",
                    placeholders: new Dictionary<string, string>
                    {
                { "driverName", $"{user.Name} {user.LastName}".Trim() },
                { "payPeriod", $"{payRun.PeriodStartDate:MMM dd yyyy} - {payRun.PeriodEndDate:MMM dd yyyy}" },
                { "calculatedAt", payRun.CalculatedAt?.ToString("MMM dd yyyy", CultureInfo.InvariantCulture) ?? "" },
                { "gross", payRun.GrossAmount.ToString("0.00") },
                { "status", (payRun.Status ?? "").Trim() },
                { "tablaPayRun", htmlTabla }
                    },
                    copy: false
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PayRunApproved email failed for DriverId={DriverId}.", driverId);
                // no throw: es fire-and-forget
            }
        }


        private static string BuildPayRunLinesHtml<T>(List<T> lines, dynamic payRun)
        {
            // lines viene como objetos anónimos con props: RouteDate, Description, Qty, Rate, Amount, ZoneArea, ZoneId, Tags
            var sb = new StringBuilder();

            sb.AppendLine("<h3>PayRun Lines</h3>");
            sb.AppendLine("<table border=\"1\" cellpadding=\"5\" cellspacing=\"0\">");
            sb.AppendLine("<tr>");
            sb.AppendLine("<th>Type</th><th>Description</th><th>Qty</th><th>Rate</th><th>Amount</th>");
            sb.AppendLine("</tr>");

            decimal total = 0m;

            foreach (dynamic l in lines)
            {
                // DateTime? dt = l.RouteDate;
                // var dateStr = dt.HasValue
                //     ? dt.Value.ToString("MMM dd yyyy", CultureInfo.InvariantCulture)
                //     : "";
                var  sourceType  = System.Net.WebUtility.HtmlEncode((string?)l.SourceType ?? "");
                var desc = System.Net.WebUtility.HtmlEncode((string?)l.Description ?? "");
                //var zoneArea = System.Net.WebUtility.HtmlEncode((string?)l.ZoneArea ?? "");
                //var tags = System.Net.WebUtility.HtmlEncode((string?)l.Tags ?? "");

                decimal qty = l.Qty;
                decimal rate = l.Rate;
                decimal amount = l.Amount;

                total += amount;

                sb.AppendLine("<tr>");
                sb.AppendLine($"<td>{sourceType}</td>");
                sb.AppendLine($"<td>{desc}</td>");
                sb.AppendLine($"<td style=\"text-align:right\">{qty:0.##}</td>");
                sb.AppendLine($"<td style=\"text-align:right\">{rate:0.00}</td>");
                sb.AppendLine($"<td style=\"text-align:right\">{amount:0.00}</td>");
                //sb.AppendLine($"<td>{zoneArea}</td>");
                //sb.AppendLine($"<td>{(l.ZoneId == null ? "" : l.ZoneId.ToString())}</td>");
                //sb.AppendLine($"<td>{tags}</td>");
                sb.AppendLine("</tr>");
            }

            sb.AppendLine($"<tr style=\"font-weight:bold;\"><td colspan=\"4\">Total</td><td style=\"text-align:right\">{total:0.00}</td><td colspan=\"3\"></td></tr>");
            sb.AppendLine("</table>");

            return sb.ToString();
        }
    }
}
