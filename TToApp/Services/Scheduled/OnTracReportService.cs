using Microsoft.Playwright;

namespace TToApp.Services.Scheduled
{
    public class OnTracReportService
    {
        public async Task DownloadBusinessReportAsync(
        string username,
        string password,
        int driverId,
        DateTime beginDate,
        DateTime endDate)
        {
            using var playwright = await Playwright.CreateAsync();

            await using var browser = await playwright.Chromium.LaunchAsync(new()
            {
                Headless = false
            });

            var page = await browser.NewPageAsync();

            // 1. Login
            await page.GotoAsync("https://fastrac.ontrac.com/identityserver/Account/Login");

            await page.FillAsync("input[name='Username'], input[type='email']", username);
            await page.FillAsync("input[name='Password'], input[type='password']", password);

            await page.ClickAsync("button[type='submit'], input[type='submit']");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // 2. Ir al reporte
            var reportUrl =
                $"https://fastrac.ontrac.com/reportviewer/Report" +
                $"?reportname=Business%20Report" +
                $"&driverId={driverId}" +
                $"&beginDate={beginDate:yyyy-MM-dd}" +
                $"&endDate={endDate:yyyy-MM-dd}";

            await page.GotoAsync(reportUrl, new()
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });

            // 3. Screenshot para verificar
            await page.ScreenshotAsync(new()
            {
                Path = $"business-report-{driverId}-{beginDate:yyyy-MM-dd}.png",
                FullPage = true
            });

            Console.WriteLine("Reporte abierto correctamente.");
            Console.WriteLine(page.Url);
        }
    }
}
