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

        private readonly FullCompareService _fullCompare;

        public DataController(
            WebScraperService scraper,
            AppDbContext context,
            FullCompareService fullCompare)
        {
            _scraper = scraper;
            _context = context;
            _fullCompare = fullCompare;
        }

        // =========================
        // SCRAPING PRINCIPAL
        // =========================

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            try
            {
                var data =
                    await _scraper.GetTableData(
                        "https://cssnal.cfe.mx/Inconformidades/solTermino.asp",

                        $"{DateTime.Now.Year}/01/01",

                        DateTime.Now.ToString("yyyy/MM/dd")
                    );

                return Ok(data);
            }
            catch (Exception ex)
            {
                return BadRequest(
                    $"Error scraping data: {ex.Message}");
            }
        }

        // =========================
        // COMPARATIVO TOTAL
        // =========================

        [HttpGet("compare")]
        public async Task<IActionResult> Compare()
        {
            try
            {
                var currentYear =
                    DateTime.Now.Year;

                var previousYear =
                    currentYear - 1;

                var yearPrevious =
                    await _context.Inconformidades
                        .Where(x =>
                            x.FechaConsulta.Year == previousYear &&
                            x.Codigo == "TOTAL")
                        .ToListAsync();

                var yearCurrent =
                    await _context.Inconformidades
                        .Where(x =>
                            x.FechaConsulta.Year == currentYear &&
                            x.Codigo == "TOTAL")
                        .ToListAsync();

                var areas =
                    yearCurrent
                        .Select(x => x.AREA)
                        .Distinct();

                var result =
                    new List<Comparativo>();

                foreach (var area in areas)
                {
                    var totalPrevious =
                        yearPrevious
                            .Where(x => x.AREA == area)
                            .Sum(x =>
                            {
                                double.TryParse(
                                    x.Valor.Replace(",", ""),
                                    out double val);

                                return val;
                            });

                    var totalCurrent =
                        yearCurrent
                            .Where(x => x.AREA == area)
                            .Sum(x =>
                            {
                                double.TryParse(
                                    x.Valor.Replace(",", ""),
                                    out double val);

                                return val;
                            });

                    double variacion = 0;

                    if (totalPrevious > 0)
                    {
                        variacion =
                            ((totalCurrent - totalPrevious)
                            / totalPrevious) * 100;
                    }

                    result.Add(
                        new Comparativo
                        {
                            AREA = area,

                            Total2025 = totalPrevious,

                            Total2026 = totalCurrent,

                            Variacion =
                                Math.Round(
                                    variacion,
                                    2)
                        });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(
                    $"Error comparing data: {ex.Message}");
            }
        }

        // =========================
        // COMPARAR UNA ZONA
        // =========================

        [HttpGet("compare/area/{area}")]
        public async Task<IActionResult>
        CompareArea(string area)
        {
            try
            {
                var currentYear =
                    DateTime.Now.Year;

                var previousYear =
                    currentYear - 1;

                var yearPrevious =
                    await _context.Inconformidades
                        .Where(x =>
                            x.FechaConsulta.Year == previousYear &&
                            x.AREA.Contains(area))
                        .ToListAsync();

                var yearCurrent =
                    await _context.Inconformidades
                        .Where(x =>
                            x.FechaConsulta.Year == currentYear &&
                            x.AREA.Contains(area))
                        .ToListAsync();

                var codigos =
                    yearCurrent
                        .Select(x => x.Codigo)
                        .Distinct();

                var result =
                    new List<object>();

                foreach (var codigo in codigos)
                {
                    var totalPrevious =
                        yearPrevious
                            .Where(x => x.Codigo == codigo)
                            .Sum(x =>
                            {
                                double.TryParse(
                                    x.Valor.Replace(",", ""),
                                    out double val);

                                return val;
                            });

                    var totalCurrent =
                        yearCurrent
                            .Where(x => x.Codigo == codigo)
                            .Sum(x =>
                            {
                                double.TryParse(
                                    x.Valor.Replace(",", ""),
                                    out double val);

                                return val;
                            });

                    double variacion = 0;

                    if (totalPrevious > 0)
                    {
                        variacion =
                            ((totalCurrent - totalPrevious)
                            / totalPrevious) * 100;
                    }

                    result.Add(new
                    {
                        Codigo = codigo,

                        Total2025 = totalPrevious,

                        Total2026 = totalCurrent,

                        Variacion =
                            Math.Round(
                                variacion,
                                2)
                    });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(
                    $"Error comparing area: {ex.Message}");
            }
        }

        // =========================
        // COMPARATIVO EN VIVO
        // =========================

        [HttpGet("compare/{codigo}")]
        public async Task<IActionResult>
        CompareByCode(string codigo)
        {
            try
            {
                var today =
                    DateTime.Now;

                var currentYear =
                    today.Year;

                var previousYear =
                    currentYear - 1;

                // =========================
                // FECHAS DINÁMICAS
                // =========================

                var desdePrevious =
                    $"{previousYear}/01/01";

                var hastaPrevious =
                    $"{previousYear}/{today.Month:D2}/{today.Day:D2}";

                var desdeCurrent =
                    $"{currentYear}/01/01";

                var hastaCurrent =
                    $"{currentYear}/{today.Month:D2}/{today.Day:D2}";

                var url =
                    "https://cssnal.cfe.mx/Inconformidades/solTermino.asp";

                // =========================
                // SCRAPING AÑO ANTERIOR
                // =========================

                var dataPrevious =
                    await _scraper.GetComparisonData(
                        url,
                        desdePrevious,
                        hastaPrevious);

                await Task.Delay(3000);

                // =========================
                // SCRAPING AÑO ACTUAL
                // =========================

                var dataCurrent =
                    await _scraper.GetComparisonData(
                        url,
                        desdeCurrent,
                        hastaCurrent);

                var result =
                    new List<Comparativo>();

                foreach (var rowCurrent in dataCurrent)
                {
                    var area =
                        rowCurrent["AREA"];

                    var rowPrevious =
                        dataPrevious
                            .FirstOrDefault(x =>
                                x["AREA"] == area);

                    double totalPrevious = 0;

                    double totalCurrent = 0;

                    if (rowPrevious != null &&
                        rowPrevious.ContainsKey(codigo))
                    {
                        double.TryParse(
                            rowPrevious[codigo]
                                .Replace(",", ""),

                            out totalPrevious);
                    }

                    if (rowCurrent.ContainsKey(codigo))
                    {
                        double.TryParse(
                            rowCurrent[codigo]
                                .Replace(",", ""),

                            out totalCurrent);
                    }

                    double variacion = 0;

                    if (totalPrevious > 0)
                    {
                        variacion =
                            ((totalCurrent - totalPrevious)
                            / totalPrevious) * 100;
                    }

                    result.Add(
                        new Comparativo
                        {
                            AREA = area,

                            Total2025 = totalPrevious,

                            Total2026 = totalCurrent,

                            Variacion =
                                Math.Round(
                                    variacion,
                                    2)
                        });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(
                    $"Error comparing live data: {ex.Message}");
            }
        }

        // =========================
        // COMPARATIVO COMPLETO (CACHÉ)
        // =========================

        [HttpGet("fullcompare")]
        public async Task<IActionResult> FullCompare()
        {
            try
            {
                var data =
                    await _fullCompare.GetFullCompareAsync();

                return Ok(data);
            }
            catch (Exception ex)
            {
                return BadRequest(
                    $"Error in full compare: {ex.Message}");
            }
        }

        [HttpPost("fullcompare/refresh")]
        public IActionResult FullCompareRefresh()
        {
            _fullCompare.InvalidateCache();
            return Ok("Cache invalidado. El próximo GET re-scrapeará.");
        }

        // =========================
        // SNAPSHOT MANUAL
        // =========================

        [HttpGet("snapshot")]
        public async Task<IActionResult>
        Snapshot()
        {
            try
            {
                await _scraper.GetTableData(
                    "https://cssnal.cfe.mx/Inconformidades/solTermino.asp",

                    $"{DateTime.Now.Year}/01/01",

                    DateTime.Now.ToString("yyyy/MM/dd")
                );

                return Ok(
                    "Snapshot completed successfully");
            }
            catch (Exception ex)
            {
                return BadRequest(
                    $"Snapshot error: {ex.Message}");
            }
        }
    }
}