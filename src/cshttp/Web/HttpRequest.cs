using System;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Text;

namespace cshttp
{
    /// <summary>
    /// The request as an application sees it. Member names follow System.Web.HttpRequest so that
    /// existing handler code compiles unchanged. Collections are parsed lazily on first access.
    /// </summary>
    public sealed class HttpRequest
    {
        static readonly byte[] EmptyBody = new byte[0];

        readonly HttpRequestHead _head;
        readonly byte[] _body;
        readonly int _bodyOffset;
        readonly int _bodyLength;
        readonly RequestConnectionInfo _conn;
        readonly RequestSettings _settings;

        string _path;
        Uri _url;
        HttpValueCollection _query, _form, _headers, _serverVariables, _params;
        HttpFileCollection _files;
        HttpCookieCollection _cookies;
        Stream _inputStream;
        byte[] _bodyBytes;
        string _bodyText;
        Encoding _contentEncoding;
        UnvalidatedRequestValues _unvalidated;
        HttpException _bodyError;

        public HttpRequest(HttpRequestHead head, byte[] body, int bodyOffset, int bodyLength,
            RequestConnectionInfo connection, RequestSettings settings)
        {
            if (head == null) throw new ArgumentNullException("head");
            _head = head;
            _body = body ?? EmptyBody;
            _bodyOffset = body == null ? 0 : bodyOffset;
            _bodyLength = body == null ? 0 : bodyLength;
            _conn = connection ?? new RequestConnectionInfo();
            _settings = settings ?? RequestSettings.Shared;
        }

        /// <summary>The parsed wire-level head (raw target, all headers, framing).</summary>
        public HttpRequestHead Head { get { return _head; } }

        public RequestConnectionInfo Connection { get { return _conn; } }

        public string HttpMethod { get { return _head.Method; } }

        public string RequestType { get { return _head.Method; } }

        /// <summary>Percent-decoded path, e.g. "/files/a b". ('+' is not a space in paths.) Trailing slashes are trimmed except for "/".</summary>
        public string Path
        {
            get
            {
                if (_path == null)
                {
                    if (_head.TargetForm == RequestTargetForm.Asterisk)
                    {
                        _path = "*";
                    }
                    else
                    {
                        string decoded = PercentDecoder.Decode(_head.Path, false, Encoding.UTF8);
                        if (decoded.Length > 1 && decoded[decoded.Length - 1] == '/')
                        {
                            decoded = decoded.TrimEnd('/');
                            if (decoded.Length == 0) decoded = "/";
                        }
                        _path = decoded;
                    }
                }
                return _path;
            }
        }

        public string FilePath { get { return Path; } }

        public string CurrentExecutionFilePath { get { return Path; } }

        public string AppRelativeCurrentExecutionFilePath { get { return "~" + Path; } }

        public string PathInfo { get { return string.Empty; } }

        public string ApplicationPath { get { return "/"; } }

        /// <summary>Raw path and query as sent, e.g. "/a%20b?x=1".</summary>
        public string RawUrl
        {
            get
            {
                if (_head.TargetForm == RequestTargetForm.Absolute)
                    return _head.Query == null ? _head.Path : _head.Path + "?" + _head.Query;
                return _head.Target;
            }
        }

        public Uri Url
        {
            get
            {
                if (_url == null)
                {
                    string scheme = IsSecureConnection ? "https" : "http";
                    string host = string.IsNullOrEmpty(_head.Host) ? (_conn.LocalAddress + ":" + _conn.LocalPort) : _head.Host;
                    if (!Uri.TryCreate(scheme + "://" + host + RawUrl, UriKind.Absolute, out _url))
                        _url = new Uri(scheme + "://localhost/");
                }
                return _url;
            }
        }

        public NameValueCollection QueryString
        {
            get
            {
                if (_query == null)
                {
                    var c = new HttpValueCollection();
                    UrlEncodedParser.Parse(_head.Query, c, Encoding.UTF8, _settings.MaxCollectionKeys);
                    c.MakeReadOnly();
                    _query = c;
                }
                return _query;
            }
        }

        public NameValueCollection Form
        {
            get
            {
                if (_form == null) ParseBody();
                return _form;
            }
        }

        public HttpFileCollection Files
        {
            get
            {
                if (_files == null) ParseBody();
                return _files;
            }
        }

        public HttpCookieCollection Cookies
        {
            get
            {
                if (_cookies == null)
                {
                    var c = new HttpCookieCollection();
                    foreach (string h in _head.Headers.GetAll("Cookie")) HttpCookieCollection.ParseInto(h, c);
                    _cookies = c;
                }
                return _cookies;
            }
        }

        public NameValueCollection Headers
        {
            get
            {
                if (_headers == null)
                {
                    var c = new HttpValueCollection();
                    foreach (HttpHeader h in _head.Headers) c.Add(h.Name, h.Value);
                    c.MakeReadOnly();
                    _headers = c;
                }
                return _headers;
            }
        }

        /// <summary>CGI-style variables (REMOTE_ADDR, HTTPS, HTTP_HOST, ...), for compatibility.</summary>
        public NameValueCollection ServerVariables
        {
            get
            {
                if (_serverVariables == null)
                {
                    var c = new HttpValueCollection();
                    c.Add("REMOTE_ADDR", UserHostAddress);
                    c.Add("REMOTE_HOST", UserHostAddress);
                    c.Add("REMOTE_PORT", _conn.RemotePort.ToString());
                    c.Add("LOCAL_ADDR", AddressString(_conn.LocalAddress));
                    c.Add("SERVER_PORT", _conn.LocalPort.ToString());
                    c.Add("SERVER_NAME", Url.Host);
                    c.Add("SERVER_PROTOCOL", _head.Version);
                    c.Add("HTTPS", IsSecureConnection ? "on" : "off");
                    c.Add("REQUEST_METHOD", HttpMethod);
                    c.Add("QUERY_STRING", _head.Query ?? string.Empty);
                    c.Add("URL", Path);
                    c.Add("PATH_INFO", Path);
                    c.Add("SCRIPT_NAME", Path);
                    c.Add("CONTENT_TYPE", ContentType);
                    c.Add("CONTENT_LENGTH", _bodyLength.ToString());
                    foreach (HttpHeader h in _head.Headers)
                        c.Add("HTTP_" + h.Name.ToUpperInvariant().Replace('-', '_'), h.Value);
                    c.MakeReadOnly();
                    _serverVariables = c;
                }
                return _serverVariables;
            }
        }

        /// <summary>QueryString, Form, Cookies and ServerVariables merged in that order.</summary>
        public NameValueCollection Params
        {
            get
            {
                if (_params == null)
                {
                    var c = new HttpValueCollection();
                    c.Add(QueryString);
                    c.Add(Form);
                    HttpCookieCollection cookies = Cookies;
                    for (int i = 0; i < cookies.Count; i++) c.Add(cookies[i].Name, cookies[i].Value);
                    c.Add(ServerVariables);
                    c.MakeReadOnly();
                    _params = c;
                }
                return _params;
            }
        }

        /// <summary>Looks up QueryString, then Form, then Cookies, then ServerVariables (System.Web order).</summary>
        public string this[string key]
        {
            get
            {
                string v = QueryString[key];
                if (v != null) return v;
                v = Form[key];
                if (v != null) return v;
                HttpCookie cookie = Cookies[key];
                if (cookie != null) return cookie.Value;
                return ServerVariables[key];
            }
        }

        /// <summary>No request validation exists, so this simply exposes the same values.</summary>
        public UnvalidatedRequestValues Unvalidated
        {
            get { return _unvalidated ?? (_unvalidated = new UnvalidatedRequestValues(this)); }
        }

        public string ContentType { get { return _head.Headers.Get("Content-Type") ?? string.Empty; } }

        public int ContentLength { get { return _bodyLength; } }

        public int TotalBytes { get { return _bodyLength; } }

        public Encoding ContentEncoding
        {
            get
            {
                if (_contentEncoding == null)
                {
                    Encoding enc = Encoding.UTF8;
                    string charset = HeaderValue.GetParameter(ContentType, "charset");
                    if (!string.IsNullOrEmpty(charset))
                    {
                        try { enc = Encoding.GetEncoding(charset.Trim()); }
                        catch (ArgumentException) { enc = Encoding.UTF8; }
                    }
                    _contentEncoding = enc;
                }
                return _contentEncoding;
            }
            set { _contentEncoding = value; }
        }

        /// <summary>The raw body. Reads directly from the received bytes (no copy).</summary>
        public Stream InputStream
        {
            get { return _inputStream ?? (_inputStream = new MemoryStream(_body, _bodyOffset, _bodyLength, false, true)); }
        }

        /// <summary>The raw request body as a byte array. Cached per request.</summary>
        public byte[] BodyBytes
        {
            get
            {
                if (_bodyBytes == null)
                {
                    if (_bodyLength == 0)
                    {
                        _bodyBytes = EmptyBody;
                    }
                    else
                    {
                        byte[] copy = new byte[_bodyLength];
                        Buffer.BlockCopy(_body, _bodyOffset, copy, 0, _bodyLength);
                        _bodyBytes = copy;
                    }
                }
                return _bodyBytes;
            }
        }

        /// <summary>The raw request body decoded as text using ContentEncoding (defaults to UTF-8). Cached per request.</summary>
        public string BodyText
        {
            get
            {
                if (_bodyText == null)
                {
                    _bodyText = _bodyLength == 0 ? string.Empty : ContentEncoding.GetString(_body, _bodyOffset, _bodyLength);
                }
                return _bodyText;
            }
        }

        public byte[] BinaryRead(int count)
        {
            if (count < 0 || count > _bodyLength) throw new ArgumentOutOfRangeException("count");
            byte[] buffer = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = InputStream.Read(buffer, read, count - read);
                if (n == 0) break;
                read += n;
            }
            if (read < count) Array.Resize(ref buffer, read);
            return buffer;
        }

        public bool IsSecureConnection { get { return _conn.IsSecure; } }

        public string UserHostAddress { get { return AddressString(_conn.RemoteAddress); } }


        /// <summary>True when the (proxy-resolved) client address is a loopback address.</summary>
        public bool IsLocal
        {
            get
            {
                IPAddress a = RequestConnectionInfo.Normalize(_conn.RemoteAddress);
                return a != null && IPAddress.IsLoopback(a);
            }
        }

        public bool IsAuthenticated { get { return false; } }

        public string UserAgent { get { return _head.Headers.Get("User-Agent"); } }

        public Uri UrlReferrer
        {
            get
            {
                string r = _head.Headers.Get("Referer");
                Uri u;
                return r != null && Uri.TryCreate(r, UriKind.Absolute, out u) ? u : null;
            }
        }

        public string[] AcceptTypes { get { return SplitHeader("Accept"); } }

        public string[] UserLanguages { get { return SplitHeader("Accept-Language"); } }

        public string MapPath(string virtualPath) { return HostingEnvironment.MapPath(virtualPath); }

        void ParseBody()
        {
            if (_bodyError != null) throw new HttpException(_bodyError.GetHttpCode(), _bodyError.Message, _bodyError);
            var form = new HttpValueCollection();
            var files = new HttpFileCollection();
            try
            {
                if (_bodyLength > 0)
                {
                    string mediaType = HeaderValue.GetMediaType(ContentType);
                    if (mediaType == "application/x-www-form-urlencoded")
                        UrlEncodedParser.Parse(_body, _bodyOffset, _bodyLength, form, ContentEncoding, _settings.MaxCollectionKeys);
                    else if (mediaType == "multipart/form-data")
                        MultipartParser.Parse(_body, _bodyOffset, _bodyLength, HeaderValue.GetParameter(ContentType, "boundary"),
                            ContentEncoding, form, files, _settings);
                }
            }
            catch (HttpException ex)
            {
                _bodyError = ex;
                throw;
            }
            form.MakeReadOnly();
            _form = form;
            _files = files;
        }

        string[] SplitHeader(string name)
        {
            string v = _head.Headers.GetCombined(name);
            if (v == null) return null;
            string[] parts = v.Split(',');
            for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
            return parts;
        }

        static string AddressString(IPAddress a)
        {
            a = RequestConnectionInfo.Normalize(a);
            return a == null ? string.Empty : a.ToString();
        }
    }

    /// <summary>Same surface as System.Web's UnvalidatedRequestValues (there is no validation to skip here).</summary>
    public sealed class UnvalidatedRequestValues
    {
        readonly HttpRequest _r;

        internal UnvalidatedRequestValues(HttpRequest request) { _r = request; }

        public NameValueCollection Form { get { return _r.Form; } }
        public NameValueCollection QueryString { get { return _r.QueryString; } }
        public NameValueCollection Headers { get { return _r.Headers; } }
        public HttpCookieCollection Cookies { get { return _r.Cookies; } }
        public HttpFileCollection Files { get { return _r.Files; } }
        public string Path { get { return _r.Path; } }
        public string RawUrl { get { return _r.RawUrl; } }
        public Uri Url { get { return _r.Url; } }
        public string this[string key] { get { return _r[key]; } }
    }
}
