# HearthFileManager on cshttp: drop-in test

A copy of `D:\aifiles\FileManagerHearth\HearthFileManager` converted from ASP.NET + IIS to cshttp.
The original project is untouched.

## Run

```
cd HearthFileManager
dotnet build
bin\Debug\net48\HearthFileManager.exe            # http://localhost:8080
bin\Debug\net48\HearthFileManager.exe --port 9000 --public
powershell -ExecutionPolicy Bypass -File ..\e2e-test.ps1   # 46 end-to-end checks (spawns its own server on port 18080)
```

## What changed

| Change | Files |
|---|---|
| `using System.Web;` / `using System.Web.Hosting;` → `using cshttp;` | 17 (one line each) |
| `Global.asax` + `Global.asax.cs` → `Program.cs` (same switch table) | 1 new |
| Old web-application `.csproj` → SDK-style exe `.csproj` (net48) | 1 |
| `web.config` limits and custom headers → `ServerOptions` / two `AppendHeader` lines in `Program.cs` | - |
| Static `/css/ /js/ /assets/` (was IIS) → three `StaticFiles.TryServe` lines | - |
| Unknown URLs → explicit 404 (was IIS) | - |

No other application code was changed: engine, RH handlers, JavaScript and CSS are identical.

## What the test found in cshttp

`Response.Cache.SetCacheability(...)` / `SetMaxAge(...)` (used by `FilesPageApi.Raw`) did not exist.
Added `HttpCachePolicy` and `HttpCacheability` to cshttp.web, with tests.

## Still the app's own dependencies

Newtonsoft.Json and System.Data.SQLite come from the local `packages` folder, exactly as before.
They are dependencies of HearthFileManager, not of cshttp.
