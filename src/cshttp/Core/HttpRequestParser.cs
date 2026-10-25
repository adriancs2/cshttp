using System;
using System.Collections.Generic;

namespace cshttp
{
    /// <summary>
    /// Strict, resumable HTTP/1.x request-head parser.
    ///
    /// Feed it everything received so far for the current request (from the same start
    /// offset each time). It returns NeedMoreData until the blank line ending the head
    /// arrives, so partial socket reads are fine. Nothing is allocated until the whole
    /// head is present. Errors carry the status code a server should answer with.
    ///
    /// Smuggling defences: CRLF required (bare LF optional), bare CR rejected, obs-fold
    /// rejected, whitespace before ':' rejected, Content-Length + Transfer-Encoding
    /// rejected, conflicting Content-Length rejected, final coding must be chunked,
    /// exactly one Host in HTTP/1.1.
    /// </summary>
    public static class HttpRequestParser
    {
        const byte CR = 13, LF = 10, SP = 32;
        static readonly char[] PathOrQuery = { '/', '?' };

        public static HttpParseResult ParseHead(byte[] buffer, int offset, int count, ParserOptions options, out HttpRequestHead head)
        {
            head = null;
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException("count");
            ParserOptions opt = options ?? ParserOptions.Shared;
            int end = offset + count;

            // RFC 9112 2.2: ignore at least one empty line before the request line.
            int pos = offset;
            int empties = 0;
            while (true)
            {
                if (pos >= end) return HttpParseResult.NeedMoreData;
                byte b = buffer[pos];
                if (b == CR)
                {
                    if (pos + 1 >= end) return HttpParseResult.NeedMoreData;
                    if (buffer[pos + 1] != LF) return HttpParseResult.Error(400, "Bare CR in request.");
                    pos += 2;
                }
                else if (b == LF)
                {
                    if (!opt.AllowBareLf) return HttpParseResult.Error(400, "Bare LF line terminator.");
                    pos++;
                }
                else break;
                if (++empties > 8) return HttpParseResult.Error(400, "Too many empty lines before the request line.");
            }

            int headStart = pos;
            int available = end - headStart;

            // Locate the request line end without scanning past its limit.
            int firstLf = ByteScan.IndexOf(buffer, headStart, Math.Min(available, opt.MaxRequestLineLength + 2), LF);
            if (firstLf < 0)
            {
                if (available > opt.MaxRequestLineLength + 1) return HttpParseResult.Error(414, "Request line too long.");
                return HttpParseResult.NeedMoreData;
            }

            // Locate the blank line that ends the head.
            int headEnd;
            int maxEnd = headStart + opt.MaxHeaderBytes;
            int p = firstLf + 1;
            while (true)
            {
                if (p >= end) return NeedMoreOrTooLarge(end, maxEnd);
                byte c = buffer[p];
                if (c == LF) { headEnd = p + 1; break; }
                if (c == CR)
                {
                    if (p + 1 >= end) return NeedMoreOrTooLarge(end, maxEnd);
                    if (buffer[p + 1] == LF) { headEnd = p + 2; break; }
                }
                int searchLen = Math.Min(end, maxEnd) - p;
                int lf = searchLen > 0 ? ByteScan.IndexOf(buffer, p, searchLen, LF) : -1;
                if (lf < 0) return NeedMoreOrTooLarge(end, maxEnd);
                p = lf + 1;
            }
            if (headEnd > maxEnd) return HttpParseResult.Error(431, "Request headers too large.");

            // ---- Request line ----
            int lineEnd;
            string err = GetLineEnd(buffer, headStart, firstLf, opt.AllowBareLf, out lineEnd);
            if (err != null) return HttpParseResult.Error(400, err);
            int lineLen = lineEnd - headStart;
            if (lineLen > opt.MaxRequestLineLength) return HttpParseResult.Error(414, "Request line too long.");

            int sp1 = ByteScan.IndexOf(buffer, headStart, lineLen, SP);
            if (sp1 <= headStart) return HttpParseResult.Error(400, "Malformed request line.");
            for (int i = headStart; i < sp1; i++)
                if (!HttpChars.IsToken(buffer[i])) return HttpParseResult.Error(400, "Invalid method.");

            int tStart = sp1 + 1;
            int sp2 = ByteScan.IndexOf(buffer, tStart, lineEnd - tStart, SP);
            if (sp2 < 0 || sp2 == tStart) return HttpParseResult.Error(400, "Malformed request line.");
            for (int i = tStart; i < sp2; i++)
                if (!HttpChars.IsTargetByte(buffer[i])) return HttpParseResult.Error(400, "Invalid character in request target.");

            int major, minor;
            int versionStatus = ParseVersion(buffer, sp2 + 1, lineEnd - sp2 - 1, out major, out minor);
            if (versionStatus != 0) return HttpParseResult.Error(versionStatus, versionStatus == 505 ? "HTTP version not supported." : "Malformed HTTP version.");

            string method = KnownMethod(buffer, headStart, sp1 - headStart) ?? ByteScan.Latin1(buffer, headStart, sp1 - headStart);
            string target = ByteScan.Latin1(buffer, tStart, sp2 - tStart);
            if (target.IndexOf('#') >= 0) return HttpParseResult.Error(400, "Fragment in request target.");

            string path, query = null, authority = null;
            RequestTargetForm form;
            if (target[0] == '/')
            {
                form = RequestTargetForm.Origin;
                SplitPathQuery(target, out path, out query);
            }
            else if (target == "*")
            {
                if (method != "OPTIONS") return HttpParseResult.Error(400, "Asterisk target is only valid for OPTIONS.");
                form = RequestTargetForm.Asterisk;
                path = "*";
            }
            else if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                int schemeEnd = target.IndexOf("://", StringComparison.Ordinal) + 3;
                int slash = target.IndexOfAny(PathOrQuery, schemeEnd);
                authority = slash < 0 ? target.Substring(schemeEnd) : target.Substring(schemeEnd, slash - schemeEnd);
                if (authority.Length == 0 || authority.IndexOf('@') >= 0) return HttpParseResult.Error(400, "Invalid authority in request target.");
                string rest = slash < 0 ? "/" : target.Substring(slash);
                if (rest[0] == '?') rest = "/" + rest;
                SplitPathQuery(rest, out path, out query);
                form = RequestTargetForm.Absolute;
            }
            else if (method == "CONNECT")
            {
                return HttpParseResult.Error(501, "CONNECT is not supported.");
            }
            else
            {
                return HttpParseResult.Error(400, "Invalid request target.");
            }

            // ---- Header lines ----
            var headers = new HttpHeaders();
            int lineStart = firstLf + 1;
            while (true)
            {
                int lf = ByteScan.IndexOf(buffer, lineStart, headEnd - lineStart, LF); // always found
                err = GetLineEnd(buffer, lineStart, lf, opt.AllowBareLf, out lineEnd);
                if (err != null) return HttpParseResult.Error(400, err);
                if (lineEnd == lineStart) break; // blank line: end of head

                byte f = buffer[lineStart];
                if (f == SP || f == 9) return HttpParseResult.Error(400, "Obsolete line folding is not allowed.");

                int colon = ByteScan.IndexOf(buffer, lineStart, lineEnd - lineStart, (byte)':');
                if (colon <= lineStart) return HttpParseResult.Error(400, "Malformed header line.");
                for (int i = lineStart; i < colon; i++)
                    if (!HttpChars.IsToken(buffer[i])) return HttpParseResult.Error(400, "Invalid header name.");

                int vs = colon + 1, ve = lineEnd;
                while (vs < ve && (buffer[vs] == SP || buffer[vs] == 9)) vs++;
                while (ve > vs && (buffer[ve - 1] == SP || buffer[ve - 1] == 9)) ve--;
                for (int i = vs; i < ve; i++)
                    if (!HttpChars.IsFieldValueByte(buffer[i])) return HttpParseResult.Error(400, "Invalid character in header value.");

                if (headers.Count >= opt.MaxHeaderCount) return HttpParseResult.Error(431, "Too many request headers.");
                string name = KnownHeaders.Find(buffer, lineStart, colon - lineStart) ?? ByteScan.Latin1(buffer, lineStart, colon - lineStart);
                headers.Add(name, ByteScan.Latin1(buffer, vs, ve - vs));
                lineStart = lf + 1;
            }

            // ---- Semantics ----
            int hostCount = headers.CountOf("Host");
            if (hostCount > 1) return HttpParseResult.Error(400, "Multiple Host headers.");
            if (minor >= 1 && hostCount == 0) return HttpParseResult.Error(400, "Missing Host header.");
            string host = authority ?? headers.Get("Host") ?? string.Empty;
            if (!IsValidHost(host)) return HttpParseResult.Error(400, "Invalid Host.");

            HttpBodyKind kind = HttpBodyKind.None;
            long length = 0;
            List<string> te = headers.GetAll("Transfer-Encoding");
            bool hasCl = headers.Contains("Content-Length");
            if (te.Count > 0)
            {
                if (minor == 0) return HttpParseResult.Error(400, "Transfer-Encoding is not allowed in HTTP/1.0 requests.");
                if (hasCl) return HttpParseResult.Error(400, "Both Transfer-Encoding and Content-Length are present.");
                List<string> codings = SplitList(te);
                if (codings.Count == 0) return HttpParseResult.Error(400, "Empty Transfer-Encoding.");
                if (!string.Equals(codings[codings.Count - 1], "chunked", StringComparison.OrdinalIgnoreCase))
                    return HttpParseResult.Error(400, "The final transfer coding must be chunked.");
                if (codings.Count > 1)
                {
                    for (int i = 0; i < codings.Count - 1; i++)
                        if (string.Equals(codings[i], "chunked", StringComparison.OrdinalIgnoreCase))
                            return HttpParseResult.Error(400, "chunked applied more than once.");
                    return HttpParseResult.Error(501, "Unsupported transfer coding.");
                }
                kind = HttpBodyKind.Chunked;
                length = -1;
            }
            else if (hasCl)
            {
                long declared = -1;
                foreach (string part in SplitList(headers.GetAll("Content-Length")))
                {
                    long v;
                    if (!TryParseDigits(part, out v)) return HttpParseResult.Error(400, "Invalid Content-Length.");
                    if (declared >= 0 && v != declared) return HttpParseResult.Error(400, "Conflicting Content-Length values.");
                    declared = v;
                }
                if (declared < 0) return HttpParseResult.Error(400, "Invalid Content-Length.");
                length = declared;
                kind = declared > 0 ? HttpBodyKind.ContentLength : HttpBodyKind.None;
            }

            bool close = false, keepAliveToken = false;
            foreach (string token in SplitList(headers.GetAll("Connection")))
            {
                if (string.Equals(token, "close", StringComparison.OrdinalIgnoreCase)) close = true;
                else if (string.Equals(token, "keep-alive", StringComparison.OrdinalIgnoreCase)) keepAliveToken = true;
            }

            bool expectContinue = false;
            string expect = headers.Get("Expect");
            if (expect != null)
            {
                if (string.Equals(expect.Trim(), "100-continue", StringComparison.OrdinalIgnoreCase)) expectContinue = minor >= 1;
                else return HttpParseResult.Error(417, "Unsupported expectation.");
            }

            head = new HttpRequestHead
            {
                Method = method,
                Target = target,
                TargetForm = form,
                Path = path,
                Query = query,
                VersionMajor = major,
                VersionMinor = minor,
                Headers = headers,
                Host = host,
                BodyKind = kind,
                ContentLength = length,
                KeepAlive = minor >= 1 ? !close : (keepAliveToken && !close),
                ExpectContinue = expectContinue
            };
            return HttpParseResult.Complete(headEnd - offset);
        }

        static HttpParseResult NeedMoreOrTooLarge(int end, int maxEnd)
        {
            return end > maxEnd ? HttpParseResult.Error(431, "Request headers too large.") : HttpParseResult.NeedMoreData;
        }

        static string GetLineEnd(byte[] b, int lineStart, int lf, bool allowBareLf, out int lineEnd)
        {
            if (lf > lineStart && b[lf - 1] == CR) lineEnd = lf - 1;
            else
            {
                lineEnd = lf;
                if (!allowBareLf) return "Bare LF line terminator.";
            }
            if (lineEnd > lineStart && ByteScan.IndexOf(b, lineStart, lineEnd - lineStart, CR) >= 0) return "Bare CR in request.";
            return null;
        }

        /// <summary>0 = ok, 400 = malformed, 505 = unsupported major version.</summary>
        static int ParseVersion(byte[] b, int start, int len, out int major, out int minor)
        {
            major = minor = 0;
            if (len != 8) return 400;
            if (b[start] != 'H' || b[start + 1] != 'T' || b[start + 2] != 'T' || b[start + 3] != 'P' || b[start + 4] != '/') return 400;
            byte ma = b[start + 5], dot = b[start + 6], mi = b[start + 7];
            if (ma < '0' || ma > '9' || dot != '.' || mi < '0' || mi > '9') return 400;
            major = ma - '0';
            if (major != 1) return 505;
            minor = mi == '0' ? 0 : 1; // HTTP/1.2+ is answered as 1.1 (RFC 9110 2.5)
            return 0;
        }

        static string KnownMethod(byte[] b, int start, int len)
        {
            switch (len)
            {
                case 3:
                    if (ByteScan.EqualsAscii(b, start, len, "GET")) return "GET";
                    if (ByteScan.EqualsAscii(b, start, len, "PUT")) return "PUT";
                    break;
                case 4:
                    if (ByteScan.EqualsAscii(b, start, len, "POST")) return "POST";
                    if (ByteScan.EqualsAscii(b, start, len, "HEAD")) return "HEAD";
                    break;
                case 5:
                    if (ByteScan.EqualsAscii(b, start, len, "PATCH")) return "PATCH";
                    break;
                case 6:
                    if (ByteScan.EqualsAscii(b, start, len, "DELETE")) return "DELETE";
                    break;
                case 7:
                    if (ByteScan.EqualsAscii(b, start, len, "OPTIONS")) return "OPTIONS";
                    if (ByteScan.EqualsAscii(b, start, len, "CONNECT")) return "CONNECT";
                    break;
            }
            return null;
        }

        static void SplitPathQuery(string target, out string path, out string query)
        {
            int q = target.IndexOf('?');
            if (q < 0) { path = target; query = null; }
            else { path = target.Substring(0, q); query = target.Substring(q + 1); }
        }

        static bool IsValidHost(string host)
        {
            for (int i = 0; i < host.Length; i++)
            {
                char c = host[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) continue;
                if ("-._~!$&'()*+,;=:[]%".IndexOf(c) >= 0) continue;
                return false;
            }
            return true;
        }

        /// <summary>Splits comma-separated list values, trimming whitespace and dropping empty elements.</summary>
        public static List<string> SplitList(List<string> values)
        {
            var result = new List<string>();
            foreach (string v in values)
            {
                foreach (string part in v.Split(','))
                {
                    string t = part.Trim(' ', '\t');
                    if (t.Length > 0) result.Add(t);
                }
            }
            return result;
        }

        static bool TryParseDigits(string s, out long value)
        {
            value = 0;
            if (s.Length == 0 || s.Length > 18) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9') return false;
                value = value * 10 + (c - '0');
            }
            return true;
        }
    }
}
