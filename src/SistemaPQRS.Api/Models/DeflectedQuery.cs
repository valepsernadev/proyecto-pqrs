namespace SistemaPQRS.Api.Models;

/// <summary>
/// Consulta resuelta por RAG sin llegar a radicar ticket formal.
/// Métrica de ahorro operativo.
/// </summary>
public class DeflectedQuery
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string ClientQuery { get; set; }
    public required string RagResponse { get; set; }
    /// <summary>¿El cliente confirmó que la respuesta del RAG le resolvió?</summary>
    public bool UserConfirmed { get; set; }
    public DateTime CreatedAt { get; set; }

    public required Tenant Tenant { get; set; }
}
