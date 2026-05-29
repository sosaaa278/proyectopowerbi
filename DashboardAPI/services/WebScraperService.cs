using HtmlAgilityPack;
using Microsoft.Playwright;
using DashboardAPI.Data;
using DashboardAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace DashboardAPI.Services
{
    /// <summary>
    /// Scrapes inconformidades data from CFE's internal portal.
    /// Uses Playwright (persistent context) for JavaScript-rendered pages.
    /// Session/cookies are shared across scraping calls via the same userDataDir.
    /// </summary>
    public class WebScraperService
    {
        // ── Configuration ──────────────────────────────────────────────────────────
        private const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

        private const int MaxRetries       = 3;
        private const int BaseRetryMs      = 2000;   // doubles on each retry (exponential backoff)
        private const int SlowMoMs         = 300;
        private const string PlaywrightDir = "playwright-data";

        // ── Selectors (centralized for change detection) ────────────────────────────
        private static class Sel
        {
            // Shared
            public const string Division   = "select[name='cveDivision']";
            public const string Zona       = "select[name='cveZona']";
            public const string Area       = "select[name='cveArea']";
            public const string FechaDesde = "input[name='fechaDesde']";
            public const string FechaHasta = "input[name='fechaHasta']";
            public const string Submit     = "#procesa";

            // Inconformidades
            public const string Proceso      = "select[name='cveProceso']";
            public const string ProcImproc   = "select[name='cveProcImproc']";
            public const string ResultTable  = "#TABLE_12";

            // Causas
            public const string Entidad          = "select[name='entidadFederativa']";
            public const string GrupoSolicitud   = "select[name='grupoSolicitud']";
            public const string CausaTerminacion = "select[name='cveCausaTerminacion']";
        }

        private readonly AppDbContext              _context;
        private readonly ILogger<WebScraperService> _logger;

        public WebScraperService(AppDbContext context, ILogger<WebScraperService> logger)
        {
            _context = context;
            _logger  = logger;
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // BROWSER HELPERS
        // ══════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Creates a persistent Playwright context that shares cookies/session
        /// across calls. Always dispose via the returned tuple.
        /// </summary>
        private async Task<(IPlaywright pw, IBrowserContext ctx)> CreateBrowserAsync(
            bool headless = true, int slowMo = SlowMoMs)
        {
            var pw      = await Playwright.CreateAsync();
            var dataDir = Path.Combine(Directory.GetCurrentDirectory(), PlaywrightDir);

            var ctx = await pw.Chromium.LaunchPersistentContextAsync(dataDir,
                new BrowserTypeLaunchPersistentContextOptions
                {
                    Headless  = headless,
                    Channel   = OperatingSystem.IsWindows() ? "msedge" : null,
                    UserAgent = UserAgent,
                    SlowMo    = slowMo,
                    Args      = ["--no-sandbox", "--disable-setuid-sandbox"]
                });

            return (pw, ctx);
        }

        /// <summary>
        /// Navigates to <paramref name="url"/> with exponential-backoff retries.
        /// Returns true on success, false if all attempts fail.
        /// </summary>
        private async Task<bool> NavigateWithRetryAsync(IPage page, string url)
        {
            for (int attempt = 0; attempt < MaxRetries; attempt++)
            {
                try
                {
                    await page.GotoAsync(url, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout   = 60_000
                    });
                    _logger.LogInformation("Navigated to {Url} (attempt {A})", url, attempt + 1);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Navigate attempt {A}/{M} failed: {Err}", attempt + 1, MaxRetries, ex.Message);
                    if (attempt < MaxRetries - 1)
                        await Task.Delay(BaseRetryMs * (int)Math.Pow(2, attempt));
                }
            }
            _logger.LogError("All {M} navigation attempts failed for {Url}", MaxRetries, url);
            return false;
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // HTML PARSING HELPERS
        // ══════════════════════════════════════════════════════════════════════════════

        /// <summary>Decodes HTML entities and collapses non-breaking spaces.</summary>
        private static string Normalize(HtmlNode node) =>
            System.Net.WebUtility.HtmlDecode(node.InnerText)
                .Replace(" ", " ")
                .Trim();

        /// <summary>
        /// Parser for causas TABLE_12.
        /// The header row mixes &lt;td&gt; (Sec, Grafica) and &lt;th&gt; (Clave, Descripcion…),
        /// so a th-only reader misaligns every column. This method reads ALL header cells
        /// by index, skips Grafica, and maps each data cell using its exact position.
        /// </summary>
        private static List<Dictionary<string, string>> ParseCausasTable(HtmlNode table)
        {
            var rows = new List<Dictionary<string, string>>();

            var headerRow = table.SelectSingleNode(".//thead/tr")
                         ?? table.SelectSingleNode(".//tr");
            if (headerRow == null) return rows;

            var allCells = headerRow.SelectNodes("./td|./th");
            if (allCells == null) return rows;

            var colMap = new List<(int idx, string name)>();
            for (int i = 0; i < allCells.Count; i++)
            {
                var name = Normalize(allCells[i]);
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (name.Contains("GRAF", StringComparison.OrdinalIgnoreCase)) continue;
                colMap.Add((i, name.Trim()));
            }

            if (colMap.Count == 0) return rows;

            // Only read tbody rows to avoid treating the header row as data
            var bodyRows = table.SelectNodes(".//tbody/tr");
            if (bodyRows == null) return rows;

            foreach (var tr in bodyRows)
            {
                var cells = tr.SelectNodes("./td");
                if (cells == null || cells.Count < 2) continue;

                var item = new Dictionary<string, string>();
                foreach (var (idx, name) in colMap)
                {
                    if (idx < cells.Count)
                        item[name] = Normalize(cells[idx]);
                }

                if (item.Count > 0) rows.Add(item);
            }

            return rows;
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // DATABASE PERSISTENCE
        // ══════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Persists scraped rows to the database.
        /// Loads existing keys for <paramref name="fechaConsulta"/> in a single query
        /// to avoid N+1 AnyAsync calls.
        /// </summary>
        private async Task PersistRowsAsync(
            List<Dictionary<string, string>> rows,
            DateTime fechaConsulta)
        {
            // One query to get all existing keys for this date
            var existing = await _context.Inconformidades
                .Where(x => x.FechaConsulta.Date == fechaConsulta.Date)
                .Select(x => new { x.SEC, x.AREA, x.Codigo })
                .ToListAsync();

            var existingSet = existing
                .Select(x => $"{x.SEC}|{x.AREA}|{x.Codigo}")
                .ToHashSet(StringComparer.Ordinal);

            int added = 0;
            foreach (var row in rows)
            {
                if (!row.TryGetValue("SEC",  out var sec))  continue;
                if (!row.TryGetValue("AREA", out var area)) continue;

                foreach (var (codigo, valor) in row)
                {
                    if (codigo == "SEC" || codigo == "AREA") continue;

                    var key = $"{sec}|{area}|{codigo}";
                    if (!existingSet.Add(key)) continue; // already exists or duplicate in batch

                    _context.Inconformidades.Add(new Inconformidad
                    {
                        FechaConsulta = fechaConsulta,
                        SEC           = sec,
                        AREA          = area,
                        Codigo        = codigo,
                        Valor         = valor
                    });
                    added++;
                }
            }

            if (added > 0) await _context.SaveChangesAsync();
            _logger.LogInformation("Persisted {Count} new records for {Date:yyyy-MM-dd}", added, fechaConsulta);
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // PUBLIC API
        // ══════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Scrapes the inconformidades summary table for the given date range,
        /// persists new rows to the DB, and returns the raw data.
        /// </summary>
        public async Task<List<Dictionary<string, string>>> GetTableData(
            string url, string fechaDesde, string fechaHasta)
        {
            _logger.LogInformation("GetTableData {Desde} → {Hasta}", fechaDesde, fechaHasta);
            var (pw, ctx) = await CreateBrowserAsync();

            try
            {
                var page = ctx.Pages.FirstOrDefault() ?? await ctx.NewPageAsync();

                if (!await NavigateWithRetryAsync(page, url))
                    throw new InvalidOperationException($"Could not load {url} after {MaxRetries} attempts.");

                // Validate critical selector before proceeding
                await page.WaitForSelectorAsync(Sel.Division, new() { Timeout = 60_000 });

                // ── Fill form ─────────────────────────────────────────────────────
                await page.SelectOptionAsync(Sel.Division,  "DC000"); await page.WaitForTimeoutAsync(1000);
                await page.SelectOptionAsync(Sel.Zona,      "00000"); await page.WaitForTimeoutAsync(800);
                await page.SelectOptionAsync(Sel.Area,      "00000"); await page.WaitForTimeoutAsync(800);
                await page.SelectOptionAsync(Sel.Proceso,   "D");     await page.WaitForTimeoutAsync(800);
                await page.SelectOptionAsync(Sel.ProcImproc,"T");     await page.WaitForTimeoutAsync(800);
                await page.FillAsync(Sel.FechaDesde, fechaDesde);
                await page.FillAsync(Sel.FechaHasta, fechaHasta);
                await page.WaitForTimeoutAsync(500);

                // ── Submit and wait for results ───────────────────────────────────
                await page.ClickAsync(Sel.Submit);

                try
                {
                    await page.WaitForSelectorAsync(Sel.ResultTable, new() { Timeout = 30_000 });
                }
                catch
                {
                    _logger.LogWarning("Result table {Sel} not detected — may be empty or selector changed", Sel.ResultTable);
                }
                await page.WaitForTimeoutAsync(2000);

                // ── Parse HTML ────────────────────────────────────────────────────
                var html = await page.ContentAsync();
                File.WriteAllText("debug.html", html);

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var table = doc.DocumentNode.SelectSingleNode("//table[@id='TABLE_12']");
                if (table == null)
                {
                    _logger.LogWarning("TABLE_12 not found — scraping returned no data");
                    return [];
                }

                // TABLE_12 structure: row 1 = title, row 2 = <th> headers, row 3+ = data
                var headerCells = table.SelectNodes(".//tr[2]/th");
                if (headerCells == null) return [];

                var headers = new List<string> { "SEC", "AREA" };
                foreach (var h in headerCells)
                {
                    var t = h.InnerText.Trim();
                    if (!string.IsNullOrWhiteSpace(t)) headers.Add(t);
                }

                var result  = new List<Dictionary<string, string>>();
                var allRows = table.SelectNodes(".//tr");
                if (allRows == null) return result;

                for (int ri = 2; ri < allRows.Count; ri++)
                {
                    var tr    = allRows[ri];
                    var cells = tr.SelectNodes("./td");
                    if (cells == null || cells.Count < 3) continue;

                    var item = new Dictionary<string, string>
                    {
                        ["SEC"]  = cells[0].InnerText.Trim(),
                        ["AREA"] = cells[1].InnerText.Trim()
                    };

                    for (int i = 2; i < cells.Count && i < headers.Count; i++)
                        item[headers[i]] = cells[i].InnerText.Trim();

                    result.Add(item);
                }

                // ── Persist ───────────────────────────────────────────────────────
                await PersistRowsAsync(result, DateTime.Parse(fechaHasta));
                _logger.LogInformation("GetTableData returned {Count} rows", result.Count);
                return result;
            }
            finally
            {
                await ctx.CloseAsync();
                pw.Dispose();
            }
        }

        /// <summary>Alias used by the compare flow; delegates to GetTableData.</summary>
        public Task<List<Dictionary<string, string>>> GetComparisonData(
            string url, string fechaDesde, string fechaHasta)
            => GetTableData(url, fechaDesde, fechaHasta);

        /// <summary>
        /// Scrapes causas de terminacion de inconformidades.
        /// Only updates the date fields via JS — leaves other selects at
        /// their session-persisted values (which were valid in the previous run).
        /// </summary>
        public async Task<List<Dictionary<string, string>>> GetCausasData(
            string fechaDesde, string fechaHasta, string tipoSolTermino = "E02")
        {
            const string url = "https://cssnal.cfe.mx/iessInformesV2/causasTerminacion.asp";
            _logger.LogInformation("GetCausasData {Desde} -> {Hasta} code={Code}", fechaDesde, fechaHasta, tipoSolTermino);

            // input[type=date] requires YYYY-MM-DD; controller sends YYYY/MM/DD
            var desde = fechaDesde.Replace("/", "-");
            var hasta  = fechaHasta.Replace("/", "-");

            // headless:false so the browser window is visible during debugging
            var (pw, ctx) = await CreateBrowserAsync(headless: false, slowMo: 1200);
            try
            {
                var page = ctx.Pages.Count > 0 ? ctx.Pages[0] : await ctx.NewPageAsync();

                // Track whether a "no data" JS alert fires — if it does, the old
                // TABLE_12 stays in the DOM and must not be treated as real results.
                bool noDataDialog = false;
                page.Dialog += async (_, dialog) =>
                {
                    _logger.LogInformation("Dialog for {Code}: {Msg}", tipoSolTermino, dialog.Message);
                    noDataDialog = true;
                    await dialog.AcceptAsync();
                };

                if (!await NavigateWithRetryAsync(page, url))
                    throw new InvalidOperationException("Could not load causas page.");

                // Dump initial HTML regardless of what the page contains
                File.WriteAllText("debug_causas_initial.html", await page.ContentAsync());

                // Wait for the date input — if it times out the session likely expired
                try
                {
                    await page.WaitForSelectorAsync(
                        "input[name='fechaDesde']", new() { Timeout = 30_000 });
                }
                catch (Exception ex)
                {
                    _logger.LogError("WaitForSelector fechaDesde failed: {Err}. Check debug_causas_initial.html", ex.Message);
                    throw new InvalidOperationException(
                        "La página de causas no cargó el formulario (sesión expirada o error de red). Revisa debug_causas_initial.html", ex);
                }

                // Fill form fields and return a diagnostic object so we can confirm values
                var formState = await page.EvaluateAsync<Dictionary<string, string>>(
                    "([d, h, ts]) => {" +
                    "  var setField = function(name, val) {" +
                    "    var el = document.querySelector(\"select[name='\" + name + \"']\");" +
                    "    if (el) el.value = val;" +
                    "  };" +
                    "  var fd = document.querySelector(\"input[name='fechaDesde']\");" +
                    "  var fh = document.querySelector(\"input[name='fechaHasta']\");" +
                    "  if (fd) fd.value = d;" +
                    "  if (fh) fh.value = h;" +
                    "  setField('cveDivision',          'DC000');" +
                    "  setField('cveZona',              '00000');" +
                    "  setField('cveArea',              '00000');" +
                    "  setField('entidadFederativa',    '0');" +
                    "  setField('cveMunicipio',         'T');" +
                    "  setField('grupoSolicitud',       'RSS');" +
                    "  setField('tipoSolInicio',        'T');" +
                    "  setField('tipoSolTermino',       ts);" +
                    "  setField('cveCausaTerminacion',  'T');" +
                    "  setField('cvePivoteTerminacion', 'cveCausaTerminacion');" +
                    "  var get = function(name) {" +
                    "    var el = document.querySelector(\"[name='\" + name + \"']\");" +
                    "    return el ? el.value : 'NOT_FOUND';" +
                    "  };" +
                    "  return {" +
                    "    fechaDesde:           get('fechaDesde')," +
                    "    fechaHasta:           get('fechaHasta')," +
                    "    grupoSolicitud:       get('grupoSolicitud')," +
                    "    tipoSolTermino:       get('tipoSolTermino')," +
                    "    cvePivoteTerminacion: get('cvePivoteTerminacion')" +
                    "  };" +
                    "}",
                    new[] { desde, hasta, tipoSolTermino });

                _logger.LogInformation("Form state before submit: {@State}", formState);

                // Wait for the form POST to complete and the new page to settle.
                // TABLE_12 already exists on initial load so we CANNOT wait for it —
                // we must wait for the page to reload after the POST.
                await page.ClickAsync("#procesa");
                try
                {
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                        new() { Timeout = 45_000 });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("NetworkIdle after submit timed out for {Code}: {Err}", tipoSolTermino, ex.Message);
                }
                await page.WaitForTimeoutAsync(1500);

                var html = await page.ContentAsync();
                File.WriteAllText("debug_causas.html", html);

                _logger.LogInformation(
                    "debug_causas.html size={Size} has500={Has500}",
                    html.Length,
                    html.Contains("internal server error"));

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // If the portal showed a "no data" alert the old TABLE_12 is still
                // in the DOM — treat that as an empty result rather than stale data.
                if (noDataDialog)
                {
                    _logger.LogInformation("No-data dialog detected for {Code} — returning []", tipoSolTermino);
                    return [];
                }

                var table = doc.DocumentNode.SelectSingleNode("//table[@id='TABLE_12']");
                if (table == null)
                {
                    _logger.LogWarning("TABLE_12 not found in causas HTML for {Code}", tipoSolTermino);
                    return [];
                }

                // ParseCausasTable reads ALL header cells (td + th) by index,
                // which correctly aligns Sec, Clave, Descripcion, Causas, %
                // while skipping the Grafica column.
                var rows = ParseCausasTable(table);
                _logger.LogInformation("GetCausasData({Code}) returned {Count} rows", tipoSolTermino, rows.Count);
                return rows;
            }
            finally
            {
                await ctx.CloseAsync();
                pw.Dispose();
            }
        }
    }
}
