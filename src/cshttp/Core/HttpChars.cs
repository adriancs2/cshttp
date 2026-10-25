using System;

namespace cshttp
{
    /// <summary>Character classes from RFC 9110 / RFC 9112.</summary>
    public static class HttpChars
    {
        static readonly bool[] TokenTable = BuildToken();

        static bool[] BuildToken()
        {
            var t = new bool[256];
            for (int c = '0'; c <= '9'; c++) t[c] = true;
            for (int c = 'a'; c <= 'z'; c++) t[c] = true;
            for (int c = 'A'; c <= 'Z'; c++) t[c] = true;
            foreach (char c in "!#$%&'*+-.^_`|~") t[c] = true;
            return t;
        }

        /// <summary>tchar: allowed in methods and header names.</summary>
        public static bool IsToken(byte b) { return TokenTable[b]; }

        public static bool IsToken(char c) { return c < 256 && TokenTable[c]; }

        /// <summary>field-vchar / obs-text / SP / HTAB. Rejects NUL, CR, LF and other controls.</summary>
        public static bool IsFieldValueByte(byte b) { return b == 9 || (b >= 0x20 && b != 0x7F); }

        /// <summary>Request-target bytes: visible, no space or control. Raw non-ASCII bytes are tolerated.</summary>
        public static bool IsTargetByte(byte b) { return b > 0x20 && b != 0x7F; }

        public static bool IsToken(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++) if (!IsToken(s[i])) return false;
            return true;
        }

        /// <summary>True when a header value can be sent safely (no CR, LF, NUL or other controls except HTAB).</summary>
        public static bool IsSafeFieldValue(string s)
        {
            if (s == null) return true;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\t') continue;
                if (c < 0x20 || c == 0x7F) return false;
            }
            return true;
        }

        public static int HexValue(byte b)
        {
            if (b >= '0' && b <= '9') return b - '0';
            if (b >= 'a' && b <= 'f') return b - 'a' + 10;
            if (b >= 'A' && b <= 'F') return b - 'A' + 10;
            return -1;
        }
    }

    /// <summary>Limits and strictness switches for the request parser.</summary>
    public sealed class ParserOptions
    {
        /// <summary>Longest accepted request line (method + target + version), excluding CRLF. Longer gives 414.</summary>
        public int MaxRequestLineLength { get; set; }

        /// <summary>Largest accepted head (request line + all header lines + blank line). Larger gives 431.</summary>
        public int MaxHeaderBytes { get; set; }

        /// <summary>Most header lines accepted. More gives 431.</summary>
        public int MaxHeaderCount { get; set; }

        /// <summary>Accept a bare LF as a line terminator. Off by default (strict CRLF guards against smuggling).</summary>
        public bool AllowBareLf { get; set; }

        public ParserOptions()
        {
            MaxRequestLineLength = 8 * 1024;
            MaxHeaderBytes = 32 * 1024;
            MaxHeaderCount = 100;
            AllowBareLf = false;
        }

        internal static readonly ParserOptions Shared = new ParserOptions();
    }
}
