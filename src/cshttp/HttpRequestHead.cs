using System;

namespace cshttp
{
    public enum HttpParseStatus
    {
        /// <summary>The buffer holds a valid prefix; read more bytes and call again with the same start.</summary>
        NeedMoreData,
        /// <summary>A complete head was parsed; <see cref="HttpParseResult.Consumed"/> bytes belong to it.</summary>
        Complete,
        /// <summary>The input is invalid; respond with <see cref="HttpParseResult.ErrorStatusCode"/> and close.</summary>
        Error
    }

    public struct HttpParseResult
    {
        public readonly HttpParseStatus Status;
        public readonly int Consumed;
        public readonly int ErrorStatusCode;
        public readonly string ErrorMessage;

        public HttpParseResult(HttpParseStatus status, int consumed, int errorStatusCode, string errorMessage)
        {
            Status = status;
            Consumed = consumed;
            ErrorStatusCode = errorStatusCode;
            ErrorMessage = errorMessage;
        }

        public static readonly HttpParseResult NeedMoreData = new HttpParseResult(HttpParseStatus.NeedMoreData, 0, 0, null);

        public static HttpParseResult Complete(int consumed) { return new HttpParseResult(HttpParseStatus.Complete, consumed, 0, null); }

        public static HttpParseResult Error(int statusCode, string message) { return new HttpParseResult(HttpParseStatus.Error, 0, statusCode, message); }

        public override string ToString()
        {
            return Status == HttpParseStatus.Error ? "Error " + ErrorStatusCode + ": " + ErrorMessage : Status.ToString();
        }
    }

    public enum HttpBodyKind
    {
        None,
        ContentLength,
        Chunked
    }

    public enum RequestTargetForm
    {
        /// <summary>"/path?query"</summary>
        Origin,
        /// <summary>"http://host/path?query" (proxy style)</summary>
        Absolute,
        /// <summary>"*" (OPTIONS only)</summary>
        Asterisk
    }

    /// <summary>The parsed request line and headers, plus the body framing decision.</summary>
    public sealed class HttpRequestHead
    {
        public string Method { get; set; }

        /// <summary>The request target exactly as sent.</summary>
        public string Target { get; set; }

        public RequestTargetForm TargetForm { get; set; }

        /// <summary>Raw (not percent-decoded) path part of the target, e.g. "/a%20b".</summary>
        public string Path { get; set; }

        /// <summary>Raw query string without the '?'. Null when the target has no '?'.</summary>
        public string Query { get; set; }

        public int VersionMajor { get; set; }
        public int VersionMinor { get; set; }

        public string Version { get { return VersionMinor == 0 ? "HTTP/1.0" : "HTTP/1.1"; } }

        public HttpHeaders Headers { get; set; }

        /// <summary>Host from an absolute-form target, else the Host header. May be empty for HTTP/1.0.</summary>
        public string Host { get; set; }

        public HttpBodyKind BodyKind { get; set; }

        /// <summary>Declared body length, 0 when none, -1 for chunked.</summary>
        public long ContentLength { get; set; }

        /// <summary>Whether the client allows the connection to stay open after this request.</summary>
        public bool KeepAlive { get; set; }

        /// <summary>The client sent "Expect: 100-continue" and waits for an interim response before the body.</summary>
        public bool ExpectContinue { get; set; }

        public bool IsHead { get { return Method == "HEAD"; } }

        public HttpRequestHead()
        {
            Headers = new HttpHeaders();
            Method = "GET";
            Target = "/";
            Path = "/";
            VersionMajor = 1;
            VersionMinor = 1;
            Host = string.Empty;
            KeepAlive = true;
        }
    }
}
