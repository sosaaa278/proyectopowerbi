namespace DashboardAPI.Models
{
    public class Inconformidad
    {
        public int Id { get; set; }

        public DateTime FechaConsulta { get; set; }

        public string SEC { get; set; } = "";

        public string AREA { get; set; } = "";

        public string Codigo { get; set; } = "";

        public string Valor { get; set; } = "";
    }
}