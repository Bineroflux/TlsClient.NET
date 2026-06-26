using System;
using System.Buffers;

namespace TlsClient.Core.Helpers
{
    /// <summary>
    /// A UTF-8 JSON payload serialized into an <see cref="ArrayPool{T}"/>-rented buffer with a
    /// trailing NUL byte. Hand <see cref="TerminatedSpan"/> to the native cffi (which reads it as a
    /// C string via <c>C.GoString</c>) and <see cref="Dispose"/> once the native call has returned
    /// to give the buffer back to the pool.
    /// </summary>
    /// <remarks>
    /// The type doubles as the <see cref="IBufferWriter{T}"/> that
    /// <see cref="System.Text.Json.Utf8JsonWriter"/> writes into, so the JSON lands directly in the
    /// pooled buffer with no intermediate <c>string</c> or <c>byte[]</c> allocation. The
    /// <see cref="IBufferWriter{T}"/> surface is implemented explicitly so it stays off the normal
    /// public API — callers only see <see cref="TerminatedSpan"/> and <see cref="Dispose"/>.
    /// <para/>
    /// The buffer is NOT cleared on rent/return: only <see cref="TerminatedSpan"/> (bounded to the
    /// written length) is ever exposed, and the native side stops at the NUL we append, so stale
    /// pool bytes past the terminator are never read.
    /// </remarks>
    public sealed class PooledJsonPayload : IBufferWriter<byte>, IDisposable
    {
        private const int DefaultInitialCapacity = 4096;

        private byte[]? _buffer;
        private int _written;

        public PooledJsonPayload(int initialCapacity = DefaultInitialCapacity)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity < 1 ? 1 : initialCapacity);
        }

        /// <summary>
        /// The serialized JSON plus the trailing NUL byte — i.e. exactly the C string the native
        /// cffi expects. Valid until <see cref="Dispose"/> returns the buffer to the pool.
        /// </summary>
        public ReadOnlySpan<byte> TerminatedSpan
        {
            get
            {
                var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledJsonPayload));
                return new ReadOnlySpan<byte>(buffer, 0, _written);
            }
        }

        /// <summary>
        /// Appends the NUL terminator <c>C.GoString</c> relies on. UTF-8 never contains a 0x00 byte
        /// inside a multi-byte sequence, and System.Text.Json escapes U+0000 as <c>\u0000</c>, so
        /// this byte is the only zero in the buffer and marks the unambiguous end of the string.
        /// </summary>
        internal void AppendNulTerminator()
        {
            EnsureCapacity(1);
            _buffer![_written++] = 0;
        }

        void IBufferWriter<byte>.Advance(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            _written += count;
        }

        Memory<byte> IBufferWriter<byte>.GetMemory(int sizeHint)
        {
            EnsureCapacity(sizeHint);
            return _buffer!.AsMemory(_written);
        }

        Span<byte> IBufferWriter<byte>.GetSpan(int sizeHint)
        {
            EnsureCapacity(sizeHint);
            return _buffer!.AsSpan(_written);
        }

        private void EnsureCapacity(int sizeHint)
        {
            var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledJsonPayload));
            if (sizeHint < 1) sizeHint = 1;

            int required = _written + sizeHint;
            if (required <= buffer.Length) return;

            int newSize = Math.Max(required, buffer.Length * 2);
            var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
            Array.Copy(buffer, newBuffer, _written);
            ArrayPool<byte>.Shared.Return(buffer);
            _buffer = newBuffer;
        }

        public void Dispose()
        {
            var buffer = _buffer;
            if (buffer is null) return;
            _buffer = null;
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
