using DashboardAPI.Services;
using Microsoft.AspNetCore.Mvc;
using DashboardAPI.Data;
using DashboardAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace DashboardAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DataController : ControllerBase
    {
        private readonly AppDbContext _context;

        private readonly WebScraperService _scraper;

        public DataController(
            WebScraperService scraper,
            AppDbContext context)
        {
            _scraper = scraper;
            _context = context;
        }

        // =========================
        // SCRAPING PRINCIPAL
        // =========================

        [HttpGet]
      public async Task<IActionResult> Get(string url)
{
    var data =
        await _scraper.GetTableData(
            url,
            $"{DateTime.Now.Year}/01/01",
            DateTime.Now.ToString("yyyy/MM/dd")
        );

    return Ok(data);
}

        // =========================
        // COMPARATIVO TOTAL
        // =========================

        [HttpGet("compare")]
        public async Task<IActionResult> Compare()
        {
            var year2025 =
                await _context.Inconformidades
                    .Where(x =>
                        x.FechaConsulta.Year == 2025 &&
                        x.Codigo == "TOTAL")
                    .ToListAsync();

            var year2026 =
                await _context.Inconformidades
                    .Where(x =>
                        x.FechaConsulta.Year == 2026 &&
                        x.Codigo == "TOTAL")
                    .ToListAsync();

            var areas =
                year2026
                    .Select(x => x.AREA)
                    .Distinct();

            var result =
                new List<Comparativo>();

            foreach (var area in areas)
            {
                var total2025 =
                    year2025
                        .Where(x => x.AREA == area)
                        .Sum(x =>
                        {
                            double.TryParse(
                                x.Valor.Replace(",", ""),
                                out double val);

                            return val;
                        });

                var total2026 =
                    year2026
                        .Where(x => x.AREA == area)
                        .Sum(x =>
                        {
                            double.TryParse(
                                x.Valor.Replace(",", ""),
                                out double val);

                            return val;
                        });

                double variacion = 0;

                if (total2025 > 0)
                {
                    variacion =
                        ((total2026 - total2025)
                        / total2025) * 100;
                }

                result.Add(
                    new Comparativo
                    {
                        AREA = area,

                        Total2025 = total2025,

                        Total2026 = total2026,

                        Variacion =
                            Math.Round(
                                variacion,
                                2)
                    });
            }

            return Ok(result);
        }

        // =========================
        // COMPARAR UNA ZONA
        // CONTRA SI MISMA
        // =========================

        [HttpGet("compare/area/{area}")]
        public async Task<IActionResult>
        CompareArea(string area)
        {
            var year2025 =
                await _context.Inconformidades
                    .Where(x =>
                        x.FechaConsulta.Year == 2025 &&
                        x.AREA.Contains(area))
                    .ToListAsync();

            var year2026 =
                await _context.Inconformidades
                    .Where(x =>
                        x.FechaConsulta.Year == 2026 &&
                        x.AREA.Contains(area))
                    .ToListAsync();

            var codigos =
                year2026
                    .Select(x => x.Codigo)
                    .Distinct();

            var result =
                new List<object>();

            foreach (var codigo in codigos)
            {
                var total2025 =
                    year2025
                        .Where(x => x.Codigo == codigo)
                        .Sum(x =>
                        {
                            double.TryParse(
                                x.Valor.Replace(",", ""),
                                out double val);

                            return val;
                        });

                var total2026 =
                    year2026
                        .Where(x => x.Codigo == codigo)
                        .Sum(x =>
                        {
                            double.TryParse(
                                x.Valor.Replace(",", ""),
                                out double val);

                            return val;
                        });

                double variacion = 0;

                if (total2025 > 0)
                {
                    variacion =
                        ((total2026 - total2025)
                        / total2025) * 100;
                }

                result.Add(new
                {
                    Codigo = codigo,

                    Total2025 = total2025,

                    Total2026 = total2026,

                    Variacion =
                        Math.Round(
                            variacion,
                            2)
                });
            }

            return Ok(result);
        }

        // =========================
        // COMPARAR POR CÓDIGO
        // EJEMPLO:
        // /compare/E02
        // =========================

       [HttpGet("compare/{codigo}")]
public async Task<IActionResult>
CompareByCode(string codigo)
{
    // =========================
    // FECHAS DINÁMICAS
    // =========================

    var today =
        DateTime.Now;

    // =========================
    // RANGO 2025
    // =========================

    var desde2025 =
        "2025/01/01";

    var hasta2025 =
        $"2025/{today.Month:D2}/{today.Day:D2}";

    // =========================
    // RANGO 2026
    // =========================

    var desde2026 =
        "2026/01/01";

    var hasta2026 =
        $"2026/{today.Month:D2}/{today.Day:D2}";

    // =========================
    // URL CFE
    // =========================

    var url =
        "https://cssnal.cfe.mx/Inconformidades/solTermino.asp";

    // =========================
    // SCRAPING 2025
    // =========================

    var data2025 =
        await _scraper.GetComparisonData(
            url,
            desde2025,
            hasta2025);

await Task.Delay(3000);
    // =========================
    // SCRAPING 2026
    // =========================

    var data2026 =
        await _scraper.GetComparisonData(
            url,
            desde2026,
            hasta2026);

    // =========================
    // RESULTADO
    // =========================

    var result =
        new List<Comparativo>();

    foreach (var row2026 in data2026)
    {
        var area =
            row2026["AREA"];

        var row2025 =
            data2025
                .FirstOrDefault(x =>
                    x["AREA"] == area);

        double total2025 = 0;

        double total2026 = 0;

        if (row2025 != null)
        {
            double.TryParse(
                row2025[codigo]
                    .Replace(",", ""),

                out total2025);
        }

        double.TryParse(
            row2026[codigo]
                .Replace(",", ""),

            out total2026);

        double variacion = 0;

        if (total2025 > 0)
        {
            variacion =
                ((total2026 - total2025)
                / total2025) * 100;
        }

        result.Add(
            new Comparativo
            {
                AREA = area,

                Total2025 =
                    total2025,

                Total2026 =
                    total2026,

                Variacion =
                    Math.Round(
                        variacion,
                        2)
            });
    }

    return Ok(result);
}

[HttpGet("snapshot")]
public async Task<IActionResult>
Snapshot()
{
    await _scraper.GetTableData(
        "https://cssnal.cfe.mx/Inconformidades/solTermino.asp",
        $"{DateTime.Now.Year}/01/01",
        DateTime.Now.ToString("yyyy/MM/dd")
    );

    return Ok();
}
    }

    
}

