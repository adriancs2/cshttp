using System;
using System.Collections.Specialized;
using System.Net;
using System.Text;

namespace cshttp
{
    /// <summary>An exception that carries an HTTP status code (same shape as System.Web.HttpException).</summary>
    public class HttpException : Exception
    {
        readonly int _statusCode;

        public HttpException(string message) : this(500, message) { }

        public HttpException(int httpCode, string message) : base(message) { _statusCode = httpCode; }

        public HttpException(int httpCode, string message, Exception innerException) : base(message, innerException) { _statusCode = httpCode; }

        public int GetHttpCode() { return _statusCode; }

        public int StatusCode { get { return _statusCode; } }
    }

    /// <summary>
    /// Thrown by <see cref="HttpResponse.End"/> to stop the handler. The server catches it silently.
    /// Unlike System.Web's ThreadAbortException it can be swallowed by a catch-all, so after End()
    /// the response also ignores any further writes.
    /// </summary>
    public sealed class HttpResponseEndException : Exception
    {
        public HttpResponseEndException() : base("Response.End() was called.") { }
    }

    /// <summary>Limits applied while interpreting request bodies.</summary>
    public sealed class RequestSettings
    {
        /// <summary>Most form/query keys accepted (System.Web default: 1000). More gives 400.</summary>
        public int MaxCollectionKeys { get; set; }

        /// <summary>Most multipart parts accepted.</summary>
        public int MaxMultipartParts { get; set; }

        /// <summary>Largest header block of one multipart part.</summary>
        public int MaxMultipartHeaderBytes { get; set; }

        public RequestSettings()
        {
            MaxCollectionKeys = 1000;
            MaxMultipartParts = 1000;
            MaxMultipartHeaderBytes = 16 * 1024;
        }

        internal static readonly RequestSettings Shared = new RequestSettings();
    }

    /// <summary>Network facts about the request, supplied by the server (after trusted-proxy rules).</summary>
    public sealed class RequestConnectionInfo
    {
        public IPAddress RemoteAddress { get; set; }
        public int RemotePort { get; set; }
        public IPAddress LocalAddress { get; set; }
        public int LocalPort { get; set; }
        public bool IsSecure { get; set; }

        /// <summary>The socket peer before X-Forwarded-For was applied.</summary>
        public IPAddress PeerAddress { get; set; }

        public RequestConnectionInfo()
        {
            RemoteAddress = IPAddress.Loopback;
            LocalAddress = IPAddress.Loopback;
            PeerAddress = IPAddress.Loopback;
        }

        public static IPAddress Normalize(IPAddress address)
        {
            if (address != null && address.IsIPv4MappedToIPv6) return address.MapToIPv4();
            return address;
        }
    }

    /// <summary>NameValueCollection that becomes read-only once filled (like System.Web's Request collections).</summary>
    public class HttpValueCollection : NameValueCollection
    {
        public HttpValueCollection() : base(StringComparer.OrdinalIgnoreCase) { }

        public void MakeReadOnly() { IsReadOnly = true; }

        /// <summary>Re-encodes the collection as a query string ("a=1&amp;b=2"), like System.Web.</summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Count; i++)
            {
                string key = GetKey(i);
                string[] values = GetValues(i);
                string encodedKey = key == null ? null : WebApp.UrlEncode(key);
                if (values == null || values.Length == 0)
                {
                    if (sb.Length > 0) sb.Append('&');
                    if (encodedKey != null) sb.Append(encodedKey).Append('=');
                    continue;
                }
                foreach (string v in values)
                {
                    if (sb.Length > 0) sb.Append('&');
                    if (encodedKey != null) sb.Append(encodedKey).Append('=');
                    sb.Append(WebApp.UrlEncode(v));
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>Helpers for structured header values: media types and ;-separated parameters.</summary>
    public static class HeaderValue
    {
        /// <summary>"text/html; charset=utf-8" gives "text/html" (lower-case). Null input gives "".</summary>
        public static string GetMediaType(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            int semi = value.IndexOf(';');
            return (semi < 0 ? value : value.Substring(0, semi)).Trim().ToLowerInvariant();
        }

        /// <summary>Returns a parameter value (quotes removed, \-escapes resolved), or null.</summary>
        public static string GetParameter(string value, string name)
        {
            if (string.IsNullOrEmpty(value)) return null;
            int i = value.IndexOf(';');
            while (i >= 0 && i < value.Length)
            {
                i++;
                while (i < value.Length && (value[i] == ' ' || value[i] == '\t')) i++;
                int nameStart = i;
                while (i < value.Length && value[i] != '=' && value[i] != ';') i++;
                string pname = value.Substring(nameStart, i - nameStart).Trim();
                string pvalue = null;
                if (i < value.Length && value[i] == '=')
                {
                    i++;
                    while (i < value.Length && (value[i] == ' ' || value[i] == '\t')) i++;
                    if (i < value.Length && value[i] == '"')
                    {
                        var sb = new StringBuilder();
                        i++;
                        while (i < value.Length && value[i] != '"')
                        {
                            if (value[i] == '\\' && i + 1 < value.Length) i++;
                            sb.Append(value[i]);
                            i++;
                        }
                        pvalue = sb.ToString();
                        while (i < value.Length && value[i] != ';') i++;
                    }
                    else
                    {
                        int vStart = i;
                        while (i < value.Length && value[i] != ';') i++;
                        pvalue = value.Substring(vStart, i - vStart).Trim();
                    }
                }
                if (string.Equals(pname, name, StringComparison.OrdinalIgnoreCase)) return pvalue ?? string.Empty;
                if (i >= value.Length) break;
            }
            return null;
        }

        /// <summary>Decodes an RFC 8187 ext-value such as UTF-8''na%C3%AFve.txt. Returns null if malformed.</summary>
        public static string DecodeExtValue(string extValue)
        {
            if (extValue == null) return null;
            int q1 = extValue.IndexOf('\'');
            if (q1 < 0) return null;
            int q2 = extValue.IndexOf('\'', q1 + 1);
            if (q2 < 0) return null;
            string charset = extValue.Substring(0, q1);
            Encoding enc;
            try { enc = Encoding.GetEncoding(charset); }
            catch (ArgumentException) { enc = Encoding.UTF8; }
            return PercentDecoder.Decode(extValue.Substring(q2 + 1), false, enc);
        }
    }
}
