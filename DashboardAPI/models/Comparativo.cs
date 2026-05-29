using System.Text.Json.Serialization;

namespace DashboardAPI.Models
{
    public class Comparativo
    {
        [JsonPropertyName("area")]
        public string AREA { get; set; } = "";

        [JsonPropertyName("total2025")]
        public double Total2025 { get; set; }

        [JsonPropertyName("total2026")]
        public double Total2026 { get; set; }

        [JsonPropertyName("variacion")]
        public double Variacion { get; set; }
    }
}