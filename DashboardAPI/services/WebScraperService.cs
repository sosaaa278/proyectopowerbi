using HtmlAgilityPack;
using Microsoft.Playwright;
using System.IO;
using DashboardAPI.Data;
using DashboardAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace DashboardAPI.Services
{
    public class WebScraperService
    {

        private readonly AppDbContext _context;

public WebScraperService(
    AppDbContext context)
{
    _context = context;
}
        public async Task<List<Dictionary<string, string>>> GetTableData(
            string url, 
            string fechaDesde,
            string fechaHasta)
        {
            var result =
                new List<Dictionary<string, string>>();

            using var playwright =
                await Playwright.CreateAsync();
var userDataDir =
    Path.Combine(
        Directory.GetCurrentDirectory(),
        "playwright-data");
var isWindows = OperatingSystem.IsWindows();

var launchOptions =
    new BrowserTypeLaunchPersistentContextOptions
    {
        Headless = true,

        Channel = isWindows ? "msedge" : null,

        UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36",

        SlowMo = 500,

        Args = ["--no-sandbox", "--disable-setuid-sandbox"]
    };

var context =
    await playwright.Chromium
        .LaunchPersistentContextAsync(userDataDir, launchOptions);
            var page =
                context.Pages.FirstOrDefault()
                ?? await context.NewPageAsync();

            // =========================
            // ABRIR URL
            // =========================

            bool loaded = false;

int retries = 0;

while (!loaded && retries < 3)
{
    try
    {
        await page.GotoAsync(
            url,
            new PageGotoOptions
            {
                WaitUntil =
                    WaitUntilState.DOMContentLoaded,

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

            // =========================
            // ESPERAR FORMULARIO
            // =========================

            await page.WaitForSelectorAsync(
                "select[name='cveDivision']",
                  new()
    {
        Timeout = 300000
    });

            // =========================
            // SELECCIONAR FILTROS
            // =========================

            await page.SelectOptionAsync(
                "select[name='cveDivision']",
                "DC000");

            await page.WaitForTimeoutAsync(1000);

            await page.SelectOptionAsync(
                "select[name='cveZona']",
                "00000");

            await page.WaitForTimeoutAsync(1000);

            await page.SelectOptionAsync(
                "select[name='cveArea']",
                "00000");

            await page.WaitForTimeoutAsync(1000);

            await page.SelectOptionAsync(
                "select[name='cveProceso']",
                "D");

            await page.WaitForTimeoutAsync(1000);

            await page.SelectOptionAsync(
                "select[name='cveProcImproc']",
                "T");



            await page.WaitForTimeoutAsync(1000);

            // =========================
// FECHAS
// =========================

await page.FillAsync(
    "input[name='fechaDesde']",
    fechaDesde);

await page.FillAsync(
    "input[name='fechaHasta']",
    fechaHasta);

await page.WaitForTimeoutAsync(1000);



            // =========================
            // CLICK PROCESA
            // =========================

            await page.ClickAsync("#procesa");

            // =========================
            // ESPERAR TABLA
            // =========================

            await page.WaitForTimeoutAsync(10000);

            // =========================
            // OBTENER HTML
            // =========================

            var html =
                await page.ContentAsync();

            File.WriteAllText(
                "debug.html",
                html);

            var doc =
                new HtmlDocument();

            doc.LoadHtml(html);

            // =========================
            // TABLA REAL
            // =========================

            var table =
                doc.DocumentNode.SelectSingleNode(
                    "//table[@id='TABLE_12']");

            if (table == null)
                return result;

            // =========================
            // HEADERS REALES
            // =========================

            var headerCells =
                table.SelectNodes(".//tr[2]/th");

            if (headerCells == null)
                return result;

            var headers =
    new List<string>();

// HEADERS FIJOS
headers.Add("SEC");
headers.Add("AREA");

// HEADERS DINÁMICOS
foreach (var header in headerCells)
{
    var text =
        header.InnerText.Trim();

    if (!string.IsNullOrWhiteSpace(text))
    {
        headers.Add(text);
    }
}

            // =========================
            // FILAS REALES
            // =========================

            var rows =
                table.SelectNodes(".//tr");

            if (rows == null)
                return result;

            foreach (var row in rows.Skip(2))
{
    var cells =
        row.SelectNodes("./td");

    if (cells == null)
        continue;

    if (cells.Count < 3)
        continue;

    var item =
        new Dictionary<string, string>();

    // =========================
    // SEC
    // =========================

    item["SEC"] =
        cells[0].InnerText.Trim();

    // =========================
    // AREA
    // =========================

    item["AREA"] =
        cells[1].InnerText.Trim();

    // =========================
    // COLUMNAS DINÁMICAS

for (int i = 2;
     i < cells.Count;
     i++)
{
    int headerIndex = i;

    if (headerIndex >= headers.Count)
        break;

    item[headers[headerIndex]] =
        cells[i].InnerText.Trim();
}

    result.Add(item);
foreach (var key in item.Keys)
{
    if (key == "SEC" || key == "AREA")
        continue;

    // =========================
    // VALIDAR DUPLICADO
    // =========================

    var exists =
        await _context.Inconformidades
            .AnyAsync(x =>

                x.FechaConsulta.Date ==
                    DateTime.Parse(fechaHasta).Date

                &&

                x.SEC == item["SEC"]

                &&

                x.AREA == item["AREA"]

                &&

                x.Codigo == key
            );

    // =========================
    // SOLO GUARDAR SI NO EXISTE
    // =========================

    if (!exists)
    {
        var inconformidad =
            new Inconformidad
            {
                FechaConsulta =
                    DateTime.Parse(fechaHasta),

                SEC = item["SEC"],

                AREA = item["AREA"],

                Codigo = key,

                Valor = item[key]
            };

        _context.Inconformidades.Add(
            inconformidad);
    }
}
}

await _context.SaveChangesAsync();
            return result;
        }

        public async Task<List<Dictionary<string, string>>>
GetComparisonData(
    string url,
    string fechaDesde,
    string fechaHasta)
{
    return await GetTableData(
        url,
        fechaDesde,
        fechaHasta);
}
    }

    
}