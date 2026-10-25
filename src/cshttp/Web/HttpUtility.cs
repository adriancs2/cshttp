using System;
using System.Collections.Specialized;
using System.Globalization;
using System.Text;

namespace cshttp
{
    /// <summary>Encoding helpers with the same output as System.Web.HttpUtility (.NET 4.x).</summary>
    public static class HttpUtility
    {
        /// <summary>Encodes &amp; &lt; &gt; " ' and chars 160-255 (as &amp;#N;).</summary>
        public static string HtmlEncode(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int i = 0;
            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<' || c == '>' || c == '"' || c == '\'' || c == '&' || (c >= 160 && c < 256)) break;
            }
            if (i == s.Length) return s;
            var sb = new StringBuilder(s.Length + 16);
            sb.Append(s, 0, i);
            for (; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&#39;"); break;
                    case '&': sb.Append("&amp;"); break;
                    default:
                        if (c >= 160 && c < 256) sb.Append("&#").Append(((int)c).ToString(CultureInfo.InvariantCulture)).Append(';');
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public static string HtmlEncode(object value)
        {
            return value == null ? string.Empty : HtmlEncode(Convert.ToString(value, CultureInfo.CurrentCulture));
        }

        /// <summary>Encodes &amp; " ' &lt; for use inside quoted attributes.</summary>
        public static string HtmlAttributeEncode(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&#39;"); break;
                    case '<': sb.Append("&lt;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        public static string HtmlDecode(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('&') < 0) return s;
            var sb = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '&')
                {
                    int semi = s.IndexOf(';', i + 1);
                    if (semi > i + 1 && semi - i <= 10)
                    {
                        string entity = s.Substring(i + 1, semi - i - 1);
                        string decoded = DecodeEntity(entity);
                        if (decoded != null)
                        {
                            sb.Append(decoded);
                            i = semi + 1;
                            continue;
                        }
                    }
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        static string DecodeEntity(string e)
        {
            if (e[0] == '#')
            {
                int code;
                bool ok = e.Length > 1 && (e[1] == 'x' || e[1] == 'X')
                    ? int.TryParse(e.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code)
                    : int.TryParse(e.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out code);
                if (!ok || code < 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF)) return null;
                return char.ConvertFromUtf32(code);
            }
            switch (e)
            {
                case "amp": return "&";
                case "lt": return "<";
                case "gt": return ">";
                case "quot": return "\"";
                case "apos": return "'";
                case "nbsp": return "\u00A0";
                case "copy": return "\u00A9";
                case "reg": return "\u00AE";
                default: return null;
            }
        }

        public static string UrlEncode(string s) { return UrlEncode(s, Encoding.UTF8); }

        /// <summary>Space becomes '+'; safe chars are letters, digits and - _ . ! * ( ); others %xx (lower-case hex).</summary>
        public static string UrlEncode(string s, Encoding encoding)
        {
            if (s == null) return null;
            byte[] bytes = (encoding ?? Encoding.UTF8).GetBytes(s);
            var sb = new StringBuilder(bytes.Length + 16);
            foreach (byte b in bytes)
            {
                char c = (char)b;
                if (IsUrlSafe(c)) sb.Append(c);
                else if (c == ' ') sb.Append('+');
                else sb.Append('%').Append(HexLower[b >> 4]).Append(HexLower[b & 15]);
            }
            return sb.ToString();
        }

        /// <summary>Encodes spaces as %20 and non-ASCII as UTF-8 escapes in the path part only.</summary>
        public static string UrlPathEncode(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int q = s.IndexOf('?');
            string path = q < 0 ? s : s.Substring(0, q);
            var sb = new StringBuilder();
            foreach (byte b in Encoding.UTF8.GetBytes(path))
            {
                if (b <= 0x20 || b >= 0x7F) sb.Append('%').Append(HexLower[b >> 4]).Append(HexLower[b & 15]);
                else sb.Append((char)b);
            }
            if (q >= 0) sb.Append(s, q, s.Length - q);
            return sb.ToString();
        }

        public static string UrlDecode(string s) { return PercentDecoder.Decode(s, true, Encoding.UTF8); }

        public static string UrlDecode(string s, Encoding encoding) { return PercentDecoder.Decode(s, true, encoding); }

        public static NameValueCollection ParseQueryString(string query) { return ParseQueryString(query, Encoding.UTF8); }

        public static NameValueCollection ParseQueryString(string query, Encoding encoding)
        {
            if (query == null) throw new ArgumentNullException("query");
            var c = new HttpValueCollection();
            if (query.Length > 0 && query[0] == '?') query = query.Substring(1);
            UrlEncodedParser.Parse(query, c, encoding, 0);
            return c;
        }

        /// <summary>Escapes a string for a JavaScript string literal; HTML-sensitive chars become \uXXXX.</summary>
        public static string JavaScriptStringEncode(string value, bool addDoubleQuotes)
        {
            var sb = new StringBuilder();
            if (addDoubleQuotes) sb.Append('"');
            if (value != null)
            {
                foreach (char c in value)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        case '\'': case '<': case '>': case '&':
                            AppendUnicode(sb, c); break;
                        default:
                            if (c < 0x20 || c == '\u2028' || c == '\u2029') AppendUnicode(sb, c);
                            else sb.Append(c);
                            break;
                    }
                }
            }
            if (addDoubleQuotes) sb.Append('"');
            return sb.ToString();
        }

        public static string JavaScriptStringEncode(string value) { return JavaScriptStringEncode(value, false); }

        static void AppendUnicode(StringBuilder sb, char c)
        {
            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
        }

        static bool IsUrlSafe(char c)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) return true;
            switch (c)
            {
                case '-': case '_': case '.': case '!': case '*': case '(': case ')': return true;
            }
            return false;
        }

        static readonly char[] HexLower = "0123456789abcdef".ToCharArray();
    }
}
