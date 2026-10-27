using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace cshttp
{
    public static class MimeTypes
    {
        static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".html", "text/html" }, { ".htm", "text/html" }, { ".css", "text/css" },
            { ".js", "text/javascript" }, { ".mjs", "text/javascript" }, { ".json", "application/json" },
            { ".map", "application/json" }, { ".txt", "text/plain" }, { ".md", "text/markdown" },
            { ".csv", "text/csv" }, { ".xml", "application/xml" }, { ".svg", "image/svg+xml" },
            { ".png", "image/png" }, { ".jpg", "image/jpeg" }, { ".jpeg", "image/jpeg" }, { ".gif", "image/gif" },
            { ".webp", "image/webp" }, { ".avif", "image/avif" }, { ".ico", "image/x-icon" }, { ".bmp", "image/bmp" },
            { ".woff", "font/woff" }, { ".woff2", "font/woff2" }, { ".ttf", "font/ttf" }, { ".otf", "font/otf" },
            { ".eot", "application/vnd.ms-fontobject" }, { ".pdf", "application/pdf" }, { ".zip", "application/zip" },
            { ".wasm", "application/wasm" }, { ".mp4", "video/mp4" }, { ".webm", "video/webm" },
            { ".mp3", "audio/mpeg" }, { ".ogg", "audio/ogg" }, { ".wav", "audio/wav" },
            { ".webmanifest", "application/manifest+json" }
        };

        static readonly object Sync = new object();

        /// <summary>Content type for an extension (".css") or file name, or null if unknown.</summary>
        public static string Get(string extensionOrFileName)
        {
            if (string.IsNullOrEmpty(extensionOrFileName)) return null;
            string ext = extensionOrFileName[0] == '.' ? extensionOrFileName : Path.GetExtension(extensionOrFileName);
            string type;
            lock (Sync) return Map.TryGetValue(ext ?? string.Empty, out type) ? type : null;
        }

        public static void Set(string extension, string contentType)
        {
            if (string.IsNullOrEmpty(extension) || extension[0] != '.') throw new ArgumentException("Extension must start with '.'.");
            lock (Sync) Map[extension] = contentType;
        }
    }

    public sealed class StaticFileOptions
    {
        /// <summary>URL prefix such as "/css/". Matched case-insensitively.</summary>
        public string RequestPrefix { get; set; }

        /// <summary>Folder to serve. A relative path is resolved against the application root.</summary>
        public string PhysicalPath { get; set; }

        /// <summary>Cache lifetime for unversioned requests. Zero or less sends "no-cache" (revalidate every time).</summary>
        public TimeSpan MaxAge { get; set; }

        /// <summary>When the URL carries the version query key (e.g. ?v=20261019), cache for a year as immutable.</summary>
        public bool ImmutableWhenVersioned { get; set; }

        public string VersionQueryKey { get; set; }

        /// <summary>Per-file override: return a max-age for the relative path, or null for the default.</summary>
        public Func<string, TimeSpan?> MaxAgeOverride { get; set; }

        /// <summary>Send ETag / Last-Modified and answer conditional requests with 304.</summary>
        public bool UseValidators { get; set; }

        /// <summary>Serve files whose extension is not in <see cref="MimeTypes"/> (as DefaultContentType). Off like IIS.</summary>
        public bool ServeUnknownFileTypes { get; set; }

        public string DefaultContentType { get; set; }

        /// <summary>When the file does not exist: false (default) answers 404, true returns false so the app can route it.</summary>
        public bool PassThroughWhenMissing { get; set; }

        public StaticFileOptions(string requestPrefix, string physicalPath)
        {
            RequestPrefix = requestPrefix;
            PhysicalPath = physicalPath;
            MaxAge = TimeSpan.FromHours(1);
            ImmutableWhenVersioned = true;
            VersionQueryKey = "v";
            UseValidators = true;
            DefaultContentType = "application/octet-stream";
        }
    }

    /// <summary>
    /// Serves files from a folder: MIME types, caching (Cache-Control, ETag, Last-Modified, 304),
    /// single byte ranges (206/416), HEAD, and path-traversal protection.
    /// </summary>
    public static class StaticFiles
    {
        const long OneYearSeconds = 365L * 24 * 3600;

        public static bool TryServe(string requestPrefix, string physicalPath)
        {
            HttpContext ctx = HttpContext.Current;
            if (ctx == null) throw new InvalidOperationException("StaticFiles.TryServe requires an active HttpContext.");
            return TryServe(ctx, new StaticFileOptions(requestPrefix, physicalPath));
        }

        public static bool TryServe(StaticFileOptions options)
        {
            HttpContext ctx = HttpContext.Current;
            if (ctx == null) throw new InvalidOperationException("StaticFiles.TryServe requires an active HttpContext.");
            return TryServe(ctx, options);
        }

        public static bool TryServe(HttpContext context, string requestPrefix, string physicalPath)
        {
            return TryServe(context, new StaticFileOptions(requestPrefix, physicalPath));
        }

        /// <summary>Returns true when the request was handled (file sent, 304, 404, 405, 416).</summary>
        public static bool TryServe(HttpContext context, StaticFileOptions options)
        {
            if (context == null) throw new ArgumentNullException("context");
            if (options == null) throw new ArgumentNullException("options");
            HttpRequest req = context.Request;
            HttpResponse res = context.Response;

            string prefix = options.RequestPrefix ?? "/";
            if (!prefix.EndsWith("/", StringComparison.Ordinal)) prefix += "/";
            string path = req.Path;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            string rel = path.Substring(prefix.Length);

            string root = ResolveRoot(options.PhysicalPath);
            string full = SafeCombine(root, rel);
            if (full == null || !File.Exists(full)) return NotFound(res, options);

            var fi = new FileInfo(full);
            if ((fi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) return NotFound(res, options);

            string contentType = MimeTypes.Get(fi.Extension);
            if (contentType == null)
            {
                if (!options.ServeUnknownFileTypes) return NotFound(res, options);
                contentType = options.DefaultContentType;
            }

            string method = req.HttpMethod;
            if (method != "GET" && method != "HEAD")
            {
                res.StatusCode = 405;
                res.AppendHeader("Allow", "GET, HEAD");
                res.ContentType = "text/plain";
                res.Write("Method Not Allowed");
                return true;
            }

            long length = fi.Length;
            DateTime lastModified = TruncateToSeconds(fi.LastWriteTimeUtc);
            string etag = "\"" + fi.LastWriteTimeUtc.Ticks.ToString("x", CultureInfo.InvariantCulture) + "-" + length.ToString("x", CultureInfo.InvariantCulture) + "\"";

            res.ContentType = contentType;
            res.CacheControl = CacheControlFor(req, rel, options);
            res.AppendHeader("Accept-Ranges", "bytes");
            if (options.UseValidators)
            {
                res.AppendHeader("ETag", etag);
                res.AppendHeader("Last-Modified", HttpDate.Format(lastModified));

                if (IsNotModified(req, etag, lastModified))
                {
                    res.StatusCode = 304;
                    return true;
                }
            }

            string range = req.Head.Headers.Get("Range");
            if (range != null && method == "GET" && IfRangeMatches(req, etag, lastModified))
            {
                long start, end;
                int r = ParseRange(range, length, out start, out end);
                if (r == 416)
                {
                    res.StatusCode = 416;
                    res.AppendHeader("Content-Range", "bytes */" + length.ToString(CultureInfo.InvariantCulture));
                    return true;
                }
                if (r == 206)
                {
                    res.StatusCode = 206;
                    res.AppendHeader("Content-Range", "bytes " + start + "-" + end + "/" + length);
                    res.TransmitFile(full, start, end - start + 1);
                    return true;
                }
            }

            res.TransmitFile(full);
            return true;
        }

        static bool NotFound(HttpResponse res, StaticFileOptions o)
        {
            if (o.PassThroughWhenMissing) return false;
            res.StatusCode = 404;
            res.ContentType = "text/plain";
            res.Write("Not Found");
            return true;
        }

        static string ResolveRoot(string physical)
        {
            if (string.IsNullOrEmpty(physical)) throw new InvalidOperationException("StaticFileOptions.PhysicalPath is required.");
            string root = Path.IsPathRooted(physical) ? physical : Path.Combine(WebApp.ApplicationPhysicalPath, physical);
            root = Path.GetFullPath(root);
            if (root[root.Length - 1] != Path.DirectorySeparatorChar) root += Path.DirectorySeparatorChar;
            return root;
        }

        /// <summary>Maps a decoded relative URL path into root, or null if it is unsafe or escapes root.</summary>
        public static string SafeCombine(string root, string rel)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(rel)) return null;
            root = Path.GetFullPath(root);
            if (root[root.Length - 1] != Path.DirectorySeparatorChar) root += Path.DirectorySeparatorChar;
            if (rel.StartsWith("~/", StringComparison.Ordinal)) rel = rel.Substring(2);
            else if (rel.StartsWith("/", StringComparison.Ordinal)) rel = rel.Substring(1);
            if (rel.Length == 0) return null;
            if (rel.IndexOf('\0') >= 0 || rel.IndexOf('\\') >= 0 || rel.IndexOf(':') >= 0) return null;
            string[] segments = rel.Split('/');
            foreach (string s in segments)
            {
                if (s.Length == 0 || s == "." || s == "..") return null;
                char last = s[s.Length - 1];
                if (last == '.' || last == ' ') return null; // Windows silently strips these
                for (int i = 0; i < s.Length; i++) if (s[i] < 0x20) return null;
            }
            string full;
            try { full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar))); }
            catch (Exception) { return null; }
            StringComparison cmp = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return full.StartsWith(root, cmp) ? full : null;
        }

        static string CacheControlFor(HttpRequest req, string rel, StaticFileOptions o)
        {
            if (o.ImmutableWhenVersioned && !string.IsNullOrEmpty(o.VersionQueryKey) && req.QueryString[o.VersionQueryKey] != null)
                return "public, max-age=" + OneYearSeconds + ", immutable";
            TimeSpan maxAge = o.MaxAge;
            if (o.MaxAgeOverride != null)
            {
                TimeSpan? over = o.MaxAgeOverride(rel);
                if (over.HasValue) maxAge = over.Value;
            }
            if (maxAge <= TimeSpan.Zero) return "no-cache";
            return "public, max-age=" + ((long)maxAge.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        internal static bool IsNotModified(HttpRequest req, string etag, DateTime lastModified)
        {
            string inm = req.Head.Headers.GetCombined("If-None-Match");
            if (inm != null)
            {
                foreach (string part in inm.Split(','))
                {
                    string t = part.Trim();
                    if (t == "*") return true;
                    if (t.StartsWith("W/", StringComparison.Ordinal)) t = t.Substring(2);
                    if (t == etag) return true;
                }
                return false; // If-None-Match takes precedence over If-Modified-Since
            }
            DateTime ims;
            if (HttpDate.TryParse(req.Head.Headers.Get("If-Modified-Since"), out ims)) return lastModified <= ims;
            return false;
        }

        internal static bool IfRangeMatches(HttpRequest req, string etag, DateTime lastModified)
        {
            string ifRange = req.Head.Headers.Get("If-Range");
            if (ifRange == null) return true;
            ifRange = ifRange.Trim();
            if (ifRange.StartsWith("\"", StringComparison.Ordinal)) return ifRange == etag;
            DateTime d;
            return HttpDate.TryParse(ifRange, out d) && d == lastModified;
        }

        /// <summary>200 = ignore the header, 206 = satisfiable single range, 416 = unsatisfiable.</summary>
        public static int ParseRange(string header, long length, out long start, out long end)
        {
            start = end = 0;
            string h = header.Trim();
            if (!h.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return 200;
            string spec = h.Substring(6).Trim();
            if (spec.IndexOf(',') >= 0) return 200; // multiple ranges: send the whole file
            int dash = spec.IndexOf('-');
            if (dash < 0) return 200;
            string a = spec.Substring(0, dash).Trim(), b = spec.Substring(dash + 1).Trim();
            long x, y;
            if (a.Length == 0)
            {
                if (!TryDigits(b, out y)) return 200;
                if (y == 0 || length == 0) return 416;
                start = Math.Max(0, length - y);
                end = length - 1;
                return 206;
            }
            if (!TryDigits(a, out x)) return 200;
            if (b.Length == 0) y = length - 1;
            else
            {
                if (!TryDigits(b, out y)) return 200;
                if (y < x) return 200;
                if (y > length - 1) y = length - 1;
            }
            if (x >= length) return 416;
            start = x;
            end = y;
            return 206;
        }

        static bool TryDigits(string s, out long v)
        {
            v = 0;
            if (s.Length == 0 || s.Length > 18) return false;
            foreach (char c in s)
            {
                if (c < '0' || c > '9') return false;
                v = v * 10 + (c - '0');
            }
            return true;
        }

        static DateTime TruncateToSeconds(DateTime d)
        {
            return new DateTime(d.Ticks - (d.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        }
    }
}
