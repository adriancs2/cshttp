using System;
using System.Collections.Generic;
using System.Globalization;

namespace cshttp
{
    /// <summary>Same numeric values as System.Web.HttpCacheability.</summary>
    public enum HttpCacheability
    {
        NoCache = 1,
        Private = 2,
        Server = 3,
        ServerAndNoCache = 3,
        Public = 4,
        ServerAndPrivate = 5
    }

    /// <summary>
    /// Subset of System.Web.HttpCachePolicy (Response.Cache). Calls build the Cache-Control header;
    /// Expires / Last-Modified / ETag become ordinary response headers.
    /// </summary>
    public sealed class HttpCachePolicy
    {
        readonly HttpResponse _response;
        HttpCacheability? _cacheability;
        TimeSpan? _maxAge;
        TimeSpan? _proxyMaxAge;
        bool _noStore;
        bool _mustRevalidate;
        readonly List<string> _extensions = new List<string>();

        internal HttpCachePolicy(HttpResponse response) { _response = response; }

        internal bool IsModified { get; private set; }

        public void SetCacheability(HttpCacheability cacheability) { _cacheability = cacheability; IsModified = true; }

        public void SetMaxAge(TimeSpan delta)
        {
            if (delta < TimeSpan.Zero) throw new ArgumentOutOfRangeException("delta");
            _maxAge = delta;
            IsModified = true;
        }

        public void SetProxyMaxAge(TimeSpan delta)
        {
            if (delta < TimeSpan.Zero) throw new ArgumentOutOfRangeException("delta");
            _proxyMaxAge = delta;
            IsModified = true;
        }

        public void SetNoStore() { _noStore = true; IsModified = true; }

        public void SetRevalidation(int unusedRevalidationMode) { _mustRevalidate = true; IsModified = true; }

        public void AppendCacheExtension(string extension)
        {
            if (!HttpChars.IsSafeFieldValue(extension)) throw new ArgumentException("Invalid cache extension.");
            _extensions.Add(extension);
            IsModified = true;
        }

        public void SetExpires(DateTime date) { _response.SetHeader("Expires", HttpDate.Format(date)); }

        public void SetLastModified(DateTime date) { _response.SetHeader("Last-Modified", HttpDate.Format(date)); }

        public void SetETag(string etag) { _response.SetHeader("ETag", etag); }

        /// <summary>No server-side output cache exists; kept for source compatibility.</summary>
        public void SetNoServerCaching() { }

        public void SetNoTransforms() { AppendCacheExtension("no-transform"); }

        /// <summary>Builds Cache-Control; the response's own CacheControl value is the starting point.</summary>
        internal string Compose(string baseValue)
        {
            if (!IsModified) return baseValue;
            var parts = new List<string>();
            if (_cacheability.HasValue)
            {
                switch (_cacheability.Value)
                {
                    case HttpCacheability.NoCache: parts.Add("no-cache"); break;
                    case HttpCacheability.Private:
                    case HttpCacheability.ServerAndPrivate: parts.Add("private"); break;
                    case HttpCacheability.Public: parts.Add("public"); break;
                    default: parts.Add("no-cache"); break;
                }
            }
            else if (!string.IsNullOrEmpty(baseValue)) parts.Add(baseValue);
            if (_noStore) parts.Add("no-store");
            if (_mustRevalidate) parts.Add("must-revalidate");
            if (_maxAge.HasValue) parts.Add("max-age=" + ((long)_maxAge.Value.TotalSeconds).ToString(CultureInfo.InvariantCulture));
            if (_proxyMaxAge.HasValue) parts.Add("s-maxage=" + ((long)_proxyMaxAge.Value.TotalSeconds).ToString(CultureInfo.InvariantCulture));
            parts.AddRange(_extensions);
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }
    }
}
