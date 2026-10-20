using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace cshttp
{
    /// <summary>One listening endpoint. A certificate makes it HTTPS.</summary>
    public sealed class ServerBinding
    {
        public IPAddress Address { get; set; }
        public int Port { get; set; }
        public X509Certificate2 Certificate { get; set; }
        public bool IsHttps { get { return Certificate != null; } }

        public override string ToString() { return (IsHttps ? "https://" : "http://") + Address + ":" + Port; }
    }

    public sealed class ServerOptions
    {
        public List<ServerBinding> Bindings { get; private set; }

        /// <summary>Wire-level limits (request line, header size and count, bare LF).</summary>
        public ParserOptions Parser { get; set; }

        /// <summary>Form / multipart limits.</summary>
        public RequestSettings Request { get; set; }

        /// <summary>Largest request body accepted; larger gives 413. Default 30 MB (as Kestrel).</summary>
        public long MaxRequestBodySize { get; set; }

        /// <summary>Concurrent connections; extra connections are closed immediately.</summary>
        public int MaxConnections { get; set; }

        /// <summary>0 = unlimited.</summary>
        public int MaxRequestsPerConnection { get; set; }

        /// <summary>How long an idle keep-alive connection waits for the next request.</summary>
        public TimeSpan KeepAliveTimeout { get; set; }

        /// <summary>Time allowed to receive the whole request head once it started (slowloris defence).</summary>
        public TimeSpan RequestHeadersTimeout { get; set; }

        /// <summary>Longest wait for a single read while receiving a body.</summary>
        public TimeSpan BodyReadTimeout { get; set; }

        /// <summary>Minimum average body upload rate after the grace period; slower clients get 408.</summary>
        public int MinRequestBodyBytesPerSecond { get; set; }

        public TimeSpan MinDataRateGracePeriod { get; set; }

        public TimeSpan SendTimeout { get; set; }

        public TimeSpan TlsHandshakeTimeout { get; set; }

        /// <summary>Time Stop() waits for running requests and background work.</summary>
        public TimeSpan ShutdownTimeout { get; set; }

        /// <summary>SslProtocols.None lets the operating system choose (recommended).</summary>
        public SslProtocols SslProtocols { get; set; }

        /// <summary>Peers whose X-Forwarded-For / X-Forwarded-Proto headers are believed (reverse proxies).</summary>
        public List<IPAddress> TrustedProxies { get; private set; }

        /// <summary>Server response header value; null (default) omits it.</summary>
        public string ServerHeader { get; set; }

        /// <summary>Application root for MapPath and relative static folders. Default: the executable's folder.</summary>
        public string ContentRoot { get; set; }

        /// <summary>Include exception details in 500 pages. Use only during development.</summary>
        public bool DetailedErrors { get; set; }

        /// <summary>Called for unhandled handler exceptions (in addition to the 500 page).</summary>
        public Action<HttpContext, Exception> OnUnhandledException { get; set; }

        /// <summary>Diagnostic messages from the server (accept errors, connection errors).</summary>
        public Action<string> Log { get; set; }

        public ServerOptions()
        {
            Bindings = new List<ServerBinding>();
            TrustedProxies = new List<IPAddress>();
            Parser = new ParserOptions();
            Request = new RequestSettings();
            MaxRequestBodySize = 30L * 1024 * 1024;
            MaxConnections = 1000;
            KeepAliveTimeout = TimeSpan.FromSeconds(60);
            RequestHeadersTimeout = TimeSpan.FromSeconds(30);
            BodyReadTimeout = TimeSpan.FromSeconds(30);
            MinRequestBodyBytesPerSecond = 240;
            MinDataRateGracePeriod = TimeSpan.FromSeconds(5);
            SendTimeout = TimeSpan.FromSeconds(60);
            TlsHandshakeTimeout = TimeSpan.FromSeconds(10);
            ShutdownTimeout = TimeSpan.FromSeconds(30);
            SslProtocols = SslProtocols.None;
        }

        public ServerOptions ListenHttp(int port) { return ListenHttp(IPAddress.Any, port); }

        public ServerOptions ListenHttp(IPAddress address, int port)
        {
            Bindings.Add(new ServerBinding { Address = address, Port = port });
            return this;
        }

        public ServerOptions ListenHttps(int port, X509Certificate2 certificate) { return ListenHttps(IPAddress.Any, port, certificate); }

        public ServerOptions ListenHttps(IPAddress address, int port, X509Certificate2 certificate)
        {
            if (certificate == null) throw new ArgumentNullException("certificate");
            Bindings.Add(new ServerBinding { Address = address, Port = port, Certificate = certificate });
            return this;
        }
    }
}
