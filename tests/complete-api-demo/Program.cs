using System;
using cshttp;
using CompleteApiDemo.RH;

namespace CompleteApiDemo
{
    static class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("  cshttp Complete API Feature Showcase (.NET 10)");
            Console.WriteLine("==================================================");

            int port = 8085;
            Console.WriteLine($"Server listening on http://localhost:{port}/");
            Console.WriteLine("Press Ctrl+C to stop.");
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
            if (path.StartsWith("/api/"))
            {
                ApiHandler.HandleRequest();
                return;
            }

            switch (path)
            {
                case "/":
                case "/home":
                    HomePage.HandleRequest();
                    return;

                case "/about":
                    AboutPage.HandleRequest();
                    return;

                default:
                    Response.StatusCode = 404;
                    Response.ContentType = "text/html; charset=utf-8";
                    Response.Write("<!DOCTYPE html><html><head><title>404 Not Found</title></head><body><h1>404 Not Found</h1><p>Route does not exist.</p><p><a href='/'>Go Home</a></p></body></html>");
                    return;
            }
        }
    }
}
