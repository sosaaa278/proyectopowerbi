using HtmlAgilityPack;

namespace DashboardAPI.Services
{
    public class WebScraperService
    {
        private readonly HttpClient _httpClient;

        public WebScraperService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<List<Dictionary<string, string>>> GetTableData(string url)
        {
            var html = await _httpClient.GetStringAsync(url);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var table = doc.DocumentNode.SelectSingleNode("//table");

            var result = new List<Dictionary<string, string>>();

            if (table == null)
                return result;

            var rows = table.SelectNodes(".//tr");

            if (rows == null)
                return result;

            var headers = rows[0]
                .SelectNodes(".//th|.//td")
                .Select(h => h.InnerText.Trim())
                .ToList();

            foreach (var row in rows.Skip(1))
            {
                var cells = row.SelectNodes(".//td");

                if (cells == null)
                    continue;

                var item = new Dictionary<string, string>();

                for (int i = 0; i < headers.Count && i < cells.Count; i++)
                {
                    item[headers[i]] = cells[i].InnerText.Trim();
                }

                result.Add(item);
            }

            return result;
        }
    }
}