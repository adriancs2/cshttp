using System;
using System.Collections.Specialized;
using System.Text;

namespace cshttp
{
    /// <summary>
    /// multipart/form-data parser (RFC 7578) over a fully received body. Zero-copy for files:
    /// each HttpPostedFile points into the body array. Malformed input throws HttpException(400).
    /// </summary>
    public static class MultipartParser
    {
        static readonly byte[] CrlfCrlf = { 13, 10, 13, 10 };

        public static void Parse(byte[] body, int offset, int count, string boundary, Encoding encoding,
            NameValueCollection form, HttpFileCollection files, RequestSettings settings)
        {
            if (settings == null) settings = RequestSettings.Shared;
            if (encoding == null) encoding = Encoding.UTF8;
            if (string.IsNullOrEmpty(boundary) || boundary.Length > 200)
                throw new HttpException(400, "Missing or invalid multipart boundary.");
            for (int i = 0; i < boundary.Length; i++)
                if (boundary[i] < 0x20 || boundary[i] > 0x7E) throw new HttpException(400, "Invalid multipart boundary.");

            byte[] dash = Encoding.ASCII.GetBytes("--" + boundary);
            byte[] crlfDash = Encoding.ASCII.GetBytes("\r\n--" + boundary);
            int end = offset + count;

            // Find the first delimiter: at the very start, or after a preamble ending with CRLF.
            int p;
            if (StartsWith(body, offset, end, dash)) p = offset + dash.Length;
            else
            {
                int idx = ByteScan.IndexOf(body, offset, count, crlfDash);
                if (idx < 0) throw new HttpException(400, "Multipart body has no boundary.");
                p = idx + crlfDash.Length;
            }

            int parts = 0;
            while (true)
            {
                // After a delimiter: "--" closes, otherwise optional padding then CRLF.
                if (p + 1 < end && body[p] == '-' && body[p + 1] == '-') return;
                while (p < end && (body[p] == ' ' || body[p] == '\t')) p++;
                if (p + 1 >= end || body[p] != 13 || body[p + 1] != 10)
                    throw new HttpException(400, "Malformed multipart delimiter.");
                p += 2;

                if (++parts > settings.MaxMultipartParts) throw new HttpException(400, "Too many multipart parts.");

                // Part headers.
                int headerEnd, dataStart;
                if (p + 1 < end && body[p] == 13 && body[p + 1] == 10)
                {
                    headerEnd = p;
                    dataStart = p + 2;
                }
                else
                {
                    int window = Math.Min(end - p, settings.MaxMultipartHeaderBytes + 4);
                    int idx = ByteScan.IndexOf(body, p, window, CrlfCrlf);
                    if (idx < 0) throw new HttpException(400, "Multipart part headers are too large or truncated.");
                    headerEnd = idx;
                    dataStart = idx + 4;
                }

                int next = ByteScan.IndexOf(body, dataStart, end - dataStart, crlfDash);
                if (next < 0) throw new HttpException(400, "Multipart body is truncated.");
                int dataLength = next - dataStart;

                string headerText = Encoding.UTF8.GetString(body, p, headerEnd - p);
                string disposition = null, contentType = null;
                foreach (string line in headerText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int colon = line.IndexOf(':');
                    if (colon <= 0) throw new HttpException(400, "Malformed multipart part header.");
                    string hname = line.Substring(0, colon).Trim();
                    string hvalue = line.Substring(colon + 1).Trim();
                    if (string.Equals(hname, "Content-Disposition", StringComparison.OrdinalIgnoreCase)) disposition = hvalue;
                    else if (string.Equals(hname, "Content-Type", StringComparison.OrdinalIgnoreCase)) contentType = hvalue;
                }

                string name = HeaderValue.GetParameter(disposition, "name");
                if (name != null)
                {
                    string fileNameStar = HeaderValue.GetParameter(disposition, "filename*");
                    string fileName = fileNameStar != null ? HeaderValue.DecodeExtValue(fileNameStar) : null;
                    if (fileName == null) fileName = HeaderValue.GetParameter(disposition, "filename");

                    if (fileName != null)
                    {
                        if (files.Count >= settings.MaxCollectionKeys) throw new HttpException(400, "Too many uploaded files.");
                        files.Add(name, new HttpPostedFile(fileName, contentType ?? "application/octet-stream", body, dataStart, dataLength));
                    }
                    else
                    {
                        if (form.Count >= settings.MaxCollectionKeys && form.GetValues(name) == null)
                            throw new HttpException(400, "The request contains more than " + settings.MaxCollectionKeys + " form keys.");
                        form.Add(name, encoding.GetString(body, dataStart, dataLength));
                    }
                }

                p = next + crlfDash.Length;
            }
        }

        static bool StartsWith(byte[] data, int offset, int end, byte[] prefix)
        {
            if (end - offset < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++) if (data[offset + i] != prefix[i]) return false;
            return true;
        }
    }
}
