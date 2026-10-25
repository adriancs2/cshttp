using System;
using System.Collections.Specialized;
using System.Text;

namespace cshttp
{
    /// <summary>
    /// Percent-decoding at the byte level, then charset decoding. Invalid escapes such as
    /// "%zz" or a trailing "%" are kept literally (System.Web behaviour); they never throw.
    /// </summary>
    public static class PercentDecoder
    {
        public static string Decode(string value, bool plusAsSpace, Encoding encoding)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (value.IndexOf('%') < 0 && (!plusAsSpace || value.IndexOf('+') < 0)) return value;
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            return Decode(bytes, 0, bytes.Length, plusAsSpace, encoding ?? Encoding.UTF8);
        }

        public static string Decode(byte[] data, int offset, int count, bool plusAsSpace, Encoding encoding)
        {
            if (count <= 0) return string.Empty;
            if (encoding == null) encoding = Encoding.UTF8;
            byte[] output = null;
            int o = 0;
            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                byte b = data[i];
                if (b == (byte)'%' && i + 2 < end)
                {
                    int h1 = HttpChars.HexValue(data[i + 1]);
                    int h2 = HttpChars.HexValue(data[i + 2]);
                    if (h1 >= 0 && h2 >= 0)
                    {
                        if (output == null) output = Begin(data, offset, i - offset, count, out o);
                        output[o++] = (byte)((h1 << 4) | h2);
                        i += 2;
                        continue;
                    }
                }
                if (plusAsSpace && b == (byte)'+')
                {
                    if (output == null) output = Begin(data, offset, i - offset, count, out o);
                    output[o++] = (byte)' ';
                    continue;
                }
                if (output != null) output[o++] = b;
            }
            return output == null ? encoding.GetString(data, offset, count) : encoding.GetString(output, 0, o);
        }

        static byte[] Begin(byte[] data, int offset, int copied, int capacity, out int o)
        {
            byte[] output = new byte[capacity];
            Buffer.BlockCopy(data, offset, output, 0, copied);
            o = copied;
            return output;
        }
    }

    /// <summary>
    /// application/x-www-form-urlencoded and query-string parsing with System.Web semantics:
    /// a segment without '=' is added with a null key; duplicates are kept in order.
    /// </summary>
    public static class UrlEncodedParser
    {
        public static void Parse(string text, NameValueCollection target, Encoding encoding, int maxKeys)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (encoding == null) encoding = Encoding.UTF8;
            int l = text.Length;
            int i = 0;
            while (i < l)
            {
                int si = i, ti = -1;
                while (i < l)
                {
                    char ch = text[i];
                    if (ch == '=') { if (ti < 0) ti = i; }
                    else if (ch == '&') break;
                    i++;
                }
                string name = null, value;
                if (ti >= 0)
                {
                    name = PercentDecoder.Decode(text.Substring(si, ti - si), true, encoding);
                    value = PercentDecoder.Decode(text.Substring(ti + 1, i - ti - 1), true, encoding);
                }
                else value = PercentDecoder.Decode(text.Substring(si, i - si), true, encoding);
                AddChecked(target, name, value, maxKeys);
                if (i == l - 1 && text[i] == '&') AddChecked(target, null, string.Empty, maxKeys);
                i++;
            }
        }

        public static void Parse(byte[] data, int offset, int count, NameValueCollection target, Encoding encoding, int maxKeys)
        {
            if (count <= 0) return;
            if (encoding == null) encoding = Encoding.UTF8;
            int l = offset + count;
            int i = offset;
            while (i < l)
            {
                int si = i, ti = -1;
                while (i < l)
                {
                    byte b = data[i];
                    if (b == (byte)'=') { if (ti < 0) ti = i; }
                    else if (b == (byte)'&') break;
                    i++;
                }
                string name = null, value;
                if (ti >= 0)
                {
                    name = PercentDecoder.Decode(data, si, ti - si, true, encoding);
                    value = PercentDecoder.Decode(data, ti + 1, i - ti - 1, true, encoding);
                }
                else value = PercentDecoder.Decode(data, si, i - si, true, encoding);
                AddChecked(target, name, value, maxKeys);
                if (i == l - 1 && data[i] == (byte)'&') AddChecked(target, null, string.Empty, maxKeys);
                i++;
            }
        }

        static void AddChecked(NameValueCollection target, string name, string value, int maxKeys)
        {
            if (maxKeys > 0 && target.Count >= maxKeys && target.GetValues(name) == null)
                throw new HttpException(400, "The request contains more than " + maxKeys + " form or query keys.");
            target.Add(name, value);
        }
    }
}
