using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using cshttp;
using Newtonsoft.Json;

namespace CompleteApiDemo.RH
{
    public static class ApiHandler
    {
        // In-memory counters for background work tracking
        static int _bgTasksTotal = 0;
        static int _bgTasksCompleted = 0;

        public static void HandleRequest()
        {
            string path = Request.Path.ToLower();

            switch (path)
            {
                case "/api/hello":
                    HandleHello();
                    return;

                case "/api/query-demo":
                    HandleQueryDemo();
                    return;

                case "/api/form-demo":
                    HandleFormDemo();
                    return;

                case "/api/cookie-demo":
                    HandleCookieDemo();
                    return;

                case "/api/body-text-demo":
                    HandleBodyTextDemo();
                    return;

                case "/api/body-bytes-demo":
                    HandleBodyBytesDemo();
                    return;

                case "/api/status-demo":
                    HandleStatusDemo();
                    return;

                case "/api/output-demo":
                    HandleOutputDemo();
                    return;

                case "/api/binary-image-demo":
                    HandleBinaryImageDemo();
                    return;

                case "/api/upload-single-demo":
                    HandleUploadSingle();
                    return;

                case "/api/upload-multi-demo":
                    HandleUploadMulti();
                    return;

                case "/api/background-work-demo":
                    HandleBackgroundWorkQueue();
                    return;

                case "/api/background-work-status":
                    HandleBackgroundWorkStatus();
                    return;

                case "/api/httputility-demo":
                    HandleHttpUtilityDemo();
                    return;

                default:
                    Response.StatusCode = 404;
                    Response.ContentType = "text/plain; charset=utf-8";
                    Response.Write("API endpoint not found: " + Request.Path);
                    return;
            }
        }

        static void HandleHello()
        {
            Response.ContentType = "text/plain; charset=utf-8";
            Response.Write($"Hello from cshttp! Path = '{Request.Path}', Method = '{Request.HttpMethod}', Time = {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        }

        static void HandleQueryDemo()
        {
            var data = new
            {
                message = "Query parameters parsed successfully!",
                received = new
                {
                    name = Request.QueryString["name"],
                    role = Request.QueryString["role"],
                    allKeys = Request.QueryString.AllKeys
                },
                rawUrl = Request.RawUrl
            };
            SendJson(data);
        }

        static void HandleFormDemo()
        {
            if (Request.HttpMethod != "POST")
            {
                Response.StatusCode = 405;
                Response.Write("Method Not Allowed. Use POST.");
                return;
            }

            var data = new
            {
                message = "POST form data parsed successfully via Request.Form!",
                formValues = new
                {
                    username = Request.Form["username"],
                    message = Request.Form["message"]
                },
                contentLength = Request.ContentLength
            };
            SendJson(data);
        }

        static void HandleCookieDemo()
        {
            string action = Request.Item("action");

            if (action == "set")
            {
                string key = Request.Form["key"] ?? "test_cookie";
                string val = Request.Form["val"] ?? "default_value";

                HttpCookie cookie = new HttpCookie(key, val)
                {
                    Expires = DateTime.UtcNow.AddDays(7),
                    HttpOnly = true,
                    Path = "/"
                };
                Response.Cookies.Add(cookie);

                SendJson(new { status = "Cookie Set", key, value = val, expires = cookie.Expires });
                return;
            }

            // Read cookies
            var list = new Dictionary<string, string>();
            foreach (string k in Request.Cookies.AllKeys)
            {
                list[k] = Request.Cookies[k]?.Value;
            }

            SendJson(new
            {
                status = "Cookies Read",
                count = Request.Cookies.Count,
                cookies = list
            });
        }

        static void HandleBodyTextDemo()
        {
            string text = Request.BodyText;

            SendJson(new
            {
                message = "Request.BodyText evaluated lazily and cached!",
                length = text.Length,
                preview = text,
                contentType = Request.ContentType
            });
        }

        static void HandleBodyBytesDemo()
        {
            byte[] bytes = Request.BodyBytes;

            string hexPreview = "";
            if (bytes.Length > 0)
            {
                int max = Math.Min(bytes.Length, 16);
                string[] hexParts = new string[max];
                for (int i = 0; i < max; i++) hexParts[i] = "0x" + bytes[i].ToString("X2");
                hexPreview = string.Join(", ", hexParts) + (bytes.Length > max ? "..." : "");
            }

            SendJson(new
            {
                message = "Request.BodyBytes accessed directly!",
                totalBytes = bytes.Length,
                hexPreview
            });
        }

        static void HandleStatusDemo()
        {
            int code;
            if (!int.TryParse(Request.QueryString["code"], out code)) code = 200;

            Response.StatusCode = code;
            Response.ContentType = "text/plain; charset=utf-8";
            Response.Write($"Response status explicitly set to {code}!");
        }

        static void HandleOutputDemo()
        {
            string type = Request.QueryString["type"] ?? "json";

            switch (type)
            {
                case "html":
                    Response.ContentType = "text/html; charset=utf-8";
                    Response.Write("<h3>Dynamic HTML rendered via Response.Write</h3><p>Server time: " + DateTime.UtcNow + "</p>");
                    break;

                case "text":
                    Response.ContentType = "text/plain; charset=utf-8";
                    Response.Write("Pure plain text payload rendered via Response.Write.");
                    break;

                default:
                    Response.ContentType = "application/json; charset=utf-8";
                    Response.Write("{\"status\":\"success\",\"message\":\"JSON string written via Response.Write\"}");
                    break;
            }
        }

        static void HandleBinaryImageDemo()
        {
            // Pure minimal valid 2x2 transparent PNG in byte array
            byte[] png = new byte[] {
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
                0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x02,
                0x08, 0x06, 0x00, 0x00, 0x00, 0x72, 0xB6, 0x0D, 0x24, 0x00, 0x00, 0x00,
                0x0F, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x60, 0x60, 0xF8, 0x0F,
                0x00, 0x00, 0x04, 0x01, 0x01, 0x00, 0x06, 0x24, 0xF5, 0xE9, 0x00, 0x00,
                0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
            };

            Response.ContentType = "image/png";
            Response.BinaryWrite(png);
        }

        static void HandleUploadSingle()
        {
            HttpPostedFile file = Request.Files["avatar"];
            if (file == null || file.ContentLength == 0)
            {
                SendJson(new { error = "No file uploaded in 'avatar' field." });
                return;
            }

            string uploadsDir = WebApp.MapPath("~/uploads");
            if (!Directory.Exists(uploadsDir)) Directory.CreateDirectory(uploadsDir);

            string safeFileName = Path.GetFileName(file.FileName);
            string dest = Path.Combine(uploadsDir, safeFileName);
            file.SaveAs(dest);

            SendJson(new
            {
                status = "File Uploaded Successfully",
                fieldName = "avatar",
                fileName = safeFileName,
                sizeBytes = file.ContentLength,
                contentType = file.ContentType,
                savedTo = "/uploads/" + safeFileName
            });
        }

        static void HandleUploadMulti()
        {
            var files = Request.Files.GetMultiple("photos");
            if (files == null || files.Count == 0)
            {
                SendJson(new { error = "No files found in 'photos' multiple field." });
                return;
            }

            string uploadsDir = WebApp.MapPath("~/uploads");
            if (!Directory.Exists(uploadsDir)) Directory.CreateDirectory(uploadsDir);

            var savedList = new List<object>();
            foreach (var f in files)
            {
                if (f.ContentLength > 0)
                {
                    string safe = Path.GetFileName(f.FileName);
                    string dest = Path.Combine(uploadsDir, safe);
                    f.SaveAs(dest);
                    savedList.Add(new { fileName = safe, size = f.ContentLength, contentType = f.ContentType });
                }
            }

            SendJson(new
            {
                status = "Multi-file Upload Completed",
                count = savedList.Count,
                files = savedList
            });
        }

        static void HandleBackgroundWorkQueue()
        {
            Interlocked.Increment(ref _bgTasksTotal);

            // Queue a background task tracked for graceful shutdown
            WebApp.BackgroundWork(ct =>
            {
                // Simulate work (e.g. email or DB sync)
                Thread.Sleep(3000);
                Interlocked.Increment(ref _bgTasksCompleted);
                Console.WriteLine($"[BackgroundWork] Task completed. ({_bgTasksCompleted}/{_bgTasksTotal})");
            });

            SendJson(new
            {
                status = "Background Task Queued",
                message = "The server tracks this task and will pause process termination during OS/IIS shutdown until complete.",
                taskTotal = _bgTasksTotal,
                taskCompleted = _bgTasksCompleted
            });
        }

        static void HandleBackgroundWorkStatus()
        {
            SendJson(new
            {
                taskTotal = _bgTasksTotal,
                taskCompleted = _bgTasksCompleted,
                pending = _bgTasksTotal - _bgTasksCompleted
            });
        }

        static void HandleHttpUtilityDemo()
        {
            string input = Request.QueryString["input"] ?? "";

            SendJson(new
            {
                original = input,
                htmlEncoded = WebApp.HtmlEncode(input),
                urlEncoded = WebApp.UrlEncode(input),
                jsStringEncoded = WebApp.JavaScriptStringEncode(input)
            });
        }

        static void SendJson(object data)
        {
            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            Response.ContentType = "application/json; charset=utf-8";
            Response.Write(json);
        }
    }
}
