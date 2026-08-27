using Pgvector;

namespace SistemaPQRS.Api.Models;

/// <summary>
/// Artículo de la base de conocimiento que el RAG usa para responder pre-radicación.
/// </summary>
public class KbArticle
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Title { get; set; }
    public required string Content { get; set; }

    /// <summary>
    /// Embedding del contenido (pgvector), nullable por ahora.
    /// La DIMENSIÓN real se fija en la columna vector(n) al generar la migración
    /// (Fase 4) — junto con el índice HNSW (Bloque 3 del plan); no se configura
    /// índice en código todavía.
    /// OJO: verificar dimensión con NVIDIA NIM — nv-embedqa-e5-v5 es de 1024
    /// según sus docs (la spec mencionaba 1536).
    /// Tipo provisto por el paquete Pgvector (pgvector-dotnet).
    /// </summary>
    public Vector? Embedding { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public required Tenant Tenant { get; set; }
}
