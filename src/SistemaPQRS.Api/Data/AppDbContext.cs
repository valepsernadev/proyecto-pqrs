using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using SistemaPQRS.Api.Models;

namespace SistemaPQRS.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<KbArticle> KbArticles => Set<KbArticle>();
    public DbSet<DeflectedQuery> DeflectedQueries => Set<DeflectedQuery>();
    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Extensión pgvector: se incluye en la migración (Fase 4) como
        // CREATE EXTENSION IF NOT EXISTS vector
        modelBuilder.HasPostgresExtension("vector");

        // ---------- Tenant ----------
        modelBuilder.Entity<Tenant>(e =>
        {
            // ApiKey única: la llave pública que usa el widget para identificar el tenant
            e.HasIndex(t => t.ApiKey).IsUnique();
        });

        // ---------- User ----------
        modelBuilder.Entity<User>(e =>
        {
            e.Property(u => u.Email).HasMaxLength(255);

            e.HasOne(u => u.Tenant)
             .WithMany(t => t.Users)
             .HasForeignKey(u => u.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- KbArticle ----------
        modelBuilder.Entity<KbArticle>(e =>
        {
            e.HasOne(a => a.Tenant)
             .WithMany(t => t.KbArticles)
             .HasForeignKey(a => a.TenantId)
             .OnDelete(DeleteBehavior.Cascade);

            // Embedding (pgvector): sin índice en código por ahora.
            // El índice HNSW y la dimensión vector(n) de la columna se definen
            // en la migración del Bloque 3 / Fase 4 (ver KbArticle.Embedding).
        });

        // ---------- DeflectedQuery ----------
        modelBuilder.Entity<DeflectedQuery>(e =>
        {
            e.HasOne(d => d.Tenant)
             .WithMany(t => t.DeflectedQueries)
             .HasForeignKey(d => d.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- Ticket ----------
        modelBuilder.Entity<Ticket>(e =>
        {
            e.Property(t => t.TicketNumber).HasMaxLength(50);
            e.HasIndex(t => t.TicketNumber).IsUnique();

            // Índices B-Tree del plan: cubren los filtros más frecuentes del panel de agents
            e.HasIndex(t => new { t.TenantId, t.Status });
            e.HasIndex(t => new { t.TenantId, t.Priority });

            e.Property(t => t.WasResolvedByRag).HasDefaultValue(false);

            e.HasOne(t => t.Tenant)
             .WithMany(ten => ten.Tickets)
             .HasForeignKey(t => t.TenantId)
             .OnDelete(DeleteBehavior.Cascade);

            // Si se elimina el user asignado, el ticket NO se borra: solo se desasigna
            e.HasOne(t => t.AssignedToUser)
             .WithMany(u => u.AssignedTickets)
             .HasForeignKey(t => t.AssignedToUserId)
             .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
