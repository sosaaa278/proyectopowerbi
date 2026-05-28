using HtmlAgilityPack;
using Microsoft.Playwright;
using DashboardAPI.Models;

namespace DashboardAPI.Services
{
    public class FullCompareService
    {
        private FullCompareResponse? _cache;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public async Task<FullCompareResponse> GetFullCompareAsync()
        {
            if (_cache != null) return _cache;

            await _lock.WaitAsync();
            try
            {
                if (_cache != null) return _cache;

                var today = DateTime.Now;

                var previousYear = today.Year - 1;
                var currentYear = today.Year;
                var url = "https://cssnal.cfe.mx/Inconformidades/solTermino.asp";

                using var playwright = await Playwright.CreateAsync();

                var userDataDir = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "playwright-data");

                var isWindows = OperatingSystem.IsWindows();

                var context = await playwright.Chromium
                    .LaunchPersistentContextAsync(
                        userDataDir,
                        new BrowserTypeLaunchPersistentContextOptions
                        {
                            Headless = true,
                            Channel = isWindows ? "msedge" : null,
                            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36",
                            SlowMo = 500,
                            Args = ["--no-sandbox", "--disable-setuid-sandbox"]
                        });

                try
                {
                    var data2025 = await ScrapeYearAsync(
                        context,
                        url,
                        $"{previousYear}/01/01",
                        $"{previousYear}/{today.Month:D2}/{today.Day:D2}");

                    await Task.Delay(3000);

                    var data2026 = await ScrapeYearAsync(
                        context,
                        url,
                        $"{currentYear}/01/01",
                        $"{currentYear}/{today.Month:D2}/{today.Day:D2}");

                    _cache = new FullCompareResponse
                    {
                        RawData2026 = data2026,
                        Compare = BuildResult(data2025, data2026)
                    };
                    return _cache;
                }
                finally
                {
                    await context.CloseAsync();
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public void InvalidateCache() => _cache = null;

        private static string CleanCell(HtmlNode cell) =>
            System.Net.WebUtility.HtmlDecode(cell.InnerText)
                .Replace(" ", "")
                .Trim();

        private async Task<List<Dictionary<string, string>>> ScrapeYearAsync(
            IBrowserContext context,
            string url,
            string fechaDesde,
            string fechaHasta)
        {
            var result = new List<Dictionary<string, string>>();

            var page = context.Pages.FirstOrDefault()
                ?? await context.NewPageAsync();

            bool loaded = false;
            int retries = 0;

            while (!loaded && retries < 3)
            {
                try
                {
                    await page.GotoAsync(url, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 300000
                    });
                    loaded = true;
                }
                catch
                {
                    retries++;
                    await Task.Delay(3000);
                }
            }

            await page.WaitForTimeoutAsync(3000);

            await page.WaitForSelectorAsync(
                "select[name='cveDivision']",
                new() { Timeout = 300000 });

            await page.SelectOptionAsync("select[name='cveDivision']", "DC000");
            await page.WaitForTimeoutAsync(1000);
            await page.SelectOptionAsync("select[name='cveZona']", "00000");
            await page.WaitForTimeoutAsync(1000);
            await page.SelectOptionAsync("select[name='cveArea']", "00000");
            await page.WaitForTimeoutAsync(1000);
            await page.SelectOptionAsync("select[name='cveProceso']", "D");
            await page.WaitForTimeoutAsync(1000);
            await page.SelectOptionAsync("select[name='cveProcImproc']", "T");
            await page.WaitForTimeoutAsync(1000);

            await page.FillAsync("input[name='fechaDesde']", fechaDesde);
            await page.FillAsync("input[name='fechaHasta']", fechaHasta);
            await page.WaitForTimeoutAsync(1000);

            await page.ClickAsync("#procesa");
            await page.WaitForTimeoutAsync(10000);

            var html = await page.ContentAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var table = doc.DocumentNode.SelectSingleNode("//table[@id='TABLE_12']");
            if (table == null) return result;

            var headerCells = table.SelectNodes(".//tr[2]/th");
            if (headerCells == null) return result;

            var headers = new List<string> { "SEC", "AREA" };
            foreach (var header in headerCells)
            {
                var text = CleanCell(header);
                if (!string.IsNullOrWhiteSpace(text))
                    headers.Add(text);
            }

            var rows = table.SelectNodes(".//tr");
            if (rows == null) return result;

            foreach (var row in rows.Skip(2))
            {
                var cells = row.SelectNodes("./td");
                if (cells == null || cells.Count < 3) continue;

                var item = new Dictionary<string, string>();
                item["SEC"] = CleanCell(cells[0]);
                item["AREA"] = CleanCell(cells[1]);

                for (int i = 2; i < cells.Count; i++)
                {
                    if (i >= headers.Count) break;
                    item[headers[i]] = CleanCell(cells[i]);
                }

                result.Add(item);
            }

            return result;
        }

        private static Dictionary<string, List<Comparativo>> BuildResult(
            List<Dictionary<string, string>> data2025,
            List<Dictionary<string, string>> data2026)
        {
            var result = new Dictionary<string, List<Comparativo>>();

            if (data2026.Count == 0) return result;

            var codes = data2026[0].Keys
                .Where(k => k != "SEC" && k != "AREA")
                .ToList();

            foreach (var code in codes)
            {
                var comparativos = new List<Comparativo>();

                foreach (var row2026 in data2026)
                {
                    var area = row2026["AREA"];
                    var row2025 = data2025.FirstOrDefault(x => x["AREA"] == area);

                    double val2025 = 0;
                    double val2026 = 0;

                    if (row2025 != null && row2025.ContainsKey(code))
                        double.TryParse(row2025[code].Replace(",", ""), out val2025);

                    if (row2026.ContainsKey(code))
                        double.TryParse(row2026[code].Replace(",", ""), out val2026);

                    double variacion = 0;
                    if (val2025 > 0 || val2026 > 0)
                        variacion = ((val2026 - val2025) / Math.Max(val2025, 1)) * 100;

                    comparativos.Add(new Comparativo
                    {
                        AREA = area,
                        Total2025 = val2025,
                        Total2026 = val2026,
                        Variacion = Math.Round(variacion, 2)
                    });
                }

                result[code] = comparativos;
            }

            return result;
        }
    }
}
