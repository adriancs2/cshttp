using System;
using System.Collections;
using System.Collections.Generic;

namespace cshttp
{
    /// <summary>One header field as received or to be sent.</summary>
    public struct HttpHeader
    {
        public readonly string Name;
        public readonly string Value;

        public HttpHeader(string name, string value)
        {
            Name = name;
            Value = value;
        }

        public override string ToString() { return Name + ": " + Value; }
    }

    /// <summary>
    /// Ordered header list with case-insensitive lookup. Duplicates are kept in order,
    /// which matters for Set-Cookie and for detecting request-smuggling attempts.
    /// </summary>
    public sealed class HttpHeaders : IEnumerable<HttpHeader>
    {
        readonly List<HttpHeader> _list = new List<HttpHeader>();

        public int Count { get { return _list.Count; } }

        public HttpHeader this[int index] { get { return _list[index]; } }

        public void Add(string name, string value)
        {
            if (name == null) throw new ArgumentNullException("name");
            _list.Add(new HttpHeader(name, value ?? string.Empty));
        }

        /// <summary>First value for the name, or null.</summary>
        public string Get(string name)
        {
            for (int i = 0; i < _list.Count; i++)
                if (string.Equals(_list[i].Name, name, StringComparison.OrdinalIgnoreCase)) return _list[i].Value;
            return null;
        }

        /// <summary>All values joined with ", " (RFC 9110 list semantics), or null if absent.</summary>
        public string GetCombined(string name)
        {
            string result = null;
            for (int i = 0; i < _list.Count; i++)
            {
                if (!string.Equals(_list[i].Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                result = result == null ? _list[i].Value : result + ", " + _list[i].Value;
            }
            return result;
        }

        public List<string> GetAll(string name)
        {
            var values = new List<string>();
            for (int i = 0; i < _list.Count; i++)
                if (string.Equals(_list[i].Name, name, StringComparison.OrdinalIgnoreCase)) values.Add(_list[i].Value);
            return values;
        }

        public bool Contains(string name)
        {
            for (int i = 0; i < _list.Count; i++)
                if (string.Equals(_list[i].Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public int CountOf(string name)
        {
            int n = 0;
            for (int i = 0; i < _list.Count; i++)
                if (string.Equals(_list[i].Name, name, StringComparison.OrdinalIgnoreCase)) n++;
            return n;
        }

        /// <summary>Removes every header with the name, then adds one.</summary>
        public void Set(string name, string value)
        {
            Remove(name);
            Add(name, value);
        }

        public bool Remove(string name)
        {
            return _list.RemoveAll(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;
        }

        public void Clear() { _list.Clear(); }

        public IEnumerator<HttpHeader> GetEnumerator() { return _list.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return _list.GetEnumerator(); }
    }

    /// <summary>Canonical strings for common header names, so parsing does not allocate them.</summary>
    public static class KnownHeaders
    {
        static readonly string[] Names =
        {
            "Host", "Connection", "Content-Length", "Content-Type", "Transfer-Encoding",
            "Accept", "Accept-Encoding", "Accept-Language", "User-Agent", "Cookie", "Referer",
            "Origin", "Cache-Control", "Pragma", "Upgrade-Insecure-Requests", "If-None-Match",
            "If-Modified-Since", "If-Range", "Range", "Expect", "Authorization", "Sec-Fetch-Site",
            "Sec-Fetch-Mode", "Sec-Fetch-Dest", "Sec-Fetch-User", "Sec-Ch-Ua", "Sec-Ch-Ua-Mobile",
            "Sec-Ch-Ua-Platform", "X-Requested-With", "X-Forwarded-For", "X-Forwarded-Proto",
            "X-Forwarded-Host", "Priority", "DNT", "TE", "Keep-Alive", "Content-Disposition"
        };

        public static string Find(byte[] data, int offset, int count)
        {
            for (int i = 0; i < Names.Length; i++)
            {
                string n = Names[i];
                if (n.Length == count && ByteScan.EqualsAsciiIgnoreCase(data, offset, count, n)) return n;
            }
            return null;
        }
    }
}
