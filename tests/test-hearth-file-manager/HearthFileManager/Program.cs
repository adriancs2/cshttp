using System;
using System.IO;
using System.Net;
using cshttp;
using HearthFileManager.engine;

namespace HearthFileManager
{
    /// <summary>
    /// Replaces Global.asax.cs. Application_Start is the code before server.Run;
    /// Application_BeginRequest is the handler. The switch table is unchanged.
    ///
    /// Usage: HearthFileManager.exe [--port 8080] [--public] [--root path]
    ///   --public  listen on all interfaces (default: localhost only)
    ///   --root    folder that contains App_Data, css, js, assets (default: found automatically)
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            int port = 8080;
            bool isPublic = false;
            string root = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--port" && i + 1 < args.Length) port = int.Parse(args[++i]);
                else if (args[i] == "--public") isPublic = true;
                else if (args[i] == "--root" && i + 1 < args.Length) root = args[++i];
            }
            root = root ?? FindContentRoot();
            if (root == null)
            {
                Console.Error.WriteLine("Cannot find the content root (a folder with App_Data, css and js). Use --root <path>.");
                return 1;
            }

            var options = new ServerOptions
            {
                ContentRoot = root,
                MaxRequestBodySize = 16L * 1024 * 1024,           // was maxAllowedContentLength (uploads arrive in 5 MB chunks)
                Log = msg => Console.WriteLine("[cshttp] " + msg),
                OnUnhandledException = (ctx, ex) => Console.Error.WriteLine("[error] " + ctx.Request.RawUrl + ": " + ex.Message)
            };
            options.ListenHttp(isPublic ? IPAddress.Any : IPAddress.Loopback, port);

            // ---- was Application_Start ----
            HostingEnvironment.SetApplicationPhysicalPath(root);
            AppConfig.AppDataPath = HostingEnvironment.MapPath("~/App_Data");
            var fs = new FsService(AppConfig.AppDataPath, AppConfig.MainRoot());
            fs.CleanupTemp(TimeSpan.FromDays(1));
            AppConfig.Get(); // migrates/creates config.json

            Console.WriteLine("HearthFileManager on cshttp: http://" + (isPublic ? "*" : "localhost") + ":" + port + "/   (root: " + root + ")");
            Console.WriteLine("Press Ctrl+C to stop.");
            new HttpServer(options).Run(BeginRequest);
            return 0;
        }

        // ---- was Application_BeginRequest ----
        static void BeginRequest(HttpContext ctx)
        {
            HttpRequest Request = ctx.Request;
            HttpResponse Response = ctx.Response;

            // was <httpProtocol><customHeaders> in web.config
            Response.AppendHeader("X-Content-Type-Options", "nosniff");
            Response.AppendHeader("Referrer-Policy", "same-origin");

            string path = Request.Path.ToLower().Trim().TrimEnd('/');

            // static assets: IIS used to serve these
            if (path.StartsWith("/css/")) { StaticFiles.TryServe(ctx, "/css/", "css"); return; }
            if (path.StartsWith("/js/")) { StaticFiles.TryServe(ctx, "/js/", "js"); return; }
            if (path.StartsWith("/assets/")) { StaticFiles.TryServe(ctx, "/assets/", "assets"); return; }

            AppSession.TryRestoreFromCookie();

            switch (path)
            {
                case "":
                case "/home":         Response.Redirect("/files", false); ApiHelper.EndResponse(); return;
                case "/login":        RH.LoginPage.HandleRequest(); return;
                case "/loginapi":     RH.LoginPageApi.HandleRequest(); return;
                case "/logout":       RH.LogoutPage.HandleRequest(); return;
                case "/files":        RH.FilesPage.HandleRequest(); return;
                case "/fileapi":      RH.FilesPageApi.HandleRequest(); return;
                case "/ai":           RH.AiPage.HandleRequest(); return;
                case "/aiapi":        RH.AiPageApi.HandleRequest(); return;
                case "/users":        RH.UsersPage.HandleRequest(); return;
                case "/userapi":      RH.UsersPageApi.HandleRequest(); return;
                case "/settings":     RH.SettingsPage.HandleRequest(); return;
                case "/settingsapi":  RH.SettingsPageApi.HandleRequest(); return;
                case "/account":      RH.AccountPage.HandleRequest(); return;
                case "/accountapi":   RH.AccountPageApi.HandleRequest(); return;
            }

            // IIS answered unknown URLs with 404
            Response.StatusCode = 404;
            Response.ContentType = "text/plain";
            Response.Write("404 Not Found");
        }

        /// <summary>Walks up from the exe folder to the folder holding App_Data, css and js (the project folder in development).</summary>
        static string FindContentRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "App_Data")) && Directory.Exists(Path.Combine(dir.FullName, "css")) && Directory.Exists(Path.Combine(dir.FullName, "js")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
