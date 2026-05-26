namespace DashboardAPI.Models
{
    public class FullCompareResponse
    {
        public List<Dictionary<string, string>> RawData2026 { get; set; } = new();

        public Dictionary<string, List<Comparativo>> Compare { get; set; } = new();
    }
}
