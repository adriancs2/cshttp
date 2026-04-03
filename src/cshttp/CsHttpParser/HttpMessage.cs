using System.Collections.Generic;

namespace CsHttp
{
    /// <summary>
    /// Base class for a parsed HTTP/1.1 message.
    /// 
    /// Per RFC 9112 Section 2.1, both request and response share the same
    /// structure: start-line CRLF *(field-line CRLF) CRLF [message-body]
    /// They differ only in the start-line format and body-length determination.
    /// </summary>
    public abstract class HttpMessage
    {
        /// <summary>
        /// The HTTP version as received (e.g., "HTTP/1.1", "HTTP/1.0").
        /// Per RFC 9112 Section 2.3, this is case-sensitive.
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// The major version number extracted from the HTTP-version field.
        /// </summary>
        public int VersionMajor { get; set; }

        /// <summary>
        /// The minor version number extracted from the HTTP-version field.
        /// </summary>
        public int VersionMinor { get; set; }

        /// <summary>
        /// The header field collection, preserving wire order with case-insensitive keyed access.
        /// </summary>
        public HttpHeaderCollection Headers { get; set; }

        /// <summary>
        /// The decoded message body content (after chunked decoding if applicable).
        /// Null if no message body is present.
        /// </summary>
        public byte[] Body { get; set; }

        /// <summary>
        /// The raw message body as received on the wire (before chunked decoding).
        /// For Content-Length framed messages, this is identical to Body.
        /// For chunked messages, this is the raw chunked stream; Body is the decoded content.
        /// Null if no message body is present.
        /// </summary>
        public byte[] RawBody { get; set; }

        /// <summary>
        /// Trailer fields received after a chunked message body.
        /// Empty if no trailers were present.
        /// Per RFC 9112 Section 7.1.2.
        /// </summary>
        public HttpHeaderCollection Trailers { get; set; }

        /// <summary>
        /// How the message body length was determined.
        /// Maps to the 8 rules in RFC 9112 Section 6.3.
        /// </summary>
        public BodyFrameKind BodyFraming { get; set; }

        /// <summary>
        /// The raw bytes of the complete start-line as received.
        /// Available for security auditing.
        /// </summary>
        public byte[] RawStartLine { get; set; }

        /// <summary>
        /// Total number of bytes consumed from the input to parse this message.
        /// Useful for pipelined request parsing where multiple messages 
        /// are concatenated in a single byte stream.
        /// </summary>
        public int BytesConsumed { get; set; }

        protected HttpMessage()
        {
            Headers = new HttpHeaderCollection();
            Trailers = new HttpHeaderCollection();
        }
    }

    /// <summary>
    /// A parsed HTTP/1.1 request message.
    /// 
    /// RFC 9112 Section 3:
    ///   request-line = method SP request-target SP HTTP-version
    /// </summary>
    public sealed class HttpRequestMessage : HttpMessage
    {
        /// <summary>
        /// The request method token (case-sensitive).
        /// e.g., "GET", "POST", "DELETE".
        /// </summary>
        public string Method { get; set; }

        /// <summary>
        /// The request-target as received (not decoded or normalized).
        /// e.g., "/api/users?page=2", "http://example.com/path", "*"
        /// </summary>
        public string RequestTarget { get; set; }

        /// <summary>
        /// The form of the request-target as determined during parsing.
        /// </summary>
        public RequestTargetForm RequestTargetForm { get; set; }
    }

    /// <summary>
    /// A parsed HTTP/1.1 response message.
    /// 
    /// RFC 9112 Section 4:
    ///   status-line = HTTP-version SP status-code SP [reason-phrase]
    /// </summary>
    public sealed class HttpResponseMessage : HttpMessage
    {
        /// <summary>
        /// The 3-digit status code as an integer.
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// The reason phrase as received. May be empty.
        /// Per RFC 9112 Section 4, clients SHOULD ignore this.
        /// </summary>
        public string ReasonPhrase { get; set; }
    }

    /// <summary>
    /// Identifies how the message body length was determined.
    /// Per RFC 9112 Section 6.3 (rules 1-8).
    /// </summary>
    public enum BodyFrameKind
    {
        /// <summary>No body present.</summary>
        None,

        /// <summary>Rule 1: HEAD response or 1xx/204/304 — no body possible.</summary>
        NoBodyByStatus,

        /// <summary>Rule 2: 2xx response to CONNECT — tunnel follows.</summary>
        Tunnel,

        /// <summary>Rule 4: Chunked transfer coding.</summary>
        Chunked,

        /// <summary>Rule 6: Content-Length specifies exact byte count.</summary>
        ContentLength,

        /// <summary>Rule 7: Request with no Content-Length or Transfer-Encoding — zero body.</summary>
        ZeroByAbsence,

        /// <summary>Rule 8: Response with no framing — read until connection close.</summary>
        UntilClose,
    }

    /// <summary>
    /// The four forms of request-target per RFC 9112 Section 3.2.
    /// </summary>
    public enum RequestTargetForm
    {
        /// <summary>Section 3.2.1: absolute-path ["?" query]</summary>
        Origin,

        /// <summary>Section 3.2.2: absolute-URI</summary>
        Absolute,

        /// <summary>Section 3.2.3: uri-host ":" port (CONNECT only)</summary>
        Authority,

        /// <summary>Section 3.2.4: "*" (server-wide OPTIONS only)</summary>
        Asterisk,
    }
}
