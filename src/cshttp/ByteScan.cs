using System;
using System.Text;

namespace cshttp
{
    /// <summary>
    /// Small byte-search helpers. This is the only place with runtime-specific code:
    /// on .NET 8+ the span overloads are vectorized; on .NET Framework the plain
    /// array versions are used (no System.Memory package needed).
    /// All indexes returned are absolute positions in the array, or -1.
    /// </summary>
    public static class ByteScan
    {
        public static int IndexOf(byte[] data, int offset, int count, byte value)
        {
            if (count <= 0) return -1;
#if NET8_0_OR_GREATER
            int i = new ReadOnlySpan<byte>(data, offset, count).IndexOf(value);
            return i < 0 ? -1 : offset + i;
#else
            return Array.IndexOf<byte>(data, value, offset, count);
#endif
        }

        public static int IndexOf(byte[] data, int offset, int count, byte[] pattern)
        {
            if (pattern == null || pattern.Length == 0) return offset;
            if (count < pattern.Length) return -1;
#if NET8_0_OR_GREATER
            int i = new ReadOnlySpan<byte>(data, offset, count).IndexOf(new ReadOnlySpan<byte>(pattern));
            return i < 0 ? -1 : offset + i;
#else
            // First-byte fast skip, then verify the rest of the pattern.
            int last = offset + count - pattern.Length; // last valid start position
            byte first = pattern[0];
            int pos = offset;
            while (pos <= last)
            {
                int i = Array.IndexOf<byte>(data, first, pos, last - pos + 1);
                if (i < 0) return -1;
                int k = 1;
                while (k < pattern.Length && data[i + k] == pattern[k]) k++;
                if (k == pattern.Length) return i;
                pos = i + 1;
            }
            return -1;
#endif
        }

        /// <summary>Decodes bytes 1:1 into chars (ISO-8859-1). Lossless for any byte.</summary>
        public static string Latin1(byte[] data, int offset, int count)
        {
            if (count <= 0) return string.Empty;
#if NET8_0_OR_GREATER
            return Encoding.Latin1.GetString(data, offset, count);
#else
            char[] c = new char[count];
            for (int i = 0; i < count; i++) c[i] = (char)data[offset + i];
            return new string(c);
#endif
        }

        /// <summary>Encodes chars to bytes 1:1; chars above 0xFF become '?'.</summary>
        public static byte[] Latin1Bytes(string s)
        {
            byte[] b = new byte[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                b[i] = c <= '\u00FF' ? (byte)c : (byte)'?';
            }
            return b;
        }

        public static bool EqualsAsciiIgnoreCase(byte[] data, int offset, int count, string s)
        {
            if (count != s.Length) return false;
            for (int i = 0; i < count; i++)
            {
                int a = data[offset + i];
                int b = s[i];
                if (a == b) continue;
                int la = a | 0x20;
                if (la >= 'a' && la <= 'z' && la == (b | 0x20)) continue;
                return false;
            }
            return true;
        }

        public static bool EqualsAscii(byte[] data, int offset, int count, string s)
        {
            if (count != s.Length) return false;
            for (int i = 0; i < count; i++)
                if (data[offset + i] != s[i]) return false;
            return true;
        }
    }
}
