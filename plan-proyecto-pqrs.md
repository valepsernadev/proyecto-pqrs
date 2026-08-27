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
Id
TenantId
Email
PasswordHash
Role
CreatedAt
```

`Role` es un enum con dos valores posibles: `Admin` | `Agent`. El Cliente final no tiene fila en esta tabla — accede exclusivamente mediante el Widget, sin autenticación.

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
| 1 | Setup + DB + esqueleto Docker | `docker-compose.yml`, `Dockerfile` (stub), `docker compose up db` corriendo desde ya |
| 2 | Modelo de datos + Tenancy dual | 5 entidades (incluye `User` con `Role`: Admin/Agent), header provider + JWT provider. **Checkpoint:** validar aislamiento cruzado apenas termine tenancy (chequeo rápido, no esperar al Bloque 8) |
| 3 | Servicios de IA base | DeepSeek (chat) + NVIDIA NIM (embeddings), probados con `curl` antes de integrar al código |
| 4 | RAG pre-radicación | `rag-search`, `rag-feedback`, entidad `DeflectedQuery` |
| 5 | Ticket + Triage + SignalR | Creación de ticket, clasificación IA (prioridad/sentimiento), evento crítico emitido en el mismo flujo (`Hub` + `Clients.Group(tenantId).SendAsync(...)`). **Prueba obligatoria:** el evento se valida desde un cliente SignalR real (página HTML mínima con `@microsoft/signalr` por CDN), no solo desde logs del backend |
| 6 | Widget JS | Una línea, Shadow DOM completo, robusto |
| 7 | JWT completo + CRUD usuarios | Login, `kb-articles` (solo Admin gestiona/crea, Agent consulta), tickets protegidos, endpoints de gestión de `User` restringidos por `Role` |
| 8 | CORS dinámico + aislamiento cruzado | Prueba con 2 tenants (repite y profundiza el checkpoint del Bloque 2) |
| 9 | Validación Docker E2E + README + pulido SOLID | `docker-compose up` completo, diagrama, justificación de decisiones |

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

## 5. Prompts exactos (fuente de verdad)

### 4.1 Prompt de RAG synthesis

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

### 4.2 Prompt de Triage

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

### 4.3 Umbral de similitud del RAG (0.75)

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

Es el canal exclusivo del Cliente final (ver Sección 2 — no implica login ni rol).

- **Shadow DOM completo**: todo el CSS/HTML del widget vive aislado del DOM del sitio anfitrión, sin fugas de estilos en ninguna dirección.
- **IIFE sin variables globales**: el script se autoejecuta y no contamina el `window` del sitio anfitrión.
- **`try/catch` en cada `fetch`**: un fallo de red o de API no debe romper el sitio anfitrión que lo integra; el widget debe degradar silenciosamente (o mostrar un estado de error contenido dentro de su propio Shadow DOM).

---

## 9. Checklist de validación progresiva (P2 — aislamiento)

- [ ] Bloque 2: chequeo rápido de aislamiento apenas termina tenancy (2 tenants, verificar que uno no ve datos del otro).
- [ ] Bloque 5: `docker build` del backend.
- [ ] Bloque 8: prueba completa de aislamiento cruzado con 2 tenants (CORS dinámico incluido).
- [ ] Bloque 9: `docker-compose up` end-to-end completo antes de entregar.
