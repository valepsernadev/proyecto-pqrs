namespace SistemaPQRS.Api.Models;

/// <summary>
/// Usuario autenticado (Admin o Agent). El Cliente final NO tiene fila acá:
/// accede solo por el Widget, sin login.
/// </summary>
public class User
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    /// <summary>Valores posibles: "Admin" | "Agent" (validado en capa de aplicación).</summary>
    public required string Role { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public required Tenant Tenant { get; set; }
    /// <summary>Tickets que este user ATIENDE (Ticket.AssignedToUserId → User).</summary>
    public ICollection<Ticket> AssignedTickets { get; set; } = [];
}
