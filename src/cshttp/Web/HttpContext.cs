using System;
using System.Collections;
using System.Threading;

namespace cshttp
{
    /// <summary>
    /// Per-request context with the System.Web shape. <see cref="Current"/> flows with the
    /// executing thread / async flow (AsyncLocal), and is set by the server around each handler call.
    /// </summary>
    public sealed class HttpContext
    {
        static readonly AsyncLocal<HttpContext> CurrentContext = new AsyncLocal<HttpContext>();

        public static HttpContext Current
        {
            get { return CurrentContext.Value; }
            set { CurrentContext.Value = value; }
        }

        readonly HttpRequest _request;
        readonly HttpResponse _response;
        Hashtable _items;
        HttpApplication _application;
        HttpServerUtility _server;

        public HttpContext(HttpRequest request, HttpResponse response)
        {
            if (request == null) throw new ArgumentNullException("request");
            if (response == null) throw new ArgumentNullException("response");
            _request = request;
            _response = response;
            Timestamp = DateTime.Now;
        }

        public HttpRequest Request { get { return _request; } }

        public HttpResponse Response { get { return _response; } }

        /// <summary>Per-request storage that lives for this request only.</summary>
        public IDictionary Items { get { return _items ?? (_items = new Hashtable()); } }

        public DateTime Timestamp { get; private set; }

        public HttpApplication ApplicationInstance { get { return _application ?? (_application = new HttpApplication(this)); } }

        public HttpServerUtility Server { get { return _server ?? (_server = new HttpServerUtility()); } }

        /// <summary>True after ApplicationInstance.CompleteRequest() was called.</summary>
        public bool IsRequestCompleted { get; internal set; }
    }

    /// <summary>Minimal stand-in for System.Web.HttpApplication: only what handlers call on ApplicationInstance.</summary>
    public sealed class HttpApplication
    {
        readonly HttpContext _context;

        internal HttpApplication(HttpContext context) { _context = context; }

        public HttpContext Context { get { return _context; } }
        public HttpRequest Request { get { return _context.Request; } }
        public HttpResponse Response { get { return _context.Response; } }

        /// <summary>Marks the request as complete. The response is finished when the handler returns.</summary>
        public void CompleteRequest() { _context.IsRequestCompleted = true; }
    }

    public sealed class HttpServerUtility
    {
        public string MachineName { get { return Environment.MachineName; } }
        public int ScriptTimeout { get; set; }
        public string MapPath(string path) { return HostingEnvironment.MapPath(path); }
        public string HtmlEncode(string s) { return HttpUtility.HtmlEncode(s); }
        public string HtmlDecode(string s) { return HttpUtility.HtmlDecode(s); }
        public string UrlEncode(string s) { return HttpUtility.UrlEncode(s); }
        public string UrlDecode(string s) { return HttpUtility.UrlDecode(s); }
        public string UrlPathEncode(string s) { return HttpUtility.UrlPathEncode(s); }
    }
}
