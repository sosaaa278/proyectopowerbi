using DashboardAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace DashboardAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DataController : ControllerBase
    {
        private readonly WebScraperService _scraper;

        public DataController(WebScraperService scraper)
        {
            _scraper = scraper;
        }

        [HttpGet]
        public async Task<IActionResult> Get(string url)
        {
            var data = await _scraper.GetTableData(url);

            return Ok(data);
        }
    }
}