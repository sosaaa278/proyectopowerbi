using DashboardAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace DashboardAPI.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Inconformidad> Inconformidades { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Inconformidad>(entity =>
            {
                // Composite index for the date-range + Codigo queries in Compare()
                entity.HasIndex(x => new { x.FechaConsulta, x.Codigo })
                      .HasDatabaseName("IX_Inconformidades_Fecha_Codigo");

                // Composite index for duplicate detection in PersistRowsAsync()
                entity.HasIndex(x => new { x.FechaConsulta, x.SEC, x.AREA, x.Codigo })
                      .IsUnique()
                      .HasDatabaseName("UX_Inconformidades_Key");
            });
        }
    }
}
