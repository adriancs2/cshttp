# Hearth File Manager (cshttp port)

Self-hosted .NET Framework 4.8 (`cshttp.server` + `cshttp.web`, C# 7.3) file manager with a Gemini-powered website builder, ported from the original Pageless ASP.NET Web Forms application.

## Layout

```
HearthFileManager/
  Program.cs                entry point, server options, startup + routing switch (was Global.asax.cs)
  engine/                   core engine and cshttp helpers
    ApiHelper.cs            JSON response helpers and request termination
    AppConfig.cs            /App_Data/config.json, users, permissions, root path validation, PBKDF2 hashing
    AppSession.cs           "hfm" cookie → RAM → App_Data/sessions.json
    Guard.cs                authentication, permission and CSRF header ("X-Hearth: 1") checks
    PageTemplate.cs         shared HTML shell and navigation rendering
    StaticAsset.cs          cache-busting version query string (?v=<ticks>) via HostingEnvironment.MapPath
    FsService.cs            sandboxed file ops on managed roots (zip, recycle bin, chunked upload, search)
    Ai/GeminiAgent.cs       Gemini REST + function-calling loop, rate limiter, model fallback
    Ai/AiTools.cs           tools exposed to Gemini (list/search/read/write/replace/mkdir/move/delete/sqlite)
    Ai/AiUndo.cs            per-turn snapshots in App_Data/ai-undo (last 50 kept)
    Ai/AiTaskManager.cs     background AI turns + polling
  RH/                       one handler per file: XxxPage (HTML) + XxxPageApi (JSON)
  js/                       site.js (Api, Toast, Dialog, Fmt), files.js, ai.js, users.js
  js/components/            hearth-editor.js (HearthEditor.open(path)), hearth-store.js (IndexedDB)
  css/                      site.css, files.css, ai.css, components/hearth-editor.css
  assets/fontawesome/       self-hosted Font Awesome
  App_Data/
    config.json             users, Gemini key/model/fallbacks/rpm, upload limit, SiteRoot, SitePreviewUrl, DevAutoLogin
    public/                 default managed website root (HTML + PHP; served by a separate web server if live preview is needed)
    recycle-bin/            deleted items (index: App_Data/recycle-index.json)
    ai-undo/                per-turn undo snapshots
    tmp/                    in-progress chunked uploads (*.part)
tests/
  test-roots.ps1            test root-folder resolution, per-root isolation and shared recycle bin (no HTTP, no Gemini)
  test-gemini.ps1           run one AI turn through the compiled exe (no HTTP)
  test-undo.ps1             undo the oldest AI turn in the sandbox
e2e-test.ps1                end-to-end HTTP test suite (spawns HearthFileManager.exe and runs 46 checks)
```

## Running & Deploying

1. Build with `dotnet build` inside `HearthFileManager\`. The compiled executable and dependencies (`Newtonsoft.Json.dll`, `System.Data.SQLite.dll`, `x64\SQLite.Interop.dll`, `x86\SQLite.Interop.dll`, and `cshttp*.dll`) are placed in `bin\Debug\net48\`.
2. The process identity needs **modify** rights on `App_Data` (`config.json`, `sessions.json`, `public`, `recycle-bin`, `ai-undo`, `tmp`).
3. **Set `"DevAutoLogin": false`** in `App_Data/config.json` on production (it only works for `localhost` anyway).
4. Public website (HTML + PHP): by default stored in `App_Data\public`. If serving PHP live, point a separate web server (e.g., IIS or Caddy/PHP) to that folder.
5. Uploads are sent in 5 MB chunks, so `ServerOptions.MaxRequestBodySize` in `Program.cs` only needs 16 MB per request; the per-file limit is `MaxUploadMb` in `config.json` (default 500).
6. First login is `admin` / `admin` unless `config.json` already has users – change it on the Users page.

## Gemini free tier notes (measured on this key, Oct 2026)

- Free daily quota is **per model** and small (e.g. 20 requests/day for `gemini-3.5-flash`).
  One builder request typically uses 2–6 API requests. When a model's quota is used up the agent
  switches to `GeminiFallbackModels` automatically; quotas reset at midnight US Pacific time.
- Google's servers often return 503 "high demand" or hang; the agent retries with back-off and a
  150 s per-request timeout.
- Free-tier prompts may be used by Google to improve its products.

## Testing the engine directly (without HTTP)

```
powershell -ExecutionPolicy Bypass -File tests\test-roots.ps1
powershell -ExecutionPolicy Bypass -File tests\test-gemini.ps1 -Prompt "write a simple under maintenance page" [-Model gemini-flash-latest]
powershell -ExecutionPolicy Bypass -File tests\test-undo.ps1
```
`test-gemini.ps1` uses the key/model from `HearthFileManager/App_Data/config.json`, works on `tests/sandbox/App_Data`, and keeps
the conversation in `tests/sandbox/history.json` (delete it to start fresh).
