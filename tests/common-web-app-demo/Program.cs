using System;
using System.IO;
using cshttp;
using CommonWebAppDemo.engine;
using CommonWebAppDemo.RH;

namespace CommonWebAppDemo
{
    static class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("  cshttp Enterprise Membership Demo (.NET 10)");
            Console.WriteLine("==================================================");

            // Initialize SQLite Database
            Db.Init();
            Console.WriteLine("SQLite Database initialized.");

            int port = 8080;
            Console.WriteLine("Server listening on http://localhost:" + port);
            Console.WriteLine("Press Ctrl+C to terminate.");
            Console.WriteLine("==================================================");

            WebApp.Run(port, OnRequest);
        }

        static void OnRequest()
        {
            string path = Request.Path.ToLower();

            // 1. Static file handler (/assets/, /uploads/, favicon)
            if (path.StartsWith("/assets/") || path.StartsWith("/uploads/") || path == "/favicon.ico" || path == "/favicon.svg")
            {
                string virtualPath = path;
                if (path == "/favicon.ico" || path == "/favicon.svg")
                {
                    virtualPath = "/assets/images/favicon.svg";
                }

                string filePath = WebApp.MapPath(virtualPath);
                if (!Response.TransmitFile(filePath))
                {
                    Response.StatusCode = 404;
                }
                return;
            }

            // 2. Application Route Interception
            switch (path)
            {
                case "/":
                case "/home":
                case "/members":
                    MemberPage.HandleRequest();
                    return;

                case "/about":
                    AboutPage.HandleRequest();
                    return;

                case "/memberapi":
                    MemberPageApi.HandleRequest();
                    return;

                case "/member/print":
                    MemberPrintPage.HandleRequest();
                    return;

                default:
                    Response.StatusCode = 404;
                    Response.ContentType = "text/html; charset=utf-8";
                    Response.Write("<!DOCTYPE html><html><head><title>404 Not Found</title></head><body><h1>404 Not Found</h1><p>The requested route does not exist.</p><p><a href='/'>Go Home</a></p></body></html>");
                    return;
            }
        }
    }
}
