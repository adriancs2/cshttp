using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace cshttp
{
    /// <summary>Same numeric values as System.Web.SameSiteMode.</summary>
    public enum SameSiteMode
    {
        Unspecified = -1,
        None = 0,
        Lax = 1,
        Strict = 2
    }

    public sealed class HttpCookie
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Path { get; set; }
        public string Domain { get; set; }

        /// <summary>DateTime.MinValue (default) means a session cookie.</summary>
        public DateTime Expires { get; set; }

        public bool HttpOnly { get; set; }
        public bool Secure { get; set; }
        public SameSiteMode SameSite { get; set; }

        public HttpCookie(string name) : this(name, string.Empty) { }

        public HttpCookie(string name, string value)
        {
            Name = name;
            Value = value;
            Path = "/";
            SameSite = SameSiteMode.Unspecified;
        }

        /// <summary>The Set-Cookie header value. Throws if any part could inject header syntax.</summary>
        public string ToSetCookieHeader()
        {
            if (string.IsNullOrEmpty(Name)) throw new InvalidOperationException("Cookie name is required.");
            Check(Name, "name", true);
            Check(Value, "value", false);
            Check(Path, "path", false);
            Check(Domain, "domain", false);
            var sb = new StringBuilder();
            sb.Append(Name).Append('=').Append(Value ?? string.Empty);
            if (Expires != DateTime.MinValue)
                sb.Append("; expires=").Append(Expires.ToUniversalTime().ToString("ddd, dd-MMM-yyyy HH':'mm':'ss 'GMT'", CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(Domain)) sb.Append("; domain=").Append(Domain);
            if (!string.IsNullOrEmpty(Path)) sb.Append("; path=").Append(Path);
            if (Secure) sb.Append("; secure");
            if (HttpOnly) sb.Append("; HttpOnly");
            switch (SameSite)
            {
                case SameSiteMode.None: sb.Append("; SameSite=None"); break;
                case SameSiteMode.Lax: sb.Append("; SameSite=Lax"); break;
                case SameSiteMode.Strict: sb.Append("; SameSite=Strict"); break;
            }
            return sb.ToString();
        }

        static void Check(string s, string what, bool isName)
        {
            if (s == null) return;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < 0x20 || c == 0x7F || c == ';' || (isName && (c == '=' || c == ' ' || c == ',')))
                    throw new ArgumentException("Invalid character in cookie " + what + ".");
            }
        }

        public override string ToString() { return Name + "=" + Value; }
    }

    /// <summary>
    /// Ordered cookie collection with case-insensitive name lookup (System.Web semantics).
    /// Enumerating yields cookie names, like System.Web.
    /// </summary>
    public sealed class HttpCookieCollection : IEnumerable
    {
        readonly List<HttpCookie> _list = new List<HttpCookie>();

        public int Count { get { return _list.Count; } }

        public HttpCookie this[int index] { get { return _list[index]; } }

        public HttpCookie this[string name] { get { return Get(name); } }

        public void Add(HttpCookie cookie)
        {
            if (cookie == null) throw new ArgumentNullException("cookie");
            _list.Add(cookie);
        }

        /// <summary>Replaces the first cookie with the same name, or adds it.</summary>
        public void Set(HttpCookie cookie)
        {
            if (cookie == null) throw new ArgumentNullException("cookie");
            for (int i = 0; i < _list.Count; i++)
            {
                if (string.Equals(_list[i].Name, cookie.Name, StringComparison.OrdinalIgnoreCase)) { _list[i] = cookie; return; }
            }
            _list.Add(cookie);
        }

        public HttpCookie Get(string name)
        {
            for (int i = 0; i < _list.Count; i++)
                if (string.Equals(_list[i].Name, name, StringComparison.OrdinalIgnoreCase)) return _list[i];
            return null;
        }

        public void Remove(string name)
        {
            _list.RemoveAll(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public void Clear() { _list.Clear(); }

        public string[] AllKeys
        {
            get
            {
                var keys = new string[_list.Count];
                for (int i = 0; i < keys.Length; i++) keys[i] = _list[i].Name;
                return keys;
            }
        }

        public IEnumerator GetEnumerator() { return AllKeys.GetEnumerator(); }

        /// <summary>Parses a Cookie request header ("a=1; b=2"). Values are kept raw (not URL-decoded).</summary>
        public static void ParseInto(string header, HttpCookieCollection target)
        {
            if (string.IsNullOrEmpty(header)) return;
            foreach (string part in header.Split(';'))
            {
                string p = part.Trim();
                if (p.Length == 0) continue;
                int eq = p.IndexOf('=');
                if (eq < 0) target.Add(new HttpCookie(string.Empty, p));
                else target.Add(new HttpCookie(p.Substring(0, eq).Trim(), p.Substring(eq + 1).Trim()));
            }
        }
    }

    /// <summary>An uploaded file. The data is not copied: InputStream reads straight from the request body.</summary>
    public sealed class HttpPostedFile
    {
        readonly byte[] _data;
        readonly int _offset;
        readonly int _length;
        Stream _stream;

        public HttpPostedFile(string fileName, string contentType, byte[] data, int offset, int length)
        {
            FileName = fileName ?? string.Empty;
            ContentType = contentType ?? string.Empty;
            _data = data;
            _offset = offset;
            _length = length;
        }

        /// <summary>The file name as sent by the client. Never use it directly as a path.</summary>
        public string FileName { get; private set; }

        public string ContentType { get; private set; }

        public int ContentLength { get { return _length; } }

        public Stream InputStream
        {
            get { return _stream ?? (_stream = new MemoryStream(_data, _offset, _length, false, true)); }
        }

        public void SaveAs(string filename)
        {
            using (var fs = new FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.None))
                fs.Write(_data, _offset, _length);
        }

        public byte[] ToArray()
        {
            byte[] copy = new byte[_length];
            Buffer.BlockCopy(_data, _offset, copy, 0, _length);
            return copy;
        }
    }

    public sealed class HttpFileCollection
    {
        readonly List<KeyValuePair<string, HttpPostedFile>> _list = new List<KeyValuePair<string, HttpPostedFile>>();

        public int Count { get { return _list.Count; } }

        public HttpPostedFile this[int index] { get { return _list[index].Value; } }

        public HttpPostedFile this[string name] { get { return Get(name); } }

        public void Add(string name, HttpPostedFile file)
        {
            _list.Add(new KeyValuePair<string, HttpPostedFile>(name, file));
        }

        public HttpPostedFile Get(string name)
        {
            for (int i = 0; i < _list.Count; i++)
                if (string.Equals(_list[i].Key, name, StringComparison.OrdinalIgnoreCase)) return _list[i].Value;
            return null;
        }

        public IList<HttpPostedFile> GetMultiple(string name)
        {
            var result = new List<HttpPostedFile>();
            for (int i = 0; i < _list.Count; i++)
                if (string.Equals(_list[i].Key, name, StringComparison.OrdinalIgnoreCase)) result.Add(_list[i].Value);
            return result;
        }

        public string GetKey(int index) { return _list[index].Key; }

        public string[] AllKeys
        {
            get
            {
                var keys = new string[_list.Count];
                for (int i = 0; i < keys.Length; i++) keys[i] = _list[i].Key;
                return keys;
            }
        }
    }
}
