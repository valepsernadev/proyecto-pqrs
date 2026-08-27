# Guía de Módulos y Fases — Sistema PQRS

> Documento de seguimiento para seguir el proceso de construcción de cerca.
> Fuente de verdad técnica: [`plan-proyecto-pqrs.md`](plan-proyecto-pqrs.md).
> Última actualización: 27/08/2026 — Bloque 2, Fase 1 completada.

**Leyenda de estado:** ✅ hecho · 🚧 en curso · ⏳ pendiente · ⚠️ requiere atención antes de avanzar

---

## 1. Estado actual

| Bloque | Fase | Estado | Notas |
|---|---|---|---|
| 1 — Setup + DB + Docker | todo | ✅ (falta validar build) | Compose, Dockerfile, limpieza `Program.cs`. El **primer `docker build` real es después del Bloque 2** (checkpoint del plan). |
| 2 — Modelo + Tenancy | F1 Entidades + DbContext | ✅ | Hoy: 5 entidades + `AppDbContext` configurado + paquetes pgvector |
| 2 | F2 Índices | ✅ (parcial) | B-Tree listos en F1; HNSW queda para Bloque 3 (comentado en código) |
| 2 | F3–F6 | ⏳ | Tenancy dual, migración, `/seed`, checkpoint de aislamiento |
| 3–9 | — | ⏳ | Ver módulos abajo |

---

## 2. Mapa del proyecto (cómo está organizado hoy)

```
sistema-pqrs/
├── plan-proyecto-pqrs.md        ← fuente de verdad (prompts, decisiones)
├── guia-modulos-pqrs.md         ← este archivo
├── docker-compose.yml           ← servicios db (pgvector) + backend
├── SistemaPQRS.slnx             ← solución (un solo proyecto: monolito)
├── widget/                      ← vacía (Bloque 6)
├── frontend-admin/              ← vacía (Bloque 7)
└── src/SistemaPQRS.Api/
    ├── Program.cs               ← bootstrap: controllers, EF Core, OpenAPI
    ├── Models/                  ← 5 entidades (Tenant, User, KbArticle, DeflectedQuery, Ticket)
    ├── Data/                    ← AppDbContext (config FKs, índices, pgvector)
    ├── Services/  Repositories/  DTOs/   ← vacías (Bloques 3-7)
    └── Dockerfile               ← multi-stage (sdk → aspnet)
```

---

## 3. Los módulos

### Módulo 1 — Setup + DB + esqueleto Docker ✅

**En qué consta:** la infraestructura base: base de datos PostgreSQL con pgvector corriendo en Docker, un Dockerfile multi-stage para el backend, y el servicio `backend` declarado en el compose listo para levantar.

**Qué lleva:**
- `docker-compose.yml`: servicio `db` (`pgvector/pgvector:pg16` — postgres 16 con la extensión pgvector preinstalada, necesaria para los embeddings), puerto `5432:5432`, volumen nombrado `pgdata` (los datos sobreviven reinicios), healthcheck con `pg_isready`.
- Servicio `backend`: construye desde el Dockerfile, arranca **solo cuando la DB está healthy** (`depends_on: condition: service_healthy`), y recibe la connection string con `Host=db` (dentro de la red de Docker los servicios se resuelven por nombre, no por localhost).
- `Dockerfile` multi-stage: SDK `10.0` (restaura + publica) → runtime `aspnet:10.0` (imagen final chica).
- Limpieza de `Program.cs`: fuera `UseHttpsRedirection()` y el endpoint `/weatherforecast` de ejemplo (HTTP puro en dev, decisión del plan §6.1).

**Fases:** hecho completo, salvo la **validación real** — el `docker build` del backend se hace recién al cerrar el Bloque 2 (checkpoint del plan; no tiene sentido validar una imagen de un código sin entidades).

### Módulo 2 — Modelo de datos + Tenancy dual 🚧 (F1 lista)

**En qué consta:** el corazón del sistema: las 5 entidades, la base de datos real (migración), y el **aislamiento multi-tenant** — la garantía de que ningún tenant ve datos de otro.

**Fases:**

| Fase | Qué se hace | Estado |
|---|---|---|
| **F1. Entidades + DbContext** | 5 entidades en `Models/` (una por archivo) con sus relaciones y el `AppDbContext` completo en `Data/`: FKs con cascada, índices B-Tree `(TenantId, Status)` y `(TenantId, Priority)` sobre Ticket, `TicketNumber` único (máx. 50), `Email` máx. 255, `ApiKey` única, `WasResolvedByRag` default false, Ticket→User con `SetNull` (si se borra el agent, el ticket se desasigna, no se borra). Paquete pgvector integrado (ver ⚠️ abajo). | ✅ |
| **F2. Índices** | B-Tree ya configurados en F1. El **índice HNSW** sobre `KbArticle.Embedding` se agrega en el Bloque 3 (está comentado en código). | ✅ (parcial) |
| **F3. Tenancy dual** | Dos providers: **Header** (`X-Tenant-Id` — lo usa el Widget público) y **JWT** (claim `tenantId` — Agents/Admins autenticados). Middleware que resuelve `ITenantContext.TenantId` según cuál esté disponible + guard (sin tenant → 401/403). | ⏳ |
| **F4. Migración EF** | `dotnet ef migrations add InitialCreate` + `dotnet ef database update` → validar tablas en Postgres. | ⏳ |
| **F5. Endpoint `/seed`** | Solo en desarrollo: crea 2 tenants de prueba (Tenant-A/B), 1 Admin + 1 Agent por tenant, 2-3 KbArticles por tenant. **No** se usa `HasData()` en migraciones (decisión del plan). | ⏳ |
| **F6. Checkpoint aislamiento** | Con `curl`: `X-Tenant-Id: tenant-a` → solo datos de A; `tenant-b` → solo de B; tenant inexistente → 403/400. **Se valida antes de tocar IA.** | ⏳ |

**⚠️ Pendientes antes de F4:**
1. **Dimensión del embedding:** la spec decía 1536, pero `nv-embedqa-e5-v5` (modelo del plan) documenta **1024**. La dimensión se fija en la columna `vector(n)` al migrar — hay que confirmarla antes (en el Bloque 3 se prueba con `curl` y ahí se ve).
2. **Compatibilidad pgvector:** los paquetes son `Pgvector` + `Pgvector.EntityFrameworkCore` (no `Npgsql.PgVector`, que no existe). El README dice que 0.3.0 soporta EF Core 9 y 10; fue compilado contra Npgsql 8, y NuGet lo unificó a Npgsql 10.0.3. Si la migración diera un error de runtime, se resuelve acá.

### Módulo 3 — Servicios de IA base ⏳

**En qué consta:** los dos clientes HTTP que todo lo demás usa: **DeepSeek** para chat (RAG synthesis + triage) y **NVIDIA NIM** para embeddings (solo eso). Dos keys, dos clientes, dos configs — aislados a propósito (el plan justifica separar la cuota de pruebas de la de la app).

**Qué lleva:** contratos `IEmbeddingService` y `IChatCompletionService` (interfaces chicas, SOLID), implementaciones por proveedor, options en `appsettings` (keys separadas), endpoints probados con `curl` **antes** de integrar al código.

**Fases:** 1) probar ambos endpoints con `curl` → 2) contratos + implementaciones + DI → 3) registrar keys en appsettings.

### Módulo 4 — RAG pre-radicación ⏳

**En qué consta:** el chat inteligente del widget. `rag-search`: embebe la pregunta del cliente → busca los chunks más similares en la KB del tenant (coseno, umbral **0.75**) → el prompt de síntesis (exacto, sección 5.1 del plan) arma la respuesta. `rag-feedback`: el cliente responde "¿te resolvió?" → se registra un `DeflectedQuery` (la métrica de ahorro operativo). Si el RAG no resuelve → se invita a radicar ticket.

**Qué lleva:** servicios RAG, la entidad `DeflectedQuery` (ya existe), y en la migración de este bloque: la **columna `vector(n)` con la dimensión real + el índice HNSW** sobre `Embedding`.

**Fases:** 1) servicio de embeddings conectado a la KB → 2) `rag-search` → 3) `rag-feedback` + entidad → 4) HNSW en migración.

### Módulo 5 — Ticket + Triage + SignalR ⏳

**En qué consta:** la radicación formal. Se crea el ticket con `TicketNumber = TCK-{yyyyMMdd}-{primeros 6 chars del Guid en mayúsculas}` (derivado del Guid — único por construcción, sin secuencias de DB ni contadores), y la IA lo clasifica con el prompt de triage (tipo Peticion/Queja/Reclamo/Sugerencia, prioridad, sentimiento, resumen). Si `Priority == Alta` o `Sentiment == Negativo`, se emite **un evento SignalR al grupo del tenant** para avisar a los agents en vivo.

**Qué lleva:** servicio de tickets + triage, Hub de SignalR, y la **prueba obligatoria** del plan: una página HTML mínima (`/tools/signalr-test.html` con `@microsoft/signalr` por CDN) que se une al grupo del tenant y muestra el evento en pantalla — no se acepta "compila" como validación.

**Fases:** 1) creación + triage → 2) Hub + evento en el mismo flujo → 3) prueba con cliente real → 4) **checkpoint: `docker build` #2**.

### Módulo 6 — Widget JS ⏳

**En qué consta:** el script que el cliente final ve en el sitio del tenant. Integración de **una línea** (`<script src="...widget.js" data-tenant="...">`): **Shadow DOM completo** (los estilos del widget no se fugan al sitio ni viceversa), **IIFE sin variables globales** (no contamina el `window` del anfitrión), **`try/catch` en cada `fetch`** (si la API falla, el widget degrada silenciosamente sin romper el sitio).

**Qué lleva:** el chat con RAG, el botón "¿te resolvió?", el formulario de radicación (nombre, email, asunto, descripción) y la confirmación con el número de ticket.

### Módulo 7 — JWT + CRUD usuarios ⏳

**En qué consta:** la autenticación y permisos. Login con JWT (el token lleva el claim `tenantId` — de ahí sale el aislamiento del lado autenticado). Endpoints protegidos: `kb-articles` (solo **Admin** crea/edita; **Agent** consulta), tickets protegidos, gestión de `User` restringida por `Role` (solo Admin crea/elimina usuarios).

**Qué lleva:** auth JWT, políticas por rol, los controllers de gestión, y el `frontend-admin/` empieza a tener sentido.

### Módulo 8 — CORS dinámico + aislamiento cruzado ⏳

**En qué consta:** el CORS que permite a cada tenant su sitio (se permite el dominio registrado en `Tenant.Domain`), y la **prueba completa de aislamiento** con 2 tenants (repite y profundiza el checkpoint del módulo 2, ahora con JWT de por medio: admin de Tenant-A intentando leer datos de Tenant-B → debe fallar).

### Módulo 9 — Validación Docker E2E + README + pulido SOLID ⏳

**En qué consta:** el cierre: `docker compose up` completo funcionando end-to-end, README con el diagrama del flujo (`Ticket → TriageService → IChatCompletionService → SignalR Hub → Clients.Group(tenantId)`) y la justificación de decisiones (índices, particionamiento futuro por `TenantId`, SOLID).

---

## 4. Decisiones ya cerradas (para entender el porqué)

- **Monolito:** un solo proyecto (`SistemaPQRS.Api`). No se separan proyectos Domain/Infrastructure (decisión del dueño del proyecto, 27/08).
- **3 actores:** Cliente final (sin login, solo widget) · Agent (ve/atiende tickets de su tenant) · Admin (además: KB, usuarios, config). No hay SuperAdmin en el MVP.
- **Aislamiento:** un solo Postgres, todo filtrado por `TenantId`.
- **`AssignedToUserId`** = quién **atiende** el ticket (nullable al crearse). Quién radicó no existe como User — son `ClientName`/`ClientEmail`.
- **Seed por endpoint `/seed`**, nunca `HasData()` en migraciones.
- **HTTP puro en desarrollo** — sin HTTPS hasta producción.
- **Chat → DeepSeek** (`deepseek-chat`) · **Embeddings → NVIDIA NIM** (`nv-embedqa-e5-v5`), keys separadas.
- **Umbral RAG:** 0.75 coseno.
- **pgvector:** paquetes `Pgvector` + `Pgvector.EntityFrameworkCore` (el tipo es `Vector`, namespace `Pgvector`; se registra con `UseVector()` en Program.cs y `HasPostgresExtension("vector")` en el DbContext).

## 5. Checklist de validaciones del plan

- [x] Bloque 1: compose + Dockerfile + DB healthy (falta `docker build` real)
- [ ] Bloque 2 F6: aislamiento cruzado con 2 tenants (recién al terminar tenancy)
- [ ] Bloque 5: `docker build` del backend
- [ ] Bloque 8: prueba completa de aislamiento cruzado (CORS incluido)
- [ ] Bloque 9: `docker-compose up` end-to-end completo
