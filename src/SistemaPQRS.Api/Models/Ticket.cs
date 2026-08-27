namespace SistemaPQRS.Api.Models;

/// <summary>
/// PQRS radicado, con clasificación IA (tipo, prioridad, sentimiento).
/// </summary>
public class Ticket
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>
    /// Formato: TCK-{yyyyMMdd}-{primeros 6 caracteres del Id en MAYÚSCULAS}.
    /// Se genera en la capa de aplicación a partir del propio Guid del ticket —
    /// NO usa secuencias de DB ni contadores: único por construcción,
    /// cero round-trips extra a la base.
    /// </summary>
    public required string TicketNumber { get; set; }

    // Datos de quien radicó (Cliente final, sin login — no hay User asociado)
    public required string ClientName { get; set; }
    public required string ClientEmail { get; set; }

    // Clasificación IA: Peticion|Queja|Reclamo|Sugerencia
    public required string Type { get; set; }
    // Clasificación IA: Alta|Media|Baja
    public required string Priority { get; set; }
    // Clasificación IA: Positivo|Neutral|Negativo
    public required string Sentiment { get; set; }
    /// <summary>Resumen de 1-2 líneas generado por IA.</summary>
    public required string Summary { get; set; }
    public required string Subject { get; set; }
    public required string Description { get; set; }

    /// <summary>
    /// Quién LO ATIENDE (el Agent/Admin que tomó ownership) — NO quién lo radicó.
    /// Nullable: al crearse el ticket todavía no está asignado a nadie.
    /// </summary>
    public Guid? AssignedToUserId { get; set; }

    // Status: Pendiente|En Proceso|Resuelto
    public required string Status { get; set; }

    /// <summary>¿Se resolvió en la fase RAG sin abrir ticket formal? (default: false)</summary>
    public bool WasResolvedByRag { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public required Tenant Tenant { get; set; }
    public User? AssignedToUser { get; set; }
}
