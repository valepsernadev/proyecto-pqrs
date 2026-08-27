# Plan de Proyecto — Sistema de PQRS Multi-Tenant con IA

> Versión actualizada con las decisiones confirmadas: proveedores DeepSeek + NVIDIA NIM, modelo de actores/roles (Cliente final / Agent / Admin), orden de bloques reordenado, SignalR fusionado en el Bloque 5 (con prueba obligatoria desde un cliente real), Docker como hilo continuo desde el Bloque 1, y las correcciones de entidades (`User` con `Role`) y `TicketNumber`.

---

## 1. Proveedores de IA (decisión cerrada)

**Decisión cerrada:** DeepSeek será utilizado para Chat Completions (RAG synthesis y Triage), mientras NVIDIA NIM será utilizado exclusivamente para embeddings.

| Servicio | Proveedor | Endpoint | Modelo | Notas |
|---|---|---|---|---|
| Chat completions (RAG synthesis + Triage) | DeepSeek | `api.deepseek.com/v1` (compatible OpenAI) | `deepseek-chat` | Se usa la key que ya tenés lista para codear |
| Embeddings (exclusivo) | NVIDIA NIM | `integrate.api.nvidia.com/v1` | `nvidia/nv-embedqa-e5-v5` | **Key separada** de la que usás para codear — mismo proveedor, distinta cuenta/API key para aislar cuota de pruebas de la app |

**Justificación de aislamiento de cuota:** aunque ambas keys sean de la misma familia de proveedores, se usan dos `HttpClient` distintos con dos `Options` distintos (`IEmbeddingService` / `IChatCompletionService` ya están separados por contrato), así que el aislamiento de cuota no cambia nada del diseño — es solo cuestión de qué valor va en cada `appsettings`/variable de entorno.

---

## 2. Actores y roles

La plataforma tiene un único sistema multi-tenant con una sola base de datos PostgreSQL. Los datos se aíslan mediante `TenantId`.

Existen 3 actores conceptuales:

1. **Cliente final**
   - No tiene login ni rol.
   - Interactúa con el sistema mediante el Widget.
   - Puede consultar la base de conocimiento mediante RAG.
   - Puede crear una PQRS cuando el RAG no resuelve su inquietud.

2. **Agent**
   - Usuario autenticado mediante JWT.
   - Pertenece a un único Tenant.
   - Puede consultar y gestionar tickets de su Tenant.

3. **Admin**
   - Usuario autenticado mediante JWT.
   - Pertenece a un único Tenant.
   - Tiene las capacidades del Agent.
   - Además puede administrar usuarios, artículos de conocimiento y configuración del Tenant.

El Widget **no es un rol**: es simplemente el canal/interfaz mediante el cual el Cliente final interactúa con la plataforma.

No se implementará un rol `SuperAdmin` en el MVP.

---

## 2.1 Flujo de Onboarding y Permisos

### Registro e Inicialización (Admin)

```
PASO 1: EMPRESA SE REGISTRA
┌─────────────────────────────────────────────────────────┐
│ Admin de la empresa llena un formulario en la plataforma:|
│ - Nombre de la empresa                                  │
│ - Dominio permitido (p.ej., www.empresa.com)           │
│ - Email del admin                                       │
│ - Contraseña                                            │
│                                                          │
│ Backend crea:                                           │
│ - Un registro Tenant (aislamiento multi-tenant)        │
│ - Un User con Role="Admin" (el propietario)            │
│ - Una API Key / Token para el Widget                   │
└─────────────────────────────────────────────────────────┘

PASO 2: ADMIN CONFIGURA LA BASE DE CONOCIMIENTO
┌─────────────────────────────────────────────────────────┐
│ Admin hace login con JWT token                         │
│ → Va a "Gestión de Artículos" (solo disponible para   │
│   Admin)                                                │
│ → Crea artículos de la KB que el RAG usará:          │
│   - "¿Cómo cambio mi contraseña?"                      │
│   - "¿Qué métodos de pago aceptan?"                    │
│   - "¿Cuál es la política de devoluciones?"            │
│                                                         │
│ Cada artículo KbArticle se guarda con:                │
│ - Title, Content                                       │
│ - TenantId (aislado a su empresa)                     │
│ - Embedding (generado automáticamente por backend)    │
└─────────────────────────────────────────────────────────┘

PASO 3 (OPCIONAL): ADMIN INVITA AGENTS
┌─────────────────────────────────────────────────────────┐
│ Admin → "Gestión de Usuarios" (solo Admin)            │
│ → Crea nuevos Users con Role="Agent"                  │
│ → Les asigna credenciales para login                  │
│                                                         │
│ Agents pueden:                                          │
│ ✅ Ver tickets del tenant                              │
│ ✅ Filtrar, asignar, cambiar status de tickets        │
│ ❌ NO pueden crear/editar artículos KB                 │
│ ❌ NO pueden crear/eliminar otros usuarios             │
└─────────────────────────────────────────────────────────┘

PASO 4: ADMIN OBTIENE EL SNIPPET DEL WIDGET
┌─────────────────────────────────────────────────────────┐
│ Admin → "Configuración" o "Embed Widget"               │
│ → Ve el snippet HTML listo para copiar:               │
│                                                         │
│ <script src="https://api.pqrs.com/widget.js"           │
│  data-tenant="TENANT_ID"></script>                     │
│                                                         │
│ Admin lo pega en su sitio web (p.ej., en el footer)   │
└─────────────────────────────────────────────────────────┘

PASO 5: CLIENTE FINAL USA EL WIDGET (Sin login)
┌─────────────────────────────────────────────────────────┐
│ Cliente visita www.empresa.com                         │
│ → Ve botón flotante (Widget incrustado)               │
│ → Hace preguntas en el chat                            │
│ → RAG busca en la KB del tenant                        │
│ → Si encuentra: muestra respuesta de la KB             │
│   "¿Te resolvió esto? [Sí] [No]"                      │
│                                                         │
│   Caso A: Cliente dice [Sí]                            │
│     → Se registra en DeflectedQuery                     │
│     → Fin. Métrica de ahorro operativo                 │
│                                                         │
│   Caso B: Cliente dice [No]                            │
│     → Widget abre formulario de radicación            │
│     → Cliente completa: Nombre, Email, Asunto, Desc   │
│     → Se crea Ticket formal en la DB                  │
│     → IA automáticamente triage:                       │
│        - Tipo (Peticion|Queja|Reclamo|Sugerencia)     │
│        - Prioridad (Alta|Media|Baja)                  │
│        - Sentimiento (Positivo|Neutral|Negativo)      │
│        - Summary (1-2 líneas)                         │
│     → SignalR notifica a Agents del tenant             │
│     → Widget muestra: "Tu ticket #TCK-20250827-ABC123" │
└─────────────────────────────────────────────────────────┘
```

### Tabla de Permisos

| Acción | Cliente Final | Agent | Admin |
|--------|---------------|-------|-------|
| Ver/usar Widget RAG | ✅ | ✗ | ✗ |
| Radicar ticket formal | ✅ | ✅ | ✅ |
| Listar tickets del tenant | ✗ | ✅ | ✅ |
| Asignar/atender tickets | ✗ | ✅ | ✅ |
| Cambiar status de tickets | ✗ | ✅ | ✅ |
| **Crear/editar artículos KB** | ✗ | ✗ | ✅ |
| **Crear/eliminar usuarios** | ✗ | ✗ | ✅ |
| **Cambiar configuración tenant** | ✗ | ✗ | ✅ |

---

## 3. Modelo de datos (5 entidades)

Corrección aplicada: `ITenantEntity` es una **interfaz**, no una entidad persistente — por eso son 5 entidades y no 6.

| Entidad | Rol |
|---|---|
| `Tenant` | Aislamiento multi-tenant (header provider + JWT provider) |
| `User` | Usuario autenticado (Admin o Agent), login, atiende/gestiona tickets |
| `KbArticle` | Base de conocimiento usada por el RAG pre-radicación |
| `DeflectedQuery` | Registro de consultas resueltas por RAG sin llegar a generar ticket (`rag-search` / `rag-feedback`) |
| `Ticket` | PQRS radicado, con clasificación IA (prioridad/sentimiento) |

### Tabla `User`

```
User
-----
Id (Guid)
TenantId (Guid, FK)
Email (string)
PasswordHash (string)
Role (string: "Admin" | "Agent")
CreatedAt (DateTime)
LastLoginAt (DateTime, nullable)
```

**Importante:**
- `Role` tiene dos valores posibles: `Admin` | `Agent`
- El Cliente final **NO tiene fila en esta tabla** — accede exclusivamente mediante el Widget, sin autenticación
- Admin puede: crear/editar artículos KB, crear/eliminar usuarios, cambiar configuración del tenant
- Agent puede: ver y atender tickets, pero NO puede gestionar la KB ni usuarios

### Tabla `Ticket`

```
Ticket
------
Id (Guid)
TenantId (Guid, FK)
TicketNumber (string, unique)

ClientName (string)          ← Nombre del que radicó (sin login)
ClientEmail (string)         ← Email del que radicó

Type (string: P|Q|R|S)       ← Clasificación IA: Peticion|Queja|Reclamo|Sugerencia
Priority (string)            ← Clasificación IA: Alta|Media|Baja
Sentiment (string)           ← Clasificación IA: Positivo|Neutral|Negativo
Summary (string)             ← Resumen de 1-2 líneas generado por IA

Subject (string)             ← Título del ticket
Description (string)         ← Descripción completa

AssignedToUserId (Guid, nullable, FK) ← Quién LO ATIENDE (Agent/Admin que lo tomó)
Status (string)              ← Pendiente|En Proceso|Resuelto

WasResolvedByRag (bool)      ← ¿Se resolvió en la fase RAG sin abrir ticket formal?
CreatedAt (DateTime)
ResolvedAt (DateTime, nullable)
```

**ACLARACIÓN CRÍTICA — AssignedToUserId:**
- `AssignedToUserId` **NO es "quién radicó el ticket"** (ese es el Cliente final, sin login)
- `AssignedToUserId` es **"quién lo está atendiendo"** (el Agent que tomó ownership)
- Es **nullable** porque cuando se crea el ticket, aún no está asignado a nadie
- Un Agent asigna el ticket a sí mismo cuando decide atenderlo
- Cambiar `AssignedToUserId` es responsabilidad de cualquier Agent/Admin del mismo tenant

### Tabla `DeflectedQuery`

```
DeflectedQuery
--------------
Id (Guid)
TenantId (Guid, FK)
ClientQuery (string)         ← La pregunta que hizo el cliente
RagResponse (string)         ← La respuesta que el RAG encontró
UserConfirmed (bool)         ← ¿El cliente dijo "Sí, me resolvió"?
CreatedAt (DateTime)
```

**Propósito:** Registro métrico de consultas que se resolvieron en la fase RAG sin necesidad de abrir ticket formal. Permite medir ahorro operativo.

### Formato de `TicketNumber` (corrección aceptada)

Se descarta el conteo secuencial en capa de aplicación (condición de carrera sin lock/secuencia de DB). Se reemplaza por un formato determinístico derivado del propio `Guid` del ticket:

```
TCK-{yyyyMMdd}-{primeros 6 caracteres del Id en mayúsculas}
```

Único por construcción, cero round-trips extra a la base de datos, cero riesgo de colisión.

---

## 4. Orden de bloques final

| # | Bloque | Incluye |
|---|---|---|
| 1 | Setup + DB + esqueleto Docker | `docker-compose.yml`, `Dockerfile` (stub), `docker compose up db` corriendo desde ya. **Limpieza:** Remover `UseHttpsRedirection()` y endpoint `/weatherforecast`. |
| 2 | Modelo de datos + Tenancy dual | 5 entidades (User/Role, Ticket/AssignedToUserId, DeflectedQuery), DbContext completo, header provider + JWT provider. **Checkpoint:** validar aislamiento cruzado apenas termine (2 tenants de prueba). |
| 3 | Servicios de IA base | DeepSeek (chat) + NVIDIA NIM (embeddings), probados con `curl` antes de integrar al código |
| 4 | RAG pre-radicación | `rag-search`, `rag-feedback`, entidad `DeflectedQuery` |
| 5 | Ticket + Triage + SignalR | Creación de ticket, clasificación IA (prioridad/sentimiento), evento crítico emitido en el mismo flujo. **Prueba obligatoria:** el evento se valida desde un cliente SignalR real (página HTML mínima con `@microsoft/signalr` por CDN). |
| 6 | Widget JS | Una línea, Shadow DOM completo, robusto |
| 7 | JWT completo + CRUD usuarios | Login, `kb-articles` (solo Admin gestiona/crea, Agent consulta), tickets protegidos, endpoints de gestión de `User` restringidos por `Role` |
| 8 | CORS dinámico + aislamiento cruzado | Prueba con 2 tenants (repite y profundiza el checkpoint del Bloque 2) |
| 9 | Validación Docker E2E + README + pulido SOLID | `docker-compose up` completo, diagrama, justificación de decisiones |

### Desglose del Bloque 2 (Modelo de datos + Tenancy dual)

**Fase 1: Entidades + DbContext**
- Definir las 5 entidades con todas sus propiedades
- Especialmente:
  - `User` con `Role` ("Admin" | "Agent")
  - `Ticket` con `AssignedToUserId` (nullable, quién LO ATIENDE, no quién radicó)
  - `Ticket` con `ClientName`, `ClientEmail`, `WasResolvedByRag`
  - `DeflectedQuery` para registrar consultas resueltas por RAG
- Agregar `DbSet<T>` para cada entidad en `ApplicationDbContext`
- Configurar relaciones FK y comportamientos de cascada en `OnModelCreating`

**Fase 2: Índices en DbContext**
- Índice B-Tree compuesto: `(TenantId, Status)` en Ticket
- Índice B-Tree compuesto: `(TenantId, Priority)` en Ticket
- Índice HNSW sobre `Embedding` en KbArticle (agregado en Bloque 3, pero config ahora si es posible)

**Fase 3: Tenancy Provider (Dual)**
- **Header Provider:** Leer `X-Tenant-Id` de encabezado HTTP (para Widget público)
- **JWT Provider:** Leer `tenantId` claim del token JWT (para Agents/Admins autenticados)
- Middleware que resuelve `ITenantContext.TenantId` basándose en cuál esté disponible
- Guard: garantizar que siempre hay un TenantId (si no, 401/403)

**Fase 4: Primera Migración EF Core**
- `dotnet ef migrations add InitialCreate`
- `dotnet ef database update`
- Validar que todas las tablas existan en PostgreSQL

**Fase 5: Endpoint `/seed` (solo en desarrollo)**
- `POST /seed` (sin autenticación, solo en IsDevelopment())
- Crea 2 Tenants de prueba (p.ej., "Tenant-A", "Tenant-B")
- Crea 1 Admin y 1 Agent por tenant
- Crea 2-3 KbArticles por tenant (artículos de prueba)
- Retorna un resumen JSON de qué fue creado
- Se ejecuta manualmente cuando quieras refrescar datos de prueba

**Fase 6: Checkpoint — Aislamiento Cruzado**
- Ejecutar `/seed` para cargar datos de prueba
- Con Postman o `curl`:
  - **Test 1:** Usar Header `X-Tenant-Id: tenant-a` en `GET /api/v1/tickets` → debe ver solo tickets de Tenant-A
  - **Test 2:** Usar Header `X-Tenant-Id: tenant-b` en `GET /api/v1/tickets` → debe ver solo tickets de Tenant-B
  - **Test 3:** Cambiar el header a un Tenant-C inexistente → debe dar 403 o 400
  - **Test 4:** (Futuro con JWT) Login como Admin de Tenant-A, token JWT tiene claim `tenantId=tenant-a`, intentar acceder a datos de Tenant-B → debe fallar
- Resultado: Garantiza que la lógica de aislamiento funciona antes de agregar IA

### Docker como hilo continuo (no un bloque aparte)

- Bloque 1: `docker-compose.yml` + `Dockerfile` (stub) + base de datos corriendo en Docker desde el minuto uno.
- Desarrollo: el backend puede correr con `dotnet run` apuntando a la DB dockerizada (iteración más rápida).
- Checkpoints intermedios de `docker build` del backend: **después del Bloque 2** y **después del Bloque 5**.
- Bloque 9 deja de ser "dockerizar" y pasa a ser "validación E2E completa + README".

### Prueba del cliente SignalR real (Bloque 5)

Como parte del criterio "SignalR bien integrado y probado", el evento crítico no se da por validado solo con logs del servidor. Se crea una página HTML mínima (puede vivir en `/tools/signalr-test.html`, fuera del build de producción) que:

1. Se conecta al hub vía `@microsoft/signalr` (CDN).
2. Se une al grupo del tenant correspondiente.
3. Muestra en pantalla el evento recibido cuando un ticket se crea con `Priority == Alta` o `Sentiment == Negativo`.

Esto confirma end-to-end que el hub emite correctamente al grupo correcto, no solo que el código compila.

---

## 4.1 Endpoint `/seed` (solo desarrollo)

**Ubicación:** `POST /seed` en `Program.cs`

**Guard:** Solo accesible si `app.Environment.IsDevelopment()`. En producción, este endpoint no existe.

**Responsabilidad:**
- Crear 2 Tenants de prueba (p.ej., "Tenant-A", "Tenant-B")
- Crear 1 Admin y 1 Agent por cada tenant
- Crear 2-3 KbArticles de prueba por tenant
- Retornar un resumen JSON de qué fue creado

**No es migración EF:** Las migraciones son solo para esquema. Los datos de prueba van en código (en este endpoint) para poder restablecerlos fácilmente.

**Ejecución:**
```bash
# En desarrollo, después de hacer "dotnet ef database update":
curl -X POST http://localhost:5000/seed

# Respuesta esperada:
{
  "message": "Seed data created",
  "tenants": ["Tenant-A", "Tenant-B"],
  "usersPerTenant": 2,
  "articlesPerTenant": 3
}
```

---

## 5. Prompts exactos (fuente de verdad)

**Nota:** Los prompts en las secciones 5.1 y 5.2 se usan en **Bloques 3, 4 y 5** cuando integres IA. Por ahora en Bloque 2, solo define las entidades.

### 5.1 Prompt de RAG synthesis

```
Sos un asistente de atención al cliente de {TenantName}. Tu tarea es responder la
pregunta del usuario usando ÚNICAMENTE la información de los siguientes artículos
de la base de conocimiento. No inventes información que no esté en el contexto.

Contexto:
{context_chunks}

Pregunta del usuario:
{user_query}

Instrucciones:
- Si el contexto contiene la respuesta, respondé de forma clara y concisa.
- Si el contexto NO contiene información suficiente, respondé exactamente:
  "No encontré información suficiente para responder esto. Te recomiendo radicar
  un ticket para que un agente te ayude."
- No agregues disclaimers ni te presentes, andá directo a la respuesta.
```

### 5.2 Prompt de Triage

```
Sos un clasificador de tickets de soporte. Analizá el siguiente mensaje de un
usuario y devolvé ÚNICAMENTE un objeto JSON con este formato exacto, sin texto
adicional, sin backticks:

{
  "type": "Peticion" | "Queja" | "Reclamo" | "Sugerencia",
  "priority": "Alta" | "Media" | "Baja",
  "sentiment": "Positivo" | "Neutral" | "Negativo",
  "summary": "resumen de una línea del problema"
}

Criterios:
- "type" clasifica la naturaleza del mensaje según el estándar PQRS:
  Peticion (solicitud de información o trámite), Queja (inconformidad con el
  servicio/atención), Reclamo (exigencia de solución ante un incumplimiento
  concreto), Sugerencia (propuesta de mejora sin inconformidad).
- "Alta" si hay urgencia explícita, impacto económico, o el usuario amenaza con
  cancelar/denunciar.
- "Negativo" si el tono expresa frustración, enojo o insatisfacción clara.
- Los cuatro campos son obligatorios en la respuesta; nunca omitas "type".

Mensaje del usuario:
{ticket_message}
```

### 5.3 Umbral de similitud del RAG (0.75)

Se fija en **0.75** (coseno) como umbral mínimo para considerar un chunk recuperado como relevante antes de pasarlo al prompt de síntesis.

**Justificación:** con `nv-embedqa-e5-v5` (modelo de embeddings orientado a QA), valores por debajo de 0.75 tienden a traer chunks temáticamente relacionados pero que no responden la pregunta puntual, degradando la calidad de la síntesis. Un umbral más alto (>0.85) es demasiado estricto para un MVP con una base de conocimiento chica, y deja preguntas válidas sin respuesta (fuerza al usuario a radicar ticket innecesariamente). 0.75 es el punto medio recomendado para bases de conocimiento pequeñas/medianas en este tipo de modelo.

---

## 6. Índices y particionamiento (contenido para el README)

- **Índices B-Tree obligatorios** sobre `Ticket`, alineados con el Assessment:
  - `(TenantId, Status)`: cubre el filtro más frecuente del panel de agentes (tickets abiertos/en progreso/cerrados de un tenant).
  - `(TenantId, Priority)`: cubre la vista de triage/urgencia (tickets de alta prioridad de un tenant).
- **Índice HNSW** sobre la columna de embeddings de `KbArticle` (pgvector) — `HNSW(Embedding)`: necesario porque una búsqueda de similitud exacta (`ORDER BY <-> :query`) sin índice aproximado escala linealmente con el tamaño de la base de conocimiento; HNSW da tiempo sublineal a costo de una pérdida de precisión aceptable para este caso de uso.
- Con estos dos índices alcanza para el volumen de datos de un MVP (decenas de tenants, cientos de artículos y tickets).
- **Siguiente paso a escala (documentado, no implementado):** particionamiento declarativo `PARTITION BY LIST (TenantId)` sobre `Ticket`, para cuando el número de tenants y el volumen de tickets por tenant crezca lo suficiente como para que el índice compuesto por sí solo no alcance a mantener las consultas rápidas por tenant.

---

## 6.1 Decisiones Críticas (aclaraciones de diseño)

**Pregunta 1: ¿Ticket.UserId es "quién radicó" o "quién lo atiende"?**

**Respuesta:** `AssignedToUserId` es **"quién lo está atendiendo"** (el Agent/Admin que tomó ownership).

- El Cliente final NO tiene fila en `User` (accede solo por Widget, sin login)
- Entonces, no hay registro de "quién radicó" en la DB; ese dato está en `ClientName` y `ClientEmail`
- `AssignedToUserId` es nullable porque cuando se crea el ticket, aún no está asignado
- Un Agent se asigna el ticket a sí mismo cuando decide atenderlo

**Pregunta 2: ¿Seeded data en migración EF o endpoint `/seed`?**

**Respuesta:** **Endpoint `/seed` (solo en desarrollo), NO en migración EF.**

- Las migraciones EF son para esquema, no para datos de prueba que cambian
- `/seed` es un endpoint que ejecutas manualmente cuando quieras refrescar datos de prueba
- Proteges con `if (app.Environment.IsDevelopment())` para que no exista en producción
- Mucho más flexible que `HasData()` en migraciones

**Pregunta 3: ¿HTTP puro en desarrollo?**

**Respuesta:** Sí, **HTTP puro sin HTTPS en desarrollo/MVP**.

- Quitar `app.UseHttpsRedirection()` de `Program.cs`
- Quitar endpoint `/weatherforecast` de ejemplo
- En producción (futuro), agregar HTTPS con certificados reales

**Pregunta 4: ¿Cuándo registra la empresa sus FAQs? ¿Quién gestiona?**

**Respuesta:** Solo **Admin** puede gestionar la KB. Flujo de onboarding:

1. Empresa se registra → Sistema crea Tenant + Admin user
2. Admin hace login → Va a "Gestión de Artículos"
3. Admin crea artículos KB (p.ej., FAQs sobre contraseña, devoluciones, etc.)
4. Cada artículo genera automáticamente un embedding
5. Admin opcionalmente invita Agents → Ellos pueden ver tickets pero NO editar KB
6. Admin obtiene snippet del Widget → Lo pega en su sitio web
7. Cliente final usa Widget → RAG busca en la KB creada por el Admin

---

## 7. Principios SOLID aplicados

| Principio | Dónde se aplica |
|---|---|
| **S**RP | Separación de servicios: `IEmbeddingService`, `IChatCompletionService`, `ITriageService`, `IRagService` — cada uno con una única responsabilidad |
| **O**CP | Nuevos proveedores de IA se agregan implementando la interfaz existente, sin modificar el código que los consume |
| **L**SP | Cualquier implementación de `IEmbeddingService` (NVIDIA, o a futuro otro proveedor) es intercambiable sin romper el `RagService` |
| **I**SP | Interfaces chicas y específicas (`IEmbeddingService` no expone métodos de chat, y viceversa) |
| **D**IP | El `RagService` y el `TriageService` dependen de `IEmbeddingService`/`IChatCompletionService` (abstracciones), no de las clases concretas de DeepSeek/NVIDIA |

Incluir en el README un diagrama simple (ASCII o Mermaid) del flujo: `Ticket creado → TriageService → (IChatCompletionService) → SignalR Hub → Clients.Group(tenantId)`.

---

## 8. Widget JS (integración de una línea)

Es el canal exclusivo del Cliente final (ver Sección 2.1 — no implica login ni rol).

- **Shadow DOM completo**: todo el CSS/HTML del widget vive aislado del DOM del sitio anfitrión, sin fugas de estilos en ninguna dirección.
- **IIFE sin variables globales**: el script se autoejecuta y no contamina el `window` del sitio anfitrión.
- **`try/catch` en cada `fetch`**: un fallo de red o de API no debe romper el sitio anfitrión que lo integra; el widget debe degradar silenciosamente (o mostrar un estado de error contenido dentro de su propio Shadow DOM).

---

## 9. Checklist de validación progresiva (P2 — aislamiento)

- [ ] Bloque 2: chequeo rápido de aislamiento apenas termina tenancy (2 tenants, verificar que uno no ve datos del otro).
- [ ] Bloque 5: `docker build` del backend.
- [ ] Bloque 8: prueba completa de aislamiento cruzado con 2 tenants (CORS dinámico incluido).
- [ ] Bloque 9: `docker-compose up` end-to-end completo antes de entregar.
