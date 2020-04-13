using System;
using System.Runtime.CompilerServices;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace Server.Network
{
    public class Pipe
    {
        public ref struct BufferWriter
        {
            public Span<byte> First;
            public Span<byte> Second;

            /// <summary>
            /// Gets or sets the current stream position.
            /// </summary>
            public int Position { get; private set; }

            /// <summary>
            /// Writes a 1-byte unsigned integer value to the underlying stream.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Write(byte value)
            {
                if (Position < First.Length)
                    First[Position++] = value;
                else if (Position < Length)
                {
                    Second[Position - First.Length] = value;
                    Position++;
                }
                else
                    throw new OutOfMemoryException();
            }

            /// <summary>
            /// Writes a 1-byte boolean value to the underlying stream. False is represented by 0, true by 1.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Write(bool value)
            {
                Write((byte)(value ? 1 : 0));
            }

            /// <summary>
            /// Writes a 1-byte signed integer value to the underlying stream.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Write(sbyte value)
            {
                Write((byte)value);
            }

            /// <summary>
            /// Writes a 2-byte signed integer value to the underlying stream.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Write(short value)
            {
                if (Position < First.Length)
                {
                    if (!BinaryPrimitives.TryWriteInt16BigEndian(First.Slice(Position), value))
                    {
                        // Not enough space. Split the spans
                        Write((byte)(value >> 8));
                        Write((byte)value);
                    }
                    else
                        Position += 2;
                }
                else if (BinaryPrimitives.TryWriteInt16BigEndian(Second.Slice(Position - First.Length), value))
                    Position += 2;
                else
                    throw new OutOfMemoryException();
            }

            /// <summary>
            /// Writes a 2-byte unsigned integer value to the underlying stream.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Write(ushort value)
            {
                if (Position < First.Length)
                {
                    if (!BinaryPrimitives.TryWriteUInt16BigEndian(First.Slice(Position), value))
                    {
                        // Not enough space. Split the spans
                        Write((byte)(value >> 8));
                        Write((byte)value);
                    }
                    else
                        Position += 2;
                }
                else if (BinaryPrimitives.TryWriteUInt16BigEndian(Second.Slice(Position - First.Length), value))
                    Position += 2;
                else
                    throw new OutOfMemoryException();
            }

            /// <summary>
            /// Writes a 4-byte signed integer value to the underlying stream.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Write(int value)
            {
                if (Position < First.Length)
                {
                    if (!BinaryPrimitives.TryWriteInt32BigEndian(First.Slice(Position), value))
                    {
                        // Not enough space. Split the spans
                        Write((byte)(value >> 24));
                        Write((byte)(value >> 16));
                        Write((byte)(value >> 8));
                        Write((byte)value);
                    }
                    else
                        Position += 4;
                }
                else if (BinaryPrimitives.TryWriteInt32BigEndian(Second.Slice(Position - First.Length), value))
                    Position += 4;
                else
                    throw new OutOfMemoryException();
            }

            /// <summary>
            /// Writes a 4-byte unsigned integer value to the underlying stream.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Write(uint value)
            {
                if (Position < First.Length)
                {
                    if (!BinaryPrimitives.TryWriteUInt32BigEndian(First.Slice(Position), value))
                    {
                        // Not enough space. Split the spans
                        Write((byte)(value >> 24));
                        Write((byte)(value >> 16));
                        Write((byte)(value >> 8));
                        Write((byte)value);
                    }
                    else
                        Position += 4;
                }
                else if (BinaryPrimitives.TryWriteUInt32BigEndian(Second.Slice(Position - First.Length), value))
                    Position += 4;
                else
                    throw new OutOfMemoryException();
            }

            /// <summary>
            /// Writes a sequence of bytes to the underlying stream
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Write(Span<byte> buffer)
            {
                if (Position < First.Length)
                {
                    var sz = Math.Min(buffer.Length, First.Length - Position);
                    buffer.CopyTo(First.Slice(Position));
                    buffer.Slice(sz).CopyTo(Second);
                    Position += buffer.Length;
                }
                else if (Position < Length)
                {
                    buffer.CopyTo(Second.Slice(Position - First.Length));
                }
                else
                    throw new OutOfMemoryException();
            }

            /// <summary>
            /// Writes a fixed-length ASCII-encoded string value to the underlying stream. To fit (size), the string content is either truncated or padded with null characters.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void WriteAsciiFixed(string value, int size)
            {
                if (value == null)
                    value = string.Empty;

                ReadOnlySpan<char> src;

                if (value.Length > size)
                    src = value.AsSpan(0, size);
                else
                    src = value.AsSpan();

                if (Position + size > Length)
                    throw new OutOfMemoryException();

                if (Position < First.Length)
                {
                    var sz = Math.Min(Encoding.ASCII.GetByteCount(src), First.Length - Position);

                    size -= Encoding.ASCII.GetBytes(src.Slice(0, sz), First.Slice(Position));
                    size -= Encoding.ASCII.GetBytes(src.Slice(sz), Second);
                }
                else
                    size -= Encoding.ASCII.GetBytes(src, Second.Slice(Position - First.Length));

                Position += src.Length;

                while (size > 0)
                {
                    Write((byte)0);
                    size--;
                }
            }

            /// <summary>
            /// Writes a dynamic-length ASCII-encoded string value to the underlying stream, followed by a 1-byte null character.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void WriteAsciiNull(string value)
            {
                if (value == null)
                    value = string.Empty;

                var src = value.AsSpan();
                var byteCount = Encoding.ASCII.GetByteCount(src);

                if (Position + byteCount + 1 > Length)
                    throw new OutOfMemoryException();

                if (Position < First.Length)
                {
                    var sz = Math.Min(byteCount, First.Length - Position);

                    Position += Encoding.ASCII.GetBytes(src.Slice(0, sz), First.Slice(Position));
                    Position += Encoding.ASCII.GetBytes(src.Slice(sz), Second);
                }
                else
                    Position += Encoding.ASCII.GetBytes(src, Second.Slice(Position - First.Length));

                Write((byte)0);
            }

            /// <summary>
            /// Writes a variable-length unicode string followed by a 2-byte null character.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe void WriteVariableLengthUnicode(string value, Encoding encoding)
            {
                if (value == null)
                    value = string.Empty;

                var src = value.AsSpan();
                var byteCount = Encoding.Unicode.GetByteCount(value);

                if (Position + byteCount + 2 > Length)
                    throw new OutOfMemoryException();

                if (Position < First.Length)
                {
                    if (Position + byteCount > First.Length)
                    {
                        // This string will cross the two spans

                        if ((First.Length - Position) % 2 != 0)
                        {
                            // The memory currently isn't aligned, so a single character will get split across
                            // the two spans. Fall back to a slow path.
                            Span<byte> tmp = stackalloc byte[2];

                            // TODO: This needs testing!

                            for (int i = 0; i < src.Length; i++)
                            {
                                encoding.GetBytes(src.Slice(i, 1), tmp);
                                Write(tmp[0]);
                                Write(tmp[1]);
                            }
                        }
                        else
                        {
                            var sz = (First.Length - Position) / 2;
                            Position += encoding.GetBytes(src.Slice(0, sz), First.Slice(Position));
                            Position += encoding.GetBytes(src.Slice(sz), Second);
                        }
                    }
                    else
                    {
                        Position += encoding.GetBytes(src, First.Slice(Position));
                    }
                }
                else
                    Position += encoding.GetBytes(src, Second.Slice(Position - First.Length));

                Write((ushort)0);
            }

            /// <summary>
            /// Writes a fixed-length unicode string value. To fit (size), the string content is either truncated or padded with null characters.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void WriteFixedLengthUnicode(string value, int size, Encoding encoding)
            {
                if (value == null)
                    value = string.Empty;

                ReadOnlySpan<char> src;

                if (value.Length > size)
                    src = value.AsSpan(0, size);
                else
                    src = value.AsSpan();

                var byteCount = encoding.GetByteCount(value);

                if (Position + (size * 2) > Length)
                    throw new OutOfMemoryException();

                if (Position < First.Length)
                {
                    if (Position + byteCount > First.Length)
                    {
                        // This string will cross the two spans

                        if ((First.Length - Position) % 2 != 0)
                        {
                            // The memory currently isn't aligned, so a single character will get split across
                            // the two spans. Fall back to a slow path.
                            Span<byte> tmp = stackalloc byte[2];

                            // TODO: This needs testing!

                            for (int i = 0; i < src.Length; i++)
                            {
                                encoding.GetBytes(src.Slice(i, 1), tmp);
                                Write(tmp[0]);
                                Write(tmp[1]);
                                size--;
                            }
                        }
                        else
                        {
                            var sz = (First.Length - Position) / 2;
                            encoding.GetBytes(src.Slice(0, sz), First.Slice(Position));
                            encoding.GetBytes(src.Slice(sz), Second);
                            size -= src.Length;
                        }
                    }
                    else
                    {
                        encoding.GetBytes(src, First.Slice(Position));
                        size -= src.Length;
                    }
                }
                else
                {
                    encoding.GetBytes(src, Second.Slice(Position - First.Length));
                    size -= src.Length;
                }

                Position += src.Length * 2;

                while (size > 0)
                {
                    Write((ushort)0);
                    size--;
                }
            }

            /// <summary>
            /// Writes a dynamic-length little-endian unicode string value to the underlying stream, followed by a 2-byte null character.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void WriteLittleUniNull(string value)
            {
                WriteVariableLengthUnicode(value, Encoding.Unicode);
            }

            /// <summary>
            /// Writes a fixed-length little-endian unicode string value to the underlying stream. To fit (size), the string content is either truncated or padded with null characters.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void WriteLittleUniFixed(string value, int size)
            {
                WriteFixedLengthUnicode(value, size, Encoding.Unicode);
            }

            /// <summary>
            /// Writes a dynamic-length big-endian unicode string value to the underlying stream, followed by a 2-byte null character.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void WriteBigUniNull(string value)
            {
                WriteVariableLengthUnicode(value, Encoding.BigEndianUnicode);
            }

            /// <summary>
            /// Writes a fixed-length big-endian unicode string value to the underlying stream. To fit (size), the string content is either truncated or padded with null characters.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void WriteBigUniFixed(string value, int size)
            {
                WriteFixedLengthUnicode(value, size, Encoding.BigEndianUnicode);
            }

            /// <summary>
            /// Fills the stream from the current position up to (capacity) with 0x00's
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Fill()
            {
                if (Position < First.Length)
                {
                    First.Slice(Position).Fill(0);
                    Second.Fill(0);
                }
                else
                    Second.Slice(Position - First.Length).Fill(0);

                Position = Length;
            }

            /// <summary>
            /// Writes a number of 0x00 byte values to the underlying stream.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Fill(int length)
            {
                if (Position < First.Length)
                {
                    var sz = Math.Min(length, First.Length - Position);

                    First.Slice(Position, sz).Fill(0);
                    Second.Slice(0, length - sz).Fill(0);
                }
                else
                    Second.Slice(Position - First.Length, length).Fill(0);

                Position += length;
            }

            /// <summary>
            /// Gets the total stream length.
            /// </summary>
            public int Length
            {
                get
                {
                    return First.Length + Second.Length;
                }
            }

            /// <summary>
            /// Offsets the current position from an origin.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int Seek(int offset, SeekOrigin origin)
            {
                switch (origin)
                {
                    case SeekOrigin.Begin:
                        Position = offset;
                        break;
                    case SeekOrigin.Current:
                        Position += offset;
                        break;
                    case SeekOrigin.End:
                        Position = Length - offset;
                        break;
                }

                return Position;
            }
        }

        public class PipeWriter
        {
            private Pipe _pipe;

            public PipeWriter(Pipe pipe)
            {
                _pipe = pipe;
            }

            public BufferWriter GetBytes()
            {
                BufferWriter sequence = new BufferWriter();

                var read = _pipe._readIdx;
                var write = _pipe._writeIdx;

                if (read <= write)
                {
                    var sz = Math.Min((read + _pipe.Size) - write - 1, _pipe.Size - write);

                    sequence.First = (sz == 0) ? Span<byte>.Empty : new Span<byte>(_pipe._buffer, (int)write, (int)sz);
                    sequence.Second = (read == 0) ? Span<byte>.Empty : new Span<byte>(_pipe._buffer, 0, (int)read);
                }
                else
                {
                    var sz = read - write - 1;

                    sequence.First = (sz == 0) ? new Span<byte>() : new Span<byte>(_pipe._buffer, (int)write, (int)sz);
                    sequence.Second = Span<byte>.Empty;
                }

                return sequence;
            }

            public void Advance(uint bytes)
            {
                var read = _pipe._readIdx;
                var write = _pipe._writeIdx;

                if (bytes == 0)
                    return;

                if (bytes > _pipe.Size - 1)
                {
                    throw new InvalidOperationException();
                }

                if (read <= write)
                {
                    if (bytes > (read + _pipe.Size) - write)
                    {
                        throw new InvalidOperationException();
                    }

                    var sz = Math.Min(bytes, _pipe.Size - write);

                    write += sz;
                    if (write > _pipe.Size - 1)
                    {
                        write = 0;
                    }
                    bytes -= sz;

                    if (bytes > 0)
                    {
                        if (bytes >= read)
                        {
                            throw new InvalidOperationException();
                        }

                        write = bytes;
                    }
                }
                else
                {
                    if (bytes > (read - write - 1))
                    {
                        throw new InvalidOperationException();
                    }

                    write += bytes;
                }

                _pipe._writeIdx = write;

                var continuation = _pipe._readerContinuation;

                if (continuation != null)
                {
                    _pipe._readerContinuation = null;
                    ThreadPool.UnsafeQueueUserWorkItem((state) => continuation(), true);
                }  
            }
        }

        public struct BufferReader
        {
            public ReadOnlyMemory<byte> First;
            public ReadOnlyMemory<byte> Second;
        }

        public class PipeReader : INotifyCompletion
        {
            private Pipe _pipe;

            private BufferReader _reader = new BufferReader();

            public PipeReader(Pipe pipe)
            {
                _pipe = pipe;
            }

            private void UpdateBufferReader()
            {
                var read = _pipe._readIdx;
                var write = _pipe._writeIdx;

                if (read <= write)
                {
                    _reader.First = ((write - read) == 0) ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>(_pipe._buffer, (int)read, (int)(write - read));
                    _reader.Second = ReadOnlyMemory<byte>.Empty;
                }
                else
                {
                    _reader.First = ((_pipe.Size - read) == 0) ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>(_pipe._buffer, (int)read, (int)(_pipe.Size - read));
                    _reader.Second = (write == 0) ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>(_pipe._buffer, 0, (int)write);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public uint BytesAvailable()
            {
                var read = _pipe._readIdx;
                var write = _pipe._writeIdx;

                if (read <= write)
                {
                    return write - read;
                }

                return (write + _pipe.Size) - read;
            }

            // The PipeReader itself is awaitable
            public PipeReader GetBytes()
            {
                if (BytesAvailable() > 0)
                {
                    UpdateBufferReader();
                }

                return this;
            }

            public void Advance(uint bytes)
            {
                var read = _pipe._readIdx;
                var write = _pipe._writeIdx;

                if (read <= write)
                {
                    if (bytes > (write - read))
                    {
                        throw new InvalidOperationException();
                    }

                    read += bytes;
                }
                else
                {
                    var sz = Math.Min(bytes, _pipe.Size - read);

                    read += sz;
                    if (read > _pipe.Size - 1)
                    {
                        read = 0;
                    }
                    bytes -= sz;

                    if (bytes > 0)
                    {
                        if (bytes > write)
                        {
                            throw new InvalidOperationException();
                        }

                        read = bytes;
                    }
                }

                _pipe._readIdx = read;
            }

            #region Awaitable

            // The following makes it possible to await the reader. Do not use any of this directly.

            public PipeReader GetAwaiter() => this;

            public bool IsCompleted { get { return BytesAvailable() > 0; } }

            public BufferReader GetResult()
            {
                UpdateBufferReader();
                return _reader;
            }

            // TODO: This may need a lock to avoid races around writes to the pipe and registering the continuation
            public void OnCompleted(Action continuation) => _pipe._readerContinuation = continuation;

            #endregion
        }

        private byte[] _buffer;
        private volatile uint _writeIdx;
        private volatile uint _readIdx;

        public PipeWriter Writer { get; private set; }
        public PipeReader Reader { get; private set; }

        public uint Size { get { return (uint)_buffer.Length; } }

        public Pipe(byte[] buf)
        {
            _buffer = buf;
            _writeIdx = 0;
            _readIdx = 0;

            Writer = new PipeWriter(this);
            Reader = new PipeReader(this);
        }

        #region Awaitable
        private volatile Action _readerContinuation = null;

        #endregion
    }
};
