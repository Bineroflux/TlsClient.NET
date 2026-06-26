using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace TlsClient.Native.Wrappers
{
    public static unsafe class TlsClientWrapper
    {
        // The native cffi ABI is `char* fn(char* payload)` (see the cgo-generated header). Each
        // export is bound as an unmanaged function pointer and the caller's payload span is pinned
        // with `fixed` at the call site — no delegate marshalling thunk, no byte[] copy. The payload
        // must be NUL-terminated: the Go side reads it with C.GoString (strlen-based);
        // PooledJsonPayload appends that terminator.
        private static bool _isInitialized;
        private static IntPtr _module;

        private static delegate* unmanaged[Cdecl]<byte*, byte*> _request;
        private static delegate* unmanaged[Cdecl]<byte*, void> _freeMemory;
        private static delegate* unmanaged[Cdecl]<byte*, byte*> _getCookiesFromSession;
        private static delegate* unmanaged[Cdecl]<byte*, byte*> _addCookiesToSession;
        private static delegate* unmanaged[Cdecl]<byte*, byte*> _destroySession;
        private static delegate* unmanaged[Cdecl]<byte*> _destroyAll;

        // Streaming exports. Stay null when the loaded library was built from a tls-client version
        // without them; the streaming methods re-check and throw a clear error on first use.
        private static delegate* unmanaged[Cdecl]<byte*, byte*> _requestStream;
        private static delegate* unmanaged[Cdecl]<byte*, byte*> _readStream;
        private static delegate* unmanaged[Cdecl]<byte*, byte*> _readStreamAll;
        private static delegate* unmanaged[Cdecl]<byte*, byte*> _cancelStream;

        public static void Initialize(string? libraryPath = null)
        {
            if (_isInitialized) return;

            libraryPath ??= NativeLoader.GetLibraryPath();

            _module = NativeLoader.LoadNativeAssembly(libraryPath);

            if (_module == IntPtr.Zero)
                throw new DllNotFoundException($"Failed to load native library: {libraryPath}");

            _request = (delegate* unmanaged[Cdecl]<byte*, byte*>)GetExport("request");
            _freeMemory = (delegate* unmanaged[Cdecl]<byte*, void>)GetExport("freeMemory");
            _getCookiesFromSession = (delegate* unmanaged[Cdecl]<byte*, byte*>)GetExport("getCookiesFromSession");
            _addCookiesToSession = (delegate* unmanaged[Cdecl]<byte*, byte*>)GetExport("addCookiesToSession");
            _destroySession = (delegate* unmanaged[Cdecl]<byte*, byte*>)GetExport("destroySession");
            _destroyAll = (delegate* unmanaged[Cdecl]<byte*>)GetExport("destroyAll");

            // Streaming exports — optional. Older native libraries don't expose these, so the
            // lookup is allowed to fail silently (TryGetExport → null). The streaming wrapper
            // methods re-check and throw a clearer error when actually invoked.
            _requestStream = (delegate* unmanaged[Cdecl]<byte*, byte*>)TryGetExport("requestStream");
            _readStream = (delegate* unmanaged[Cdecl]<byte*, byte*>)TryGetExport("readStream");
            _readStreamAll = (delegate* unmanaged[Cdecl]<byte*, byte*>)TryGetExport("readStreamAll");
            _cancelStream = (delegate* unmanaged[Cdecl]<byte*, byte*>)TryGetExport("cancelStream");

            _isInitialized = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsureInitialized()
        {
            if (!_isInitialized)
                throw new InvalidOperationException("TlsClientStaticWrapper not initialized. Call Initialize(string libraryPath) first.");
        }

        private static void* GetExport(string functionName)
        {
            var functionPtr = NativeLoader.GetProcAddress(_module, functionName);
            if (functionPtr == IntPtr.Zero)
                throw new EntryPointNotFoundException($"Failed to get address of native function '{functionName}'.");

            return (void*)functionPtr;
        }

        // Returns null when the export is absent (used for the optional streaming functions).
        private static void* TryGetExport(string functionName)
            => (void*)NativeLoader.GetProcAddress(_module, functionName);

        private static EntryPointNotFoundException StreamingNotSupported(string functionName)
            => new EntryPointNotFoundException(
                $"Native function '{functionName}' is not exported by the loaded tls-client library. " +
                "Streaming requires a native library built with the streaming exports (requestStream, " +
                "readStream, readStreamAll, cancelStream). Update your native library.");

        // Copies the native-returned C string into a managed string. The native memory itself is
        // released separately via freeMemory(responseId) by the caller (see NativeTlsClient).
        private static string Decode(byte* result)
        {
            if (result is null)
                throw new InvalidOperationException("Native method returned a null pointer.");

            return Marshal.PtrToStringUTF8((IntPtr)result)
                ?? throw new InvalidOperationException("Failed to convert UTF-8 string from native pointer.");
        }

        // The native side reads the payload with C.GoString (strlen), so without a trailing NUL it
        // would read past the end of the buffer. Reject such payloads instead of handing them over.
        private static void EnsureNulTerminated(ReadOnlySpan<byte> payload)
        {
            if (payload.IsEmpty || payload[payload.Length - 1] != 0)
                throw new ArgumentException(
                    "The payload must be NUL-terminated UTF-8 JSON, e.g. RequestHelpers.PreparePooled(...).TerminatedSpan.",
                    nameof(payload));
        }

        // Compatibility for callers of the former byte[] overloads: appends the NUL terminator
        // when it is missing (by copying the payload).
        private static byte[] WithNulTerminator(byte[] payload)
        {
            if (payload is null) throw new ArgumentNullException(nameof(payload));
            if (payload.Length > 0 && payload[payload.Length - 1] == 0) return payload;

            var terminated = new byte[payload.Length + 1];
            Buffer.BlockCopy(payload, 0, terminated, 0, payload.Length);
            return terminated;
        }

        public static string Request(ReadOnlySpan<byte> payload)
        {
            EnsureInitialized();
            EnsureNulTerminated(payload);
            fixed (byte* p = payload)
                return Decode(_request(p));
        }

        /// <summary>Compatibility overload; appends the NUL terminator when it is missing.</summary>
        public static string Request(byte[] payload) => Request(new ReadOnlySpan<byte>(WithNulTerminator(payload)));

        public static void FreeMemory(string sessionId)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentException("sessionId cannot be null or empty.", nameof(sessionId));

            // freeMemory(char* id) also reads via C.GoString, so the id needs a NUL terminator.
            // Response ids are short ASCII GUIDs, so encode into a stack buffer to stay alloc-free.
            int max = Encoding.UTF8.GetMaxByteCount(sessionId.Length) + 1;
            Span<byte> buffer = max <= 256 ? stackalloc byte[256] : new byte[max];
            int written = Encoding.UTF8.GetBytes(sessionId, buffer);
            buffer[written] = 0;
            fixed (byte* p = buffer)
                _freeMemory(p);
        }

        public static string GetCookiesFromSession(ReadOnlySpan<byte> payload)
        {
            EnsureInitialized();
            EnsureNulTerminated(payload);
            fixed (byte* p = payload)
                return Decode(_getCookiesFromSession(p));
        }

        /// <summary>Compatibility overload; appends the NUL terminator when it is missing.</summary>
        public static string GetCookiesFromSession(byte[] payload) => GetCookiesFromSession(new ReadOnlySpan<byte>(WithNulTerminator(payload)));

        public static string AddCookiesToSession(ReadOnlySpan<byte> payload)
        {
            EnsureInitialized();
            EnsureNulTerminated(payload);
            fixed (byte* p = payload)
                return Decode(_addCookiesToSession(p));
        }

        /// <summary>Compatibility overload; appends the NUL terminator when it is missing.</summary>
        public static string AddCookiesToSession(byte[] payload) => AddCookiesToSession(new ReadOnlySpan<byte>(WithNulTerminator(payload)));

        public static string DestroySession(ReadOnlySpan<byte> payload)
        {
            EnsureInitialized();
            EnsureNulTerminated(payload);
            fixed (byte* p = payload)
                return Decode(_destroySession(p));
        }

        /// <summary>Compatibility overload; appends the NUL terminator when it is missing.</summary>
        public static string DestroySession(byte[] payload) => DestroySession(new ReadOnlySpan<byte>(WithNulTerminator(payload)));

        public static string DestroyAll()
        {
            EnsureInitialized();
            return Decode(_destroyAll());
        }

        public static string RequestStream(ReadOnlySpan<byte> payload)
        {
            EnsureInitialized();
            if (_requestStream == null) throw StreamingNotSupported("requestStream");
            EnsureNulTerminated(payload);
            fixed (byte* p = payload)
                return Decode(_requestStream(p));
        }

        public static string ReadStream(ReadOnlySpan<byte> payload)
        {
            EnsureInitialized();
            if (_readStream == null) throw StreamingNotSupported("readStream");
            EnsureNulTerminated(payload);
            fixed (byte* p = payload)
                return Decode(_readStream(p));
        }

        public static string ReadStreamAll(ReadOnlySpan<byte> payload)
        {
            EnsureInitialized();
            if (_readStreamAll == null) throw StreamingNotSupported("readStreamAll");
            EnsureNulTerminated(payload);
            fixed (byte* p = payload)
                return Decode(_readStreamAll(p));
        }

        public static string CancelStream(ReadOnlySpan<byte> payload)
        {
            EnsureInitialized();
            if (_cancelStream == null) throw StreamingNotSupported("cancelStream");
            EnsureNulTerminated(payload);
            fixed (byte* p = payload)
                return Decode(_cancelStream(p));
        }

        /// <summary>
        /// True when the loaded native library exposes the streaming exports
        /// (<c>requestStream</c>, <c>readStream</c>, <c>readStreamAll</c>, <c>cancelStream</c>).
        /// Older builds of the tls-client library may not expose these, in which case the streaming
        /// methods on <see cref="TlsClient.Native.NativeTlsClient"/> will throw on first use.
        /// </summary>
        public static bool IsStreamingSupported
        {
            get
            {
                EnsureInitialized();
                return _requestStream != null
                    && _readStream != null
                    && _readStreamAll != null
                    && _cancelStream != null;
            }
        }

        public static void Destroy()
        {
            if (!_isInitialized)
                return;

            _ = DestroyAll();

            var module = _module;
            try
            {
                if (module != IntPtr.Zero)
                {
                    NativeLoader.FreeNativeAssembly(module);
                }
            }
            finally
            {
                _request = null;
                _freeMemory = null;
                _getCookiesFromSession = null;
                _addCookiesToSession = null;
                _destroySession = null;
                _destroyAll = null;
                _requestStream = null;
                _readStream = null;
                _readStreamAll = null;
                _cancelStream = null;

                _module = IntPtr.Zero;

                _isInitialized = false;
            }
        }

    }
}
