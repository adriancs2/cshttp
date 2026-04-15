using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CsHttp
{
    internal class Program
    {
        private static int _passed = 0;
        private static int _failed = 0;

        static void Main(string[] args)
        {
            Console.WriteLine("cshttp Parser Tests — RFC 9112 Compliance");
            Console.WriteLine("==========================================");
            Console.WriteLine();

            // Section 2.1 — Message Format
            Test_BasicGetRequest();
            Test_BasicPostRequestWithBody();

            // Section 2.2 — Message Parsing
            Test_LeadingEmptyLinesIgnored();
            Test_BareLFTolerated();
            Test_BareCRInHeaderRejectedStrict();
            Test_WhitespaceAfterStartLineRejectedStrict();
            Test_WhitespaceAfterStartLineConsumedLenient();

            // Section 2.3 — HTTP Version
            Test_HttpVersionValidation();
            Test_Http10Version();

            // Section 3 — Request Line
            Test_RequestLineParsing();
            Test_RequestLineMethodCaseSensitive();

            // Section 3.2 — Request Target Forms
            Test_OriginForm();
            Test_AbsoluteForm();
            Test_AuthorityForm();
            Test_AsteriskForm();

            // Section 4 — Status Line
            Test_BasicStatusLine();
            Test_StatusLineNoReasonPhrase();
            Test_StatusLine404();

            // Section 5 — Field Syntax
            Test_HeaderParsing();
            Test_HeaderCaseInsensitiveAccess();
            Test_MultipleHeadersSameName();
            Test_WhitespaceBeforeColonRejected();
            Test_HeaderOWSTrimming();

            // Section 5.2 — Obsolete Line Folding
            Test_ObsLineFoldingRejected();
            Test_ObsLineFoldingReplacedLenient();

            // Section 6 — Message Body
            Test_ContentLengthBody();
            Test_ZeroContentLength();
            Test_NoBodyOnRequest();

            // Section 6.3 — Message Body Length
            Test_ResponseNoBodyOnHead();
            Test_ResponseNoBodyOn204();
            Test_ResponseNoBodyOn304();
            Test_ResponseNoBodyOn1xx();
            Test_ConflictingTEandCLRejectedStrict();

            // Section 7.1 — Chunked Transfer Coding
            Test_ChunkedBody();
            Test_ChunkedBodyWithTrailers();
            Test_ChunkedBodyZeroOnly();

            // Limits and callbacks
            Test_MaxHeaderCountEnforced();
            Test_StartLineTooLong();
            Test_CallbackRejection();

            // Pipeline support
            Test_BytesConsumedForPipelining();

            Console.WriteLine();
            Console.WriteLine("==========================================");
            Console.WriteLine("PASSED: {0}  FAILED: {1}  TOTAL: {2}", _passed, _failed, _passed + _failed);

            if (_failed > 0)
                Environment.Exit(1);
        }

        // ─── Helpers ────────────────────────────────────────────────────

        private static byte[] B(string s) => Encoding.ASCII.GetBytes(s);

        private static void Assert(bool condition, string testName, string detail = "")
        {
            if (condition)
            {
                Console.WriteLine("  PASS: " + testName);
                _passed++;
            }
            else
            {
                Console.WriteLine("  FAIL: " + testName + (detail != "" ? " — " + detail : ""));
                _failed++;
            }
        }

        private static void Section(string name)
        {
            Console.WriteLine();
            Console.WriteLine("--- " + name + " ---");
        }

        // ─── Section 2.1: Message Format ────────────────────────────────

        private static void Test_BasicGetRequest()
        {
            Section("Section 2.1 — Basic GET Request");
            var data = B("GET /index.html HTTP/1.1\r\nHost: example.com\r\n\r\n");
            var result = HttpParser.ParseRequest(data);

            Assert(result.Success, "Parse succeeds");
            Assert(result.IsRequest, "Is a request");
            Assert(result.Request.Method == "GET", "Method is GET");
            Assert(result.Request.RequestTarget == "/index.html", "Target is /index.html");
            Assert(result.Request.Version == "HTTP/1.1", "Version is HTTP/1.1");
            Assert(result.Request.VersionMajor == 1, "Major version is 1");
            Assert(result.Request.VersionMinor == 1, "Minor version is 1");
            Assert(result.Request.Headers["Host"] == "example.com", "Host header correct");
            Assert(result.Request.Body == null, "No body");
            Assert(result.Request.BodyFraming == BodyFrameKind.ZeroByAbsence, "Body framing is ZeroByAbsence");
        }

        private static void Test_BasicPostRequestWithBody()
        {
            Section("Section 2.1 — POST Request with Body");
            var data = B("POST /submit HTTP/1.1\r\nHost: example.com\r\nContent-Length: 11\r\n\r\nHello World");
            var result = HttpParser.ParseRequest(data);

            Assert(result.Success, "Parse succeeds");
            Assert(result.Request.Method == "POST", "Method is POST");
            Assert(result.Request.Body != null, "Body present");
            Assert(Encoding.ASCII.GetString(result.Request.Body) == "Hello World", "Body content correct");
            Assert(result.Request.BodyFraming == BodyFrameKind.ContentLength, "Body framing is ContentLength");
        }

        // ─── Section 2.2: Message Parsing ───────────────────────────────

        private static void Test_LeadingEmptyLinesIgnored()
        {
            Section("Section 2.2 — Leading Empty Lines");
            var data = B("\r\n\r\nGET / HTTP/1.1\r\nHost: x\r\n\r\n");
            var result = HttpParser.ParseRequest(data);

            Assert(result.Success, "Parse succeeds with leading CRLFs");
            Assert(result.Warnings.Count > 0, "Warning issued for leading empty lines");
        }

        private static void Test_BareLFTolerated()
        {
            Section("Section 2.2 — Bare LF Tolerated");
            var data = B("GET / HTTP/1.1\nHost: x\n\n");
            var result = HttpParser.ParseRequest(data);

            Assert(result.Success, "Parse succeeds with bare LF");
            Assert(result.Request.Method == "GET", "Method parsed correctly");
        }

        private static void Test_BareCRInHeaderRejectedStrict()
        {
            Section("Section 2.2 — Bare CR in Header (strict)");
            var data = B("GET / HTTP/1.1\r\nHost: ex\rample.com\r\n\r\n");
            var opts = ParserOptions.Strict;
            var result = HttpParser.ParseRequest(data, opts);

            Assert(!result.Success, "Strict mode rejects bare CR in header value");
            Assert(result.Error.Kind == ParseErrorKind.BareCR, "Error is BareCR");
        }

        private static void Test_WhitespaceAfterStartLineRejectedStrict()
        {
            Section("Section 2.2 — Whitespace After Start-line (strict)");
            var data = B("GET / HTTP/1.1\r\n Host: x\r\n\r\n");
            var opts = ParserOptions.Strict;
            var result = HttpParser.ParseRequest(data, opts);

            Assert(!result.Success, "Strict mode rejects whitespace after start-line");
        }

        private static void Test_WhitespaceAfterStartLineConsumedLenient()
        {
            Section("Section 2.2 — Whitespace After Start-line (lenient)");
            var data = B("GET / HTTP/1.1\r\n garbage line\r\nHost: x\r\n\r\n");
            var opts = new ParserOptions { StrictMode = false };
            var result = HttpParser.ParseRequest(data, opts);

            Assert(result.Success, "Lenient mode consumes whitespace-preceded lines");
            Assert(result.Request.Headers["Host"] == "x", "Host header parsed after skipped line");
        }

        // ─── Section 2.3: HTTP Version ──────────────────────────────────

        private static void Test_HttpVersionValidation()
        {
            Section("Section 2.3 — HTTP Version Validation");
            var data = B("GET / HTTZ/1.1\r\nHost: x\r\n\r\n");
            var result = HttpParser.ParseRequest(data);

            Assert(!result.Success, "Rejects invalid HTTP-name");
            Assert(result.Error.Kind == ParseErrorKind.InvalidVersion, "Error is InvalidVersion");
        }

        private static void Test_Http10Version()
        {
            Section("Section 2.3 — HTTP/1.0 Version");
            var data = B("GET / HTTP/1.0\r\nHost: x\r\n\r\n");
            var result = HttpParser.ParseRequest(data);

            Assert(result.Success, "Accepts HTTP/1.0");
            Assert(result.Request.VersionMajor == 1, "Major version 1");
            Assert(result.Request.VersionMinor == 0, "Minor version 0");
        }

        // ─── Section 3: Request Line ────────────────────────────────────

        private static void Test_RequestLineParsing()
        {
            Section("Section 3 — Request Line Parsing");
            var data = B("DELETE /resource/42 HTTP/1.1\r\nHost: api.example.com\r\n\r\n");
            var result = HttpParser.ParseRequest(data);

            Assert(result.Success, "Parse succeeds");
            Assert(result.Request.Method == "DELETE", "Method is DELETE");
            Assert(result.Request.RequestTarget == "/resource/42", "Target is /resource/42");
        }

        private static void Test_RequestLineMethodCaseSensitive()
        {
            Section("Section 3.1 — Method is Case-Sensitive");
            var data = B("get / HTTP/1.1\r\nHost: x\r\n\r\n");
            var result = HttpParser.ParseRequest(data);

            Assert(result.Success, "Lowercase method is valid token");
            Assert(result.Request.Method == "get", "Method preserved as 'get' (case-sensitive)");
        }

        // ─── Section 3.2: Request Target Forms ──────────────────────────

        private static void Test_OriginForm()
        {
            Section("Section 3.2.1 — Origin Form");
            var data = B("GET /where?q=now HTTP/1.1\r\nHost: www.example.org\r\n\r\n");
            var result = HttpParser.ParseRequest(data);

            Assert(result.Success, "Parse succeeds");
            Assert(result.Request.RequestTarget == "/where?q=now", "Target correct");
            Assert(result.Request.RequestTargetForm == RequestTargetForm.Origin, "Form is Origin");
        }

        private static void Test_AbsoluteForm()
        {
            Section("Section 3.2.2 — Absolute Form");
            var data = B("GET http://www.example.org/pub/WWW/TheProject.html HTTP/1.1\r\nHost: www.example.org\r\n\r\n");
            var result = HttpParser.ParseRequest(data);

            Assert(result.Success, "Parse succeeds");
            Assert(result.Request.RequestTargetForm == RequestTargetForm.Absolute, "Form is Absolute");
        }

        private static void Test_AuthorityForm()
        {
            Section("Section 3.2.3 — Authority Form");
            var data = B("CONNECT www.example.com:80 HTTP/1.1\r\nHost: www.example.com\r\n\r\n");
            var result = HttpParser.ParseRequest(data);

            Assert(result.Success, "Parse succeeds");
            Assert(result.Request.RequestTargetForm == RequestTargetForm.Authority, "Form is Authority");
        }

        private static void Test_AsteriskForm()
        {
            Section("Section 3.2.4 — Asterisk Form");
            var data = B("OPTIONS * HTTP/1.1\r\nHost: www.example.org\r\n\r\n");
            var result = HttpParser.ParseRequest(data);

            Assert(result.Success, "Parse succeeds");
            Assert(result.Request.RequestTarget == "*", "Target is *");
            Assert(result.Request.RequestTargetForm == RequestTargetForm.Asterisk, "Form is Asterisk");
        }

        // ─── Section 4: Status Line ─────────────────────────────────────

        private static void Test_BasicStatusLine()
        {
            Section("Section 4 — Basic Status Line");
            var data = B("HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello");
            var result = HttpParser.ParseResponse(data, "GET");

            Assert(result.Success, "Parse succeeds");
            Assert(result.IsResponse, "Is a response");
            Assert(result.Response.StatusCode == 200, "Status code is 200");
            Assert(result.Response.ReasonPhrase == "OK", "Reason phrase is OK");
            Assert(result.Response.Version == "HTTP/1.1", "Version is HTTP/1.1");
            Assert(Encoding.ASCII.GetString(result.Response.Body) == "hello", "Body is 'hello'");
        }

        private static void Test_StatusLineNoReasonPhrase()
        {
            Section("Section 4 — Status Line Without Reason Phrase");
            var data = B("HTTP/1.1 200 \r\nContent-Length: 0\r\n\r\n");
            var result = HttpParser.ParseResponse(data, "GET");

            Assert(result.Success, "Parse succeeds with empty reason phrase");
            Assert(result.Response.StatusCode == 200, "Status code is 200");
            Assert(result.Response.ReasonPhrase == "", "Reason phrase is empty string");
        }

        private static void Test_StatusLine404()
        {
            Section("Section 4 — 404 Not Found Response");
            var data = B("HTTP/1.1 404 Not Found\r\nContent-Length: 9\r\n\r\nNot Found");
            var result = HttpParser.ParseResponse(data, "GET");

            Assert(result.Success, "Parse succeeds");
            Assert(result.Response.StatusCode == 404, "Status code is 404");
            Assert(result.Response.ReasonPhrase == "Not Found", "Reason phrase is 'Not Found'");
        }

        // ─── Section 5: Field Syntax ────────────────────────────────────

        private static void Test_HeaderParsing()
        {
            Section("Section 5 — Header Parsing");
            var data = B("GET / HTTP/1.1\r\nHost: example.com\r\nAccept: text/html\r\nX-Custom: value123\r\n\r\n");
            var result = HttpParser.ParseRequest(data);

            Assert(result.Success, "Parse succeeds");
            Assert(result.Request.Headers.Count == 3, "3 headers parsed");
            Assert(result.Request.Headers["Accept"] == "text/html", "Accept header correct");
            Assert(result.Request.Headers["X-Custom"] == "value123", "Custom header correct");
        }

        private static void Test_HeaderCaseInsensitiveAccess()
        {
            Section("Section 5 — Case-Insensitive Header Access");
            var data = B("GET / HTTP/1.1\r\nhost: example.com\r\nCONTENT-TYPE: text/plain\r\n\r\n");
            var result = HttpParser.ParseRequest(data);

            Assert(result.Success, "Parse succeeds");
            Assert(result.Request.Headers["HOST"] == "example.com", "Case-insensitive lookup works");
            Assert(result.Request.Headers["content-type"] == "text/plain", "Case-insensitive lookup works (2)");
        }

        private static void Test_MultipleHeadersSameName()
        {
            Section("Section 5 — Multiple Headers Same Name");
            var data = B("GET / HTTP/1.1\r\nHost: x\r\nSet-Cookie: a=1\r\nSet-Cookie: b=2\r\n\r\n");
            var result = HttpParser.ParseRequest(data);

            Assert(result.Success, "Parse succeeds");
            Assert(result.Request.Headers.GetValues("Set-Cookie").Length == 2, "Two Set-Cookie values");
            Assert(result.Request.Headers["Set-Cookie"] == "a=1, b=2", "Combined value is comma-joined");
        }

        private static void Test_WhitespaceBeforeColonRejected()
        {
            Section("Section 5.1 — Whitespace Before Colon Rejected");
            var data = B("GET / HTTP/1.1\r\nHost : example.com\r\n\r\n");
            var result = HttpParser.ParseRequest(data);

            Assert(!result.Success, "Rejects whitespace before colon");
            Assert(result.Error.Kind == ParseErrorKind.WhitespaceBeforeColon, "Error is WhitespaceBeforeColon");
        }

        private static void Test_HeaderOWSTrimming()
        {
            Section("Section 5.1 — OWS Trimming");
            var data = B("GET / HTTP/1.1\r\nHost:   example.com   \r\n\r\n");
            var result = HttpParser.ParseRequest(data);

            Assert(result.Success, "Parse succeeds");
            Assert(result.Request.Headers["Host"] == "example.com", "Leading/trailing OWS trimmed");
        }

        // ─── Section 5.2: Obsolete Line Folding ─────────────────────────

        private static void Test_ObsLineFoldingRejected()
        {
            Section("Section 5.2 — Obs-fold Rejected (default)");
            var data = B("GET / HTTP/1.1\r\nHost: example.com\r\nX-Long: value\r\n continued\r\n\r\n");
            var result = HttpParser.ParseRequest(data);

            Assert(!result.Success, "Default rejects obs-fold");
            Assert(result.Error.Kind == ParseErrorKind.ObsoleteLineFolding, "Error is ObsoleteLineFolding");
        }

        private static void Test_ObsLineFoldingReplacedLenient()
        {
            Section("Section 5.2 — Obs-fold Replaced (lenient)");
            var data = B("GET / HTTP/1.1\r\nHost: example.com\r\nX-Long: value\r\n continued\r\n\r\n");
            var opts = new ParserOptions { RejectObsoleteLineFolding = false };
            var result = HttpParser.ParseRequest(data, opts);

            Assert(result.Success, "Lenient mode accepts obs-fold");
            Assert(result.Request.Headers["X-Long"].Contains("value"), "Value contains original part");
            Assert(result.Request.Headers["X-Long"].Contains("continued"), "Value contains folded part");
        }

        // ─── Section 6: Message Body ────────────────────────────────────

        private static void Test_ContentLengthBody()
        {
            Section("Section 6.2 — Content-Length Body");
            var body = "Hello, World!";
            var data = B("POST /api HTTP/1.1\r\nHost: x\r\nContent-Length: " + body.Length + "\r\n\r\n" + body);
            var result = HttpParser.ParseRequest(data);

            Assert(result.Success, "Parse succeeds");
            Assert(result.Request.Body != null, "Body is present");
            Assert(Encoding.ASCII.GetString(result.Request.Body) == body, "Body content matches");
            Assert(result.Request.BodyFraming == BodyFrameKind.ContentLength, "Framing is ContentLength");
        }

        private static void Test_ZeroContentLength()
        {
            Section("Section 6.2 — Zero Content-Length");
            var data = B("POST /api HTTP/1.1\r\nHost: x\r\nContent-Length: 0\r\n\r\n");
            var result = HttpParser.ParseRequest(data);

            Assert(result.Success, "Parse succeeds");
            Assert(result.Request.Body == null, "Body is null for zero CL");
        }

        private static void Test_NoBodyOnRequest()
        {
            Section("Section 6.3 Rule 7 — No Body on Request Without CL/TE");
            var data = B("GET / HTTP/1.1\r\nHost: x\r\n\r\n");
            var result = HttpParser.ParseRequest(data);

            Assert(result.Success, "Parse succeeds");
            Assert(result.Request.Body == null, "No body");
            Assert(result.Request.BodyFraming == BodyFrameKind.ZeroByAbsence, "Framing is ZeroByAbsence");
        }

        // ─── Section 6.3: Response Body Rules ───────────────────────────

        private static void Test_ResponseNoBodyOnHead()
        {
            Section("Section 6.3 Rule 1 — HEAD Response Has No Body");
            var data = B("HTTP/1.1 200 OK\r\nContent-Length: 1000\r\n\r\n");
            var result = HttpParser.ParseResponse(data, "HEAD");

            Assert(result.Success, "Parse succeeds");
            Assert(result.Response.Body == null, "No body on HEAD response");
            Assert(result.Response.BodyFraming == BodyFrameKind.NoBodyByStatus, "Framing is NoBodyByStatus");
        }

        private static void Test_ResponseNoBodyOn204()
        {
            Section("Section 6.3 Rule 1 — 204 No Content");
            var data = B("HTTP/1.1 204 No Content\r\n\r\n");
            var result = HttpParser.ParseResponse(data, "GET");

            Assert(result.Success, "Parse succeeds");
            Assert(result.Response.Body == null, "No body on 204");
        }

        private static void Test_ResponseNoBodyOn304()
        {
            Section("Section 6.3 Rule 1 — 304 Not Modified");
            var data = B("HTTP/1.1 304 Not Modified\r\n\r\n");
            var result = HttpParser.ParseResponse(data, "GET");

            Assert(result.Success, "Parse succeeds");
            Assert(result.Response.Body == null, "No body on 304");
        }

        private static void Test_ResponseNoBodyOn1xx()
        {
            Section("Section 6.3 Rule 1 — 100 Continue");
            var data = B("HTTP/1.1 100 Continue\r\n\r\n");
            var result = HttpParser.ParseResponse(data, "POST");

            Assert(result.Success, "Parse succeeds");
            Assert(result.Response.Body == null, "No body on 100");
        }

        private static void Test_ConflictingTEandCLRejectedStrict()
        {
            Section("Section 6.3 Rule 3 — Conflicting TE and CL (strict)");
            var data = B("POST / HTTP/1.1\r\nHost: x\r\nTransfer-Encoding: chunked\r\nContent-Length: 5\r\n\r\n0\r\n\r\n");
            var opts = new ParserOptions { RejectConflictingFraming = true };
            var result = HttpParser.ParseRequest(data, opts);

            Assert(!result.Success, "Strict mode rejects conflicting TE + CL");
            Assert(result.Error.Kind == ParseErrorKind.ConflictingFraming, "Error is ConflictingFraming");
        }

        // ─── Section 7.1: Chunked Transfer Coding ───────────────────────

        private static void Test_ChunkedBody()
        {
            Section("Section 7.1 — Chunked Body");
            var data = B(
                "POST /upload HTTP/1.1\r\n" +
                "Host: x\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n" +
                "7\r\n" +
                "Mozilla\r\n" +
                "9\r\n" +
                "Developer\r\n" +
                "7\r\n" +
                "Network\r\n" +
                "0\r\n" +
                "\r\n");
            var opts = new ParserOptions { RejectConflictingFraming = true };
            var result = HttpParser.ParseRequest(data, opts);

            Assert(result.Success, "Parse succeeds");
            Assert(result.Request.BodyFraming == BodyFrameKind.Chunked, "Framing is Chunked");
            string body = Encoding.ASCII.GetString(result.Request.Body);
            Assert(body == "MozillaDeveloperNetwork", "Chunked body decoded correctly: '" + body + "'");
        }

        private static void Test_ChunkedBodyWithTrailers()
        {
            Section("Section 7.1.2 — Chunked Body with Trailers");
            var data = B(
                "POST /data HTTP/1.1\r\n" +
                "Host: x\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n" +
                "5\r\n" +
                "Hello\r\n" +
                "0\r\n" +
                "X-Checksum: abc123\r\n" +
                "\r\n");
            var opts = new ParserOptions { RejectConflictingFraming = true };
            var result = HttpParser.ParseRequest(data, opts);

            Assert(result.Success, "Parse succeeds");
            string body = Encoding.ASCII.GetString(result.Request.Body);
            Assert(body == "Hello", "Body decoded: '" + body + "'");
            Assert(result.Request.Trailers.Count == 1, "One trailer field");
            Assert(result.Request.Trailers["X-Checksum"] == "abc123", "Trailer value correct");
        }

        private static void Test_ChunkedBodyZeroOnly()
        {
            Section("Section 7.1 — Chunked Body Zero-Only (empty body)");
            var data = B(
                "POST /empty HTTP/1.1\r\n" +
                "Host: x\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n" +
                "0\r\n" +
                "\r\n");
            var opts = new ParserOptions { RejectConflictingFraming = true };
            var result = HttpParser.ParseRequest(data, opts);

            Assert(result.Success, "Parse succeeds");
            Assert(result.Request.Body.Length == 0, "Body is empty (zero-length)");
        }

        // ─── Limits and Callbacks ───────────────────────────────────────

        private static void Test_MaxHeaderCountEnforced()
        {
            Section("Limits — Max Header Count");
            var sb = new StringBuilder();
            sb.Append("GET / HTTP/1.1\r\n");
            for (int i = 0; i < 6; i++)
                sb.AppendFormat("X-Header-{0}: value{0}\r\n", i);
            sb.Append("\r\n");

            var opts = new ParserOptions { MaxHeaderCount = 5 };
            var result = HttpParser.ParseRequest(B(sb.ToString()), opts);

            Assert(!result.Success, "Rejects when header count exceeds limit");
            Assert(result.Error.Kind == ParseErrorKind.TooManyHeaders, "Error is TooManyHeaders");
        }

        private static void Test_StartLineTooLong()
        {
            Section("Limits — Start-line Too Long");
            string longTarget = "/" + new string('a', 100);
            var data = B("GET " + longTarget + " HTTP/1.1\r\nHost: x\r\n\r\n");
            var opts = new ParserOptions { MaxStartLineLength = 50 };
            var result = HttpParser.ParseRequest(data, opts);

            Assert(!result.Success, "Rejects oversized start-line");
            Assert(result.Error.Kind == ParseErrorKind.StartLineTooLong, "Error is StartLineTooLong");
        }

        private static void Test_CallbackRejection()
        {
            Section("Callbacks — Header Rejection");
            var data = B("GET / HTTP/1.1\r\nHost: x\r\nX-Evil: injection\r\n\r\n");
            var opts = new ParserOptions
            {
                OnHeaderParsed = (name, value, idx) =>
                {
                    // Reject any header named X-Evil
                    return !name.Equals("X-Evil", StringComparison.OrdinalIgnoreCase);
                }
            };
            var result = HttpParser.ParseRequest(data, opts);

            Assert(!result.Success, "Callback rejects the message");
            Assert(result.Error.Kind == ParseErrorKind.RejectedByCallback, "Error is RejectedByCallback");
        }

        // ─── Pipeline Support ───────────────────────────────────────────

        private static void Test_BytesConsumedForPipelining()
        {
            Section("Pipeline — BytesConsumed");
            string msg1 = "GET /first HTTP/1.1\r\nHost: x\r\n\r\n";
            string msg2 = "GET /second HTTP/1.1\r\nHost: x\r\n\r\n";
            var data = B(msg1 + msg2);

            var result1 = HttpParser.ParseRequest(data);
            Assert(result1.Success, "First request parses");
            Assert(result1.Request.RequestTarget == "/first", "First target is /first");
            Assert(result1.BytesConsumed == msg1.Length, "BytesConsumed matches first message length");

            // Parse second message starting where the first ended
            var result2 = HttpParser.ParseRequest(data, result1.BytesConsumed, data.Length - result1.BytesConsumed);
            Assert(result2.Success, "Second request parses");
            Assert(result2.Request.RequestTarget == "/second", "Second target is /second");
        }
    }
}