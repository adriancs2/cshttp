using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace cshttp
{
    /// <summary>
    /// Beginner-friendly entry point for starting a cshttp web application with minimal ceremony.
    /// Supports both single-port and multi-port hosting with synchronous request handlers.
    /// </summary>
    public static class WebApp
    {
        static int _port = 8080;
        static int[] _ports;
        static ServerOptions _options;

        /// <summary>Server configuration options (ContentRoot, MaxRequestBodySize, Log, etc.).</summary>
        public static ServerOptions Options
        {
            get
            {
                if (_options == null)
                {
                    _options = new ServerOptions
                    {
                        ContentRoot = AppDomain.CurrentDomain.BaseDirectory
                    };
                }
                return _options;
            }
            set { _options = value; }
        }

        /// <summary>Application root folder for MapPath and static files. Defaults to the project folder in dev or executable's directory.</summary>
        public static string ContentRoot
        {
            get
            {
                if (string.IsNullOrEmpty(Options.ContentRoot) || Options.ContentRoot == AppDomain.CurrentDomain.BaseDirectory)
                {
                    string autoRoot = AutoFindContentRoot();
                    if (!string.IsNullOrEmpty(autoRoot))
                    {
                        Options.ContentRoot = autoRoot;
                    }
                }
                return Options.ContentRoot;
            }
            set { Options.ContentRoot = value; }
        }

        static string AutoFindContentRoot()
        {
            try
            {
                var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                while (dir != null)
                {
                    if (Directory.Exists(Path.Combine(dir.FullName, "css")) && Directory.Exists(Path.Combine(dir.FullName, "js")))
                    {
                        return dir.FullName;
                    }
                    dir = dir.Parent;
                }
            }
            catch { }
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        /// <summary>
        /// Safely maps a virtual URL path (e.g. "/css/site.css") to a physical path under the application root.
        /// Returns null if the path contains traversal segments (".."), invalid characters, or escapes the root.
        /// </summary>
        public static string MapPath(string virtualPath)
        {
            if (string.IsNullOrEmpty(virtualPath)) return null;
            string root = !string.IsNullOrEmpty(_options != null ? _options.ContentRoot : null)
                ? _options.ContentRoot
                : HostingEnvironment.ApplicationPhysicalPath;
            if (virtualPath == "~" || virtualPath == "~/" || virtualPath == "/")
                return Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            return StaticFiles.SafeCombine(root, virtualPath);
        }

        /// <summary>Default HTTP port when calling WebApp.Run(OnRequest). Defaults to 8080.</summary>
        public static int Port
        {
            get { return _port; }
            set
            {
                ValidatePort(value);
                _port = value;
                _ports = new int[] { value };
            }
        }

        /// <summary>Multiple HTTP ports when calling WebApp.Run(OnRequest).</summary>
        public static int[] Ports
        {
            get { return _ports ?? new int[] { _port }; }
            set
            {
                if (value == null || value.Length == 0)
                    throw new ArgumentException("At least one port must be specified.", "value");
                for (int i = 0; i < value.Length; i++)
                    ValidatePort(value[i]);
                _ports = value;
                _port = value[0];
            }
        }

        /// <summary>Address to bind when starting via WebApp.Run. Defaults to IPAddress.Loopback (localhost).</summary>
        public static IPAddress Address { get; set; } = IPAddress.Loopback;

        /// <summary>Starts the server on WebApp.Port (or WebApp.Ports) and blocks until Ctrl+C.</summary>
        public static void Run(Action handler)
        {
            if (handler == null) throw new ArgumentNullException("handler");
            Run(Ports, handler);
        }

        /// <summary>Starts the server on the specified port and blocks until Ctrl+C.</summary>
        public static void Run(int port, Action handler)
        {
            ValidatePort(port);
            if (handler == null) throw new ArgumentNullException("handler");
            _port = port;
            _ports = new int[] { port };
            Run(new int[] { port }, handler);
        }

        /// <summary>Starts the server on two ports and blocks until Ctrl+C.</summary>
        public static void Run(int port1, int port2, Action handler)
        {
            ValidatePort(port1);
            ValidatePort(port2);
            if (handler == null) throw new ArgumentNullException("handler");
            _port = port1;
            _ports = new int[] { port1, port2 };
            Run(_ports, handler);
        }

        /// <summary>Starts the server on three ports and blocks until Ctrl+C.</summary>
        public static void Run(int port1, int port2, int port3, Action handler)
        {
            ValidatePort(port1);
            ValidatePort(port2);
            ValidatePort(port3);
            if (handler == null) throw new ArgumentNullException("handler");
            _port = port1;
            _ports = new int[] { port1, port2, port3 };
            Run(_ports, handler);
        }

        /// <summary>Starts the server on the specified ports and blocks until Ctrl+C.</summary>
        public static void Run(int[] ports, Action handler)
        {
            if (ports == null || ports.Length == 0)
                throw new ArgumentException("At least one port must be specified.", "ports");
            if (handler == null) throw new ArgumentNullException("handler");

            ServerOptions options = Options;
            if (string.IsNullOrEmpty(options.ContentRoot))
            {
                options.ContentRoot = AppDomain.CurrentDomain.BaseDirectory;
            }

            options.Bindings.Clear();
            IPAddress bindAddr = Address ?? IPAddress.Loopback;
            string hostDisplay = bindAddr.Equals(IPAddress.Any) || bindAddr.Equals(IPAddress.IPv6Any) ? "*" : "localhost";
            var urls = new List<string>();
            for (int i = 0; i < ports.Length; i++)
            {
                ValidatePort(ports[i]);
                options.ListenHttp(bindAddr, ports[i]);
                urls.Add("http://" + hostDisplay + ":" + ports[i] + "/");
            }

            Console.WriteLine("Server running at " + string.Join(", ", urls.ToArray()) + " (Press Ctrl+C to stop)");

            new HttpServer(options).Run(ctx => handler());
        }

        /// <summary>Queues a background work item that is safely tracked for graceful server shutdown.</summary>
        public static void BackgroundWork(Action<System.Threading.CancellationToken> workItem)
        {
            HostingEnvironment.QueueBackgroundWork(workItem);
        }

        /// <summary>Queues an async background work item that is safely tracked for graceful server shutdown.</summary>
        public static void BackgroundWork(Func<System.Threading.CancellationToken, System.Threading.Tasks.Task> workItem)
        {
            HostingEnvironment.QueueBackgroundWork(workItem);
        }

        static void ValidatePort(int port)
        {
            if (port < 0 || port > 65535)
                throw new ArgumentOutOfRangeException("port", "Port must be between 0 and 65535.");
        }
    }

    /// <summary>
    /// Static accessors for the current request's HttpRequest, matching classic ASP.NET Request.* syntax
    /// without requiring parameters or unpacking HttpContext.
    /// </summary>
    public static class Request
    {
        public static HttpRequest Current
        {
            get
            {
                HttpContext ctx = HttpContext.Current;
                if (ctx == null) throw new InvalidOperationException("Request is only available during an active HTTP request.");
                return ctx.Request;
            }
        }

        public static HttpRequestHead Head { get { return Current.Head; } }
        public static RequestConnectionInfo Connection { get { return Current.Connection; } }
        public static string HttpMethod { get { return Current.HttpMethod; } }
        public static string RequestType { get { return Current.RequestType; } }
        public static string Path { get { return Current.Path; } }
        public static string FilePath { get { return Current.FilePath; } }
        public static string CurrentExecutionFilePath { get { return Current.CurrentExecutionFilePath; } }
        public static string AppRelativeCurrentExecutionFilePath { get { return Current.AppRelativeCurrentExecutionFilePath; } }
        public static string PathInfo { get { return Current.PathInfo; } }
        public static string ApplicationPath { get { return Current.ApplicationPath; } }
        public static string RawUrl { get { return Current.RawUrl; } }
        public static Uri Url { get { return Current.Url; } }
        public static System.Collections.Specialized.NameValueCollection QueryString { get { return Current.QueryString; } }
        public static System.Collections.Specialized.NameValueCollection Form { get { return Current.Form; } }
        public static HttpFileCollection Files { get { return Current.Files; } }
        public static HttpCookieCollection Cookies { get { return Current.Cookies; } }
        public static System.Collections.Specialized.NameValueCollection Headers { get { return Current.Headers; } }
        public static System.Collections.Specialized.NameValueCollection ServerVariables { get { return Current.ServerVariables; } }
        public static System.Collections.Specialized.NameValueCollection Params { get { return Current.Params; } }
        public static UnvalidatedRequestValues Unvalidated { get { return Current.Unvalidated; } }
        public static string ContentType { get { return Current.ContentType; } }
        public static int ContentLength { get { return Current.ContentLength; } }
        public static int TotalBytes { get { return Current.TotalBytes; } }
        public static Encoding ContentEncoding { get { return Current.ContentEncoding; } set { Current.ContentEncoding = value; } }
        public static Stream InputStream { get { return Current.InputStream; } }
        public static byte[] BodyBytes { get { return Current.BodyBytes; } }
        public static string BodyText { get { return Current.BodyText; } }
        public static byte[] BinaryRead(int count) { return Current.BinaryRead(count); }
        public static bool IsSecureConnection { get { return Current.IsSecureConnection; } }
        public static string UserHostAddress { get { return Current.UserHostAddress; } }
        public static bool IsLocal { get { return Current.IsLocal; } }
        public static bool IsAuthenticated { get { return Current.IsAuthenticated; } }
        public static string UserAgent { get { return Current.UserAgent; } }
        public static Uri UrlReferrer { get { return Current.UrlReferrer; } }
        public static string[] AcceptTypes { get { return Current.AcceptTypes; } }
        public static string[] UserLanguages { get { return Current.UserLanguages; } }
        public static string MapPath(string virtualPath) { return Current.MapPath(virtualPath); }
        public static string Item(string key) { return Current[key]; }
    }

    /// <summary>
    /// Static accessors for the current request's HttpResponse, matching classic ASP.NET Response.* syntax
    /// without requiring parameters or unpacking HttpContext.
    /// </summary>
    public static class Response
    {
        public static HttpResponse Current
        {
            get
            {
                HttpContext ctx = HttpContext.Current;
                if (ctx == null) throw new InvalidOperationException("Response is only available during an active HTTP request.");
                return ctx.Response;
            }
        }

        public static int StatusCode { get { return Current.StatusCode; } set { Current.StatusCode = value; } }
        public static string StatusDescription { get { return Current.StatusDescription; } set { Current.StatusDescription = value; } }
        public static string Status { get { return Current.Status; } }
        public static string ContentType { get { return Current.ContentType; } set { Current.ContentType = value; } }
        public static string Charset { get { return Current.Charset; } set { Current.Charset = value; } }
        public static Encoding ContentEncoding { get { return Current.ContentEncoding; } set { Current.ContentEncoding = value; } }
        public static string CacheControl { get { return Current.CacheControl; } set { Current.CacheControl = value; } }
        public static string RedirectLocation { get { return Current.RedirectLocation; } set { Current.RedirectLocation = value; } }
        public static HttpCachePolicy Cache { get { return Current.Cache; } }
        public static HttpHeaders Headers { get { return Current.Headers; } }
        public static HttpCookieCollection Cookies { get { return Current.Cookies; } }
        public static void AppendHeader(string name, string value) { Current.AppendHeader(name, value); }
        public static void SetHeader(string name, string value) { Current.SetHeader(name, value); }
        public static void AppendCookie(HttpCookie cookie) { Current.AppendCookie(cookie); }
        public static void SetCookie(HttpCookie cookie) { Current.SetCookie(cookie); }
        public static bool BufferOutput { get { return Current.BufferOutput; } set { Current.BufferOutput = value; } }
        public static bool SuppressContent { get { return Current.SuppressContent; } set { Current.SuppressContent = value; } }
        public static bool IsRequestBeingRedirected { get { return Current.IsRequestBeingRedirected; } }
        public static bool HeadersWritten { get { return Current.HeadersWritten; } }
        public static bool IsClientConnected { get { return Current.IsClientConnected; } }
        public static bool KeepAlive { get { return Current.KeepAlive; } }
        public static bool IsCompleted { get { return Current.IsCompleted; } }
        public static TextWriter Output { get { return Current.Output; } }
        public static Stream OutputStream { get { return Current.OutputStream; } }
        public static void Write(string s) { Current.Write(s); }
        public static void Write(char ch) { Current.Write(ch); }
        public static void Write(char[] buffer, int index, int count) { Current.Write(buffer, index, count); }
        public static void Write(object obj) { Current.Write(obj); }
        public static void BinaryWrite(byte[] buffer) { Current.BinaryWrite(buffer); }
        public static void WriteBytes(byte[] buffer, int offset, int count) { Current.WriteBytes(buffer, offset, count); }
        public static bool TransmitFile(string filename) { return Current.TransmitFile(filename); }
        public static void TransmitFile(string filename, long offset, long length) { Current.TransmitFile(filename, offset, length); }
        public static void Flush() { Current.Flush(); }
        public static void Clear() { Current.Clear(); }
        public static void ClearContent() { Current.ClearContent(); }
        public static void ClearHeaders() { Current.ClearHeaders(); }
        public static void End() { Current.End(); }
        public static void Redirect(string url) { Current.Redirect(url); }
        public static void Redirect(string url, bool endResponse) { Current.Redirect(url, endResponse); }
        public static void RedirectPermanent(string url) { Current.RedirectPermanent(url); }
        public static void RedirectPermanent(string url, bool endResponse) { Current.RedirectPermanent(url, endResponse); }
        public static void Reset() { Current.Reset(); }
        public static void CompleteRequest() { HttpContext.Current.ApplicationInstance.CompleteRequest(); }


    }
}
