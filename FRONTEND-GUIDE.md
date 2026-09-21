# V6 Playground — Frontend Guide

Guide for building and calling the V6 External Playground UI (`wwwroot`).

---

## 1. What this app is

V6Playground is a **browser UI + thin ASP.NET proxy**. Pages call **local** wrapper APIs (`/api/Client/*`, `/api/v6/*`). The server forwards to the hosted V6 / SaaSApp API.

```
Browser (HTML/JS)
    →  Playground wrappers  (/api/Client, /api/v6)
        →  Hosted V6 API    (V6Api:BaseUrl in appsettings)
```

Frontend code never opens SQL or PostgreSQL. Configure the backend base URL only in server config.

| Setting | File | Current value |
|---------|------|----------------|
| Hosted API | `appsettings.json` → `V6Api:BaseUrl` | `https://cloud.ezofis.com` |

Confirm at runtime: `GET /api/Client/info` → `v6ApiBaseUrl`.

---

## 2. Run & open the UI

| Host | Typical URL |
|------|-------------|
| Kestrel (dev) | `https://localhost:5180/` or `http://localhost:5180/` |
| IIS sub-app | `http://localhost/V6Playground/` |

Default page: `apikey.html`.

**Scripts every page should load (order matters):**

1. `js/playground-base.js` — path base for IIS (`/V6Playground`)
2. `js/playground-common.js` — session, toast, `apiFetch`
3. `js/playground-shell.js` — header / brand
4. `js/playground-sidebar.js` — left nav

CSS: `theme.css`, `css/playground-*.css`.

---

## 3. Path base (`PlaygroundBase`)

Under IIS the app may live at `/V6Playground`. Always build links and API URLs like this:

```js
// Page navigation
PlaygroundBase.withBase('/workflow-start.html')

// API call
PlaygroundBase.apiUrl('/api/v6/workflows')
// → http://localhost/V6Playground/api/v6/workflows  (when under IIS)
// → http://localhost:5180/api/v6/workflows          (when site root)
```

Fallback if script missing:

```js
const url = window.PlaygroundBase
  ? PlaygroundBase.apiUrl('/api/v6/workflows')
  : '/api/v6/workflows';
```

---

## 4. Auth flow (frontend)

### Step A — Generate API key

Page: `apikey.html`

```http
POST /api/Client/apiKey?userName={email}&password={password}&daysValid=7
```

Optional: `tenantId`, or social `provider=google|microsoft`.

**Success (200)** — store and reuse:

| Field | Use |
|-------|-----|
| `apiKey` | Header `X-API-Key` on all `/api/v6/*` calls |
| `tenantId` | Tenant context / listing keys |
| `expiresAt` | Key expiry |

### Step B — Call playground wrappers

```http
GET /api/v6/workflows
X-API-Key: {apiKey}
```

Without a valid `X-API-Key`, middleware rejects `/api/v6/*` (Client routes for key/login stay open).

### Session in the browser (`localStorage`)

Managed by `playground-common.js`:

| Key | Purpose |
|-----|---------|
| `v6PgApiKey` | Last API key |
| `v6PgTenantId` | Tenant id |
| `v6PgAccessToken` | Bearer token (if used) |
| `v6PgLastEmail` | Last email |

```js
PlaygroundCommon.saveSession({ apiKey, tenantId, email: userName });
PlaygroundCommon.restoreFields(); // fills #apiKey, #tenantId, etc.
```

---

## 5. Pages map

### API key

| Page | Purpose |
|------|---------|
| `apikey.html` | Login + create playground API key |
| `my-api-keys.html` | List keys / usage for user + tenant |
| `generate-token.html` | Obtain access token |

### Workflow

| Page | Wrapper |
|------|---------|
| `workflow-get.html` | `GET /api/v6/workflows` |
| `workflow-start.html` | `POST /api/v6/workflows/start` (multipart; prefers `workflowName`) |
| `move-next.html` | `POST /api/v6/workflows/instances/{instanceId}/move-next` |
| `inbox.html` | `GET /api/v6/workflows/inbox` |

### Repository

| Page | Wrapper |
|------|---------|
| `upload-file.html` | `POST /api/v6/repositories/upload-file` |
| `download-file.html` | List items + `GET .../items/{itemId}/file` |
| `repository-filter.html` | filter-fields → facets → query |

### Tools

| Page | Purpose |
|------|---------|
| `examples.html` | curl / Python / JS snippets |
| `playground-documentation.html` | Endpoint cards |
| `usage-report.html` | API usage chart |

---

## 6. Wrapper API cheat sheet

Base = playground origin (+ path base if IIS).

### Client (no `X-API-Key` required for key/login)

| Method | Path | Notes |
|--------|------|--------|
| GET | `/api/Client/info` | Playground name + `v6ApiBaseUrl` |
| POST | `/api/Client/apiKey` | Create key (`userName`, `password` or `provider`) |
| GET | `/api/Client/apiKey` | Re-login / return active key |
| GET | `/api/Client/tenants?userName=` | Tenant picker |
| GET | `/api/Client/apiKeys` | Keys for user |
| GET | `/api/Client/apiUsage` | Usage for user |
| POST | `/api/Client/token` | Token helper |

### V6 wrappers (require `X-API-Key`)

| Method | Path |
|--------|------|
| GET | `/api/v6/workflows` |
| POST | `/api/v6/workflows/start` |
| GET | `/api/v6/workflows/inbox` |
| POST | `/api/v6/workflows/instances/{instanceId}/move-next` |
| GET | `/api/v6/repositories` |
| POST | `/api/v6/repositories/upload-file` |
| GET | `/api/v6/repositories/{id}/items` |
| GET | `/api/v6/repositories/{id}/items/{itemId}/file` |
| GET | `/api/v6/repositories/{id}/items/filter-fields` |
| GET | `/api/v6/repositories/{id}/items/facets/{name}` |
| POST | `/api/v6/repositories/{id}/items/query` |

Upstream SaaSApp docs: [cloud.ezofis.com/swagger](https://cloud.ezofis.com/swagger/index.html).

---

## 7. Frontend fetch patterns

### JSON + API key

```js
async function listWorkflows(apiKey) {
  const res = await fetch(PlaygroundBase.apiUrl('/api/v6/workflows'), {
    headers: { 'X-API-Key': apiKey }
  });
  const data = await res.json();
  if (!res.ok) throw new Error(typeof data === 'string' ? data : JSON.stringify(data));
  return data;
}
```

### Multipart (start workflow / upload)

```js
const form = new FormData();
form.append('workflowName', name);
form.append('file', fileInput.files[0]);
form.append('context', 'string');
form.append('envType', 'string');

const res = await fetch(PlaygroundBase.apiUrl('/api/v6/workflows/start'), {
  method: 'POST',
  headers: { 'X-API-Key': apiKey }, // do not set Content-Type — browser sets boundary
  body: form
});
```

### Shared helper

```js
const { ok, status, data } = await PlaygroundCommon.apiFetch('/api/v6/workflows', {
  apiKey: document.getElementById('apiKey').value
});
```

---

## 8. Adding a new frontend page

1. Copy an existing page (e.g. `workflow-get.html`) for layout shell + sidebar mount.
2. Include the four `js/playground-*.js` scripts.
3. Call **playground wrappers** (`/api/v6/...` or `/api/Client/...`), not the cloud host directly from the browser (keeps API key + CORS simple).
4. Register the page in `js/playground-sidebar.js` (`WORKFLOW_ITEMS`, `REPOSITORY_ITEMS`, or Tools).
5. Prefer `workflowName` / `repositoryName` when the wrapper resolves IDs for you.

---

## 9. Troubleshooting (frontend)

| Symptom | What to check |
|---------|----------------|
| 401 / 403 on `/api/v6/*` | Missing or expired `X-API-Key` — regenerate on `apikey.html` |
| 502 / nginx HTML body | Hosted `V6Api:BaseUrl` down or wrong — check `/api/Client/info` and cloud API |
| 404 under IIS | Missing `PlaygroundBase`; path not under `/V6Playground` |
| Empty API key field on other pages | `localStorage` cleared — generate key again |
| CORS errors calling cloud from browser | Call playground wrappers only, not `cloud.ezofis.com` from JS |

Quick health check:

```http
GET /api/Client/info
```

Expect JSON with `v6ApiBaseUrl` matching your intended cloud/demo host.

---

## 10. Related files

| Area | Path |
|------|------|
| UI pages | `wwwroot/*.html` |
| Shared JS | `wwwroot/js/playground-*.js` |
| Styles | `wwwroot/theme.css`, `wwwroot/css/` |
| Wrappers | `Controllers/ApiKeyController.cs`, `V6WorkflowController.cs`, `V6RepositoryController.cs` |
| Hosted API config | `appsettings.json` → `V6Api:BaseUrl` |
| Code samples UI | `wwwroot/examples.html` |
