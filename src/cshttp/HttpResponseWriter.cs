using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace cshttp
{
    public static class HttpStatus
    {
        static readonly Dictionary<int, string> Phrases = new Dictionary<int, string>
        {
            { 100, "Continue" }, { 101, "Switching Protocols" },
            { 200, "OK" }, { 201, "Created" }, { 202, "Accepted" }, { 203, "Non-Authoritative Information" },
            { 204, "No Content" }, { 205, "Reset Content" }, { 206, "Partial Content" },
            { 300, "Multiple Choices" }, { 301, "Moved Permanently" }, { 302, "Found" }, { 303, "See Other" },
            { 304, "Not Modified" }, { 307, "Temporary Redirect" }, { 308, "Permanent Redirect" },
            { 400, "Bad Request" }, { 401, "Unauthorized" }, { 402, "Payment Required" }, { 403, "Forbidden" },
            { 404, "Not Found" }, { 405, "Method Not Allowed" }, { 406, "Not Acceptable" }, { 408, "Request Timeout" },
            { 409, "Conflict" }, { 410, "Gone" }, { 411, "Length Required" }, { 412, "Precondition Failed" },
            { 413, "Content Too Large" }, { 414, "URI Too Long" }, { 415, "Unsupported Media Type" },
            { 416, "Range Not Satisfiable" }, { 417, "Expectation Failed" }, { 421, "Misdirected Request" },
            { 422, "Unprocessable Content" }, { 426, "Upgrade Required" }, { 428, "Precondition Required" },
            { 429, "Too Many Requests" }, { 431, "Request Header Fields Too Large" },
            { 500, "Internal Server Error" }, { 501, "Not Implemented" }, { 502, "Bad Gateway" },
            { 503, "Service Unavailable" }, { 504, "Gateway Timeout" }, { 505, "HTTP Version Not Supported" }
        };

        public static string GetReasonPhrase(int statusCode)
        {
            string s;
            return Phrases.TryGetValue(statusCode, out s) ? s : (statusCode >= 100 && statusCode < 600 ? "Status " + statusCode : "Unknown");
        }

        /// <summary>1xx, 204 and 304 responses never carry a body or body framing.</summary>
        public static bool ForbidsBody(int statusCode)
        {
            return (statusCode >= 100 && statusCode < 200) || statusCode == 204 || statusCode == 304;
        }
    }

    public static class HttpDate
    {
        static readonly string[] Formats =
        {
            "r",
            "ddd, dd MMM yyyy HH':'mm':'ss 'GMT'",
            "dddd, dd-MMM-yy HH':'mm':'ss 'GMT'",
            "ddd MMM d HH':'mm':'ss yyyy",
            "ddd MMM  d HH':'mm':'ss yyyy"
        };

        /// <summary>IMF-fixdate, e.g. "Sun, 06 Nov 1994 08:49:37 GMT".</summary>
        public static string Format(DateTime value)
        {
            return value.ToUniversalTime().ToString("r", CultureInfo.InvariantCulture);
        }

        public static bool TryParse(string value, out DateTime utc)
        {
            if (string.IsNullOrEmpty(value)) { utc = DateTime.MinValue; return false; }
            return DateTime.TryParseExact(value.Trim(), Formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out utc);
        }
    }

    /// <summary>Serializes response heads and chunk framing. Refuses header injection.</summary>
    public static class HttpResponseWriter
    {
        public static readonly byte[] Crlf = { 13, 10 };
        public static readonly byte[] LastChunk = { (byte)'0', 13, 10, 13, 10 };
        public static readonly byte[] Continue100 = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");

        public static void ValidateHeader(string name, string value)
        {
            if (!HttpChars.IsToken(name)) throw new ArgumentException("Invalid header name: " + name);
            if (!HttpChars.IsSafeFieldValue(value)) throw new ArgumentException("Header value for '" + name + "' contains control characters (CR/LF injection).");
        }

        /// <summary>Builds "HTTP/1.1 code reason\r\n" + headers + "\r\n" as Latin-1 bytes.</summary>
        public static byte[] BuildHead(int statusCode, string reasonPhrase, HttpHeaders headers)
        {
            if (statusCode < 100 || statusCode > 999) throw new ArgumentOutOfRangeException("statusCode");
            string reason = string.IsNullOrEmpty(reasonPhrase) ? HttpStatus.GetReasonPhrase(statusCode) : reasonPhrase;
            if (!HttpChars.IsSafeFieldValue(reason)) throw new ArgumentException("Reason phrase contains control characters.");
            var sb = new StringBuilder(256);
            sb.Append("HTTP/1.1 ").Append(statusCode.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(reason).Append("\r\n");
            if (headers != null)
            {
                foreach (HttpHeader h in headers)
                {
                    ValidateHeader(h.Name, h.Value);
                    sb.Append(h.Name).Append(": ").Append(h.Value).Append("\r\n");
                }
            }
            sb.Append("\r\n");
            return ByteScan.Latin1Bytes(sb.ToString());
        }

        /// <summary>Writes "{hex length}\r\n" into dest and returns the number of bytes written (max 18).</summary>
        public static int WriteChunkHeader(byte[] dest, int offset, long length)
        {
            if (length <= 0) throw new ArgumentOutOfRangeException("length");
            string hex = length.ToString("x", CultureInfo.InvariantCulture);
            for (int i = 0; i < hex.Length; i++) dest[offset + i] = (byte)hex[i];
            dest[offset + hex.Length] = 13;
            dest[offset + hex.Length + 1] = 10;
            return hex.Length + 2;
        }

        /// <summary>Writes one complete chunk (header, data, CRLF). Zero-length input writes nothing.</summary>
        public static void WriteChunk(Stream stream, byte[] data, int offset, int count)
        {
            if (count <= 0) return;
            byte[] header = new byte[20];
            int n = WriteChunkHeader(header, 0, count);
            stream.Write(header, 0, n);
            stream.Write(data, offset, count);
            stream.Write(Crlf, 0, 2);
        }
    }
}
