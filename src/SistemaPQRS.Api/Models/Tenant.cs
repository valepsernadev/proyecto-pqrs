namespace SistemaPQRS.Api.Models;

/// <summary>
/// Tenant: aislamiento multi-tenant. Toda la data se filtra por TenantId.
/// </summary>
public class Tenant
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Domain { get; set; }
    public required string ApiKey { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navegación: hijos del tenant
    public ICollection<User> Users { get; set; } = [];
    public ICollection<KbArticle> KbArticles { get; set; } = [];
    public ICollection<Ticket> Tickets { get; set; } = [];
    public ICollection<DeflectedQuery> DeflectedQueries { get; set; } = [];
}
