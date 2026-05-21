using System.IO;
using HtmlAgilityPack;
using Microsoft.Playwright;

namespace DashboardAPI.Services
{
    public class WebScraperService
    {
      public async Task<List<Dictionary<string, string>>> GetTableData(string url)
{
    var result = new List<Dictionary<string, string>>();

    using var playwright = await Playwright.CreateAsync();

    var userDataDir =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "PlaywrightProfile");

    var context =
        await playwright.Chromium.LaunchPersistentContextAsync(
            userDataDir,
            new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = false,
                Channel = "msedge"
            });

    var page =
        context.Pages.FirstOrDefault()
        ?? await context.NewPageAsync();

    await page.GotoAsync(
        url,
        new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 120000
        });

    // =========================
    // FILTROS AUTOMÁTICOS
    // =========================

    await page.SelectOptionAsync(
        "select[name='cveDivision']",
        "DC000");

    await page.SelectOptionAsync(
        "select[name='cveZona']",
        "00000");

    await page.SelectOptionAsync(
        "select[name='cveArea']",
        "00000");

    await page.SelectOptionAsync(
        "select[name='cveProceso']",
        "D");

    await page.SelectOptionAsync(
        "select[name='cveProcImproc']",
        "T");

    // =========================
    // CLICK PROCESA
    // =========================

    await page.ClickAsync("#procesa");

    // =========================
    // ESPERAR TABLA FINAL
    // =========================

    await page.WaitForSelectorAsync(
        "#TABLE_12",
        new PageWaitForSelectorOptions
        {
            Timeout = 120000
        });

    await page.WaitForTimeoutAsync(5000);

    var html = await page.ContentAsync();

    var doc = new HtmlDocument();

    doc.LoadHtml(html);

    var table =
        doc.DocumentNode.SelectSingleNode(
            "//table[@id='TABLE_12']");

    if (table == null)
        return result;

    var rows =
        table.SelectNodes(".//tbody/tr");

    if (rows == null)
        return result;

    var headers =
        table.SelectNodes(".//thead/tr[2]/th")
        .Select(h => h.InnerText.Trim())
        .ToList();

    headers.Insert(0, "AREA");

    foreach (var row in rows)
    {
        var cells = row.SelectNodes("./td");

        if (cells == null || cells.Count < 2)
            continue;

        var item =
            new Dictionary<string, string>();

        item["AREA"] =
            cells[1].InnerText.Trim();

        for (int i = 2;
             i < cells.Count && i - 2 < headers.Count - 1;
             i++)
        {
            item[headers[i - 1]] =
                cells[i].InnerText.Trim();
        }

        result.Add(item);
    }

    return result;
}
    }
}