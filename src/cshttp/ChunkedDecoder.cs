using System;

namespace cshttp
{
    public enum ChunkedStatus
    {
        /// <summary>All input consumed; feed more bytes.</summary>
        NeedMoreData,
        /// <summary>A slice of body data is available at dataOffset/dataCount (inside the input buffer).</summary>
        Data,
        /// <summary>The terminating chunk and trailers were read. Bytes after Consumed belong to the next message.</summary>
        Complete,
        /// <summary>Malformed chunked encoding; see <see cref="ChunkedDecoder.ErrorMessage"/>.</summary>
        Error
    }

    /// <summary>
    /// Incremental, zero-copy decoder for "Transfer-Encoding: chunked" (RFC 9112 7.1).
    /// Each call returns at most one data slice that points into the caller's buffer.
    /// Chunk extensions and trailer fields are validated and discarded.
    /// </summary>
    public sealed class ChunkedDecoder
    {
        enum State { Size, AfterSize, Extension, SizeLf, Data, DataCr, DataLf, TrailerStart, TrailerLine, TrailerLineLf, FinalLf, Done, Failed }

        const byte CR = 13, LF = 10;

        State _state = State.Size;
        long _remaining;
        int _digits;
        int _lineLength;
        int _trailerBytes;

        /// <summary>Longest chunk-size line (with extensions) accepted.</summary>
        public int MaxLineLength { get; set; }

        /// <summary>Largest total trailer section accepted.</summary>
        public int MaxTrailerBytes { get; set; }

        public bool AllowBareLf { get; set; }

        public string ErrorMessage { get; private set; }

        public bool IsComplete { get { return _state == State.Done; } }

        /// <summary>Total decoded body bytes returned so far.</summary>
        public long TotalDataBytes { get; private set; }

        public ChunkedDecoder()
        {
            MaxLineLength = 4096;
            MaxTrailerBytes = 16 * 1024;
        }

        public ChunkedStatus Decode(byte[] buffer, int offset, int count, out int consumed, out int dataOffset, out int dataCount)
        {
            dataOffset = 0;
            dataCount = 0;
            int pos = offset, end = offset + count;
            while (true)
            {
                if (_state == State.Done) { consumed = pos - offset; return ChunkedStatus.Complete; }
                if (_state == State.Failed) { consumed = pos - offset; return ChunkedStatus.Error; }

                if (_state == State.Data)
                {
                    if (pos >= end) { consumed = pos - offset; return ChunkedStatus.NeedMoreData; }
                    int n = (int)Math.Min(_remaining, end - pos);
                    dataOffset = pos;
                    dataCount = n;
                    pos += n;
                    _remaining -= n;
                    TotalDataBytes += n;
                    if (_remaining == 0) _state = State.DataCr;
                    consumed = pos - offset;
                    return ChunkedStatus.Data;
                }

                if (pos >= end) { consumed = pos - offset; return ChunkedStatus.NeedMoreData; }
                byte b = buffer[pos++];

                switch (_state)
                {
                    case State.Size:
                    {
                        if (++_lineLength > MaxLineLength) return Fail(pos, offset, "Chunk size line too long.", out consumed);
                        int h = HttpChars.HexValue(b);
                        if (h >= 0)
                        {
                            if (_remaining > (long.MaxValue >> 4)) return Fail(pos, offset, "Chunk size too large.", out consumed);
                            _remaining = (_remaining << 4) | (uint)h;
                            _digits++;
                            break;
                        }
                        if (_digits == 0) return Fail(pos, offset, "Invalid chunk size.", out consumed);
                        if (b == ' ' || b == '\t') { _state = State.AfterSize; break; }
                        if (b == ';') { _state = State.Extension; break; }
                        if (b == CR) { _state = State.SizeLf; break; }
                        if (b == LF && AllowBareLf) { EndSizeLine(); break; }
                        return Fail(pos, offset, "Invalid chunk size line.", out consumed);
                    }
                    case State.AfterSize:
                        if (++_lineLength > MaxLineLength) return Fail(pos, offset, "Chunk size line too long.", out consumed);
                        if (b == ' ' || b == '\t') break;
                        if (b == ';') { _state = State.Extension; break; }
                        if (b == CR) { _state = State.SizeLf; break; }
                        if (b == LF && AllowBareLf) { EndSizeLine(); break; }
                        return Fail(pos, offset, "Invalid chunk size line.", out consumed);

                    case State.Extension:
                        if (++_lineLength > MaxLineLength) return Fail(pos, offset, "Chunk extension too long.", out consumed);
                        if (b == CR) { _state = State.SizeLf; break; }
                        if (b == LF && AllowBareLf) { EndSizeLine(); break; }
                        if ((b < 0x20 && b != 9) || b == 0x7F) return Fail(pos, offset, "Invalid character in chunk extension.", out consumed);
                        break;

                    case State.SizeLf:
                        if (b != LF) return Fail(pos, offset, "Bare CR in chunk size line.", out consumed);
                        EndSizeLine();
                        break;

                    case State.DataCr:
                        if (b == CR) { _state = State.DataLf; break; }
                        if (b == LF && AllowBareLf) { StartNextChunk(); break; }
                        return Fail(pos, offset, "Missing CRLF after chunk data.", out consumed);

                    case State.DataLf:
                        if (b != LF) return Fail(pos, offset, "Missing LF after chunk data.", out consumed);
                        StartNextChunk();
                        break;

                    case State.TrailerStart:
                        if (b == CR) { _state = State.FinalLf; break; }
                        if (b == LF && AllowBareLf) { _state = State.Done; break; }
                        if (!HttpChars.IsToken(b)) return Fail(pos, offset, "Invalid trailer field.", out consumed);
                        if (++_trailerBytes > MaxTrailerBytes) return Fail(pos, offset, "Trailers too large.", out consumed);
                        _state = State.TrailerLine;
                        break;

                    case State.TrailerLine:
                        if (++_trailerBytes > MaxTrailerBytes) return Fail(pos, offset, "Trailers too large.", out consumed);
                        if (b == CR) { _state = State.TrailerLineLf; break; }
                        if (b == LF && AllowBareLf) { _state = State.TrailerStart; break; }
                        if (!HttpChars.IsFieldValueByte(b)) return Fail(pos, offset, "Invalid character in trailer.", out consumed);
                        break;

                    case State.TrailerLineLf:
                        if (b != LF) return Fail(pos, offset, "Bare CR in trailer.", out consumed);
                        _state = State.TrailerStart;
                        break;

                    case State.FinalLf:
                        if (b != LF) return Fail(pos, offset, "Bare CR after last chunk.", out consumed);
                        _state = State.Done;
                        break;
                }
            }
        }

        void EndSizeLine()
        {
            _lineLength = 0;
            _digits = 0;
            _state = _remaining == 0 ? State.TrailerStart : State.Data;
        }

        void StartNextChunk()
        {
            _remaining = 0;
            _digits = 0;
            _lineLength = 0;
            _state = State.Size;
        }

        ChunkedStatus Fail(int pos, int offset, string message, out int consumed)
        {
            _state = State.Failed;
            ErrorMessage = message;
            consumed = pos - offset;
            return ChunkedStatus.Error;
        }
    }
}
