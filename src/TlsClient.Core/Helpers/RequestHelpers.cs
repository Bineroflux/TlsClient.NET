using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TlsClient.Core.Helpers
{
    public static class RequestHelpers
    {
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        public static string ToBase64(this byte[] data) => Convert.ToBase64String(data);
        public static byte[] FromBase64(this string base64) => Convert.FromBase64String(base64);

        public static string ToJson(this object data) => JsonSerializer.Serialize(data, _jsonOptions);

        public static T FromJson<T>(this string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return default!;
            return JsonSerializer.Deserialize<T>(json, _jsonOptions)!;
        }

        private static byte[] ToBytes(string data) => Encoding.UTF8.GetBytes(data);
        public static string ToStringFromBytes(this byte[] data) => Encoding.UTF8.GetString(data);

        /// <summary>
        /// Serializes <paramref name="data"/> to UTF-8 JSON bytes, without a NUL terminator.
        /// Kept for existing callers; the native client uses <see cref="PreparePooled"/>.
        /// </summary>
        public static byte[] Prepare(object data) => ToBytes(ToJson(data));
        public static string PrepareBody(byte[] data) => Convert.ToBase64String(data);

        /// <summary>
        /// Serializes <paramref name="data"/> to UTF-8 JSON straight into a pooled buffer and
        /// appends the NUL terminator the native cffi requires (it reads the pointer with
        /// <c>C.GoString</c>, which is strlen-based). No intermediate <c>string</c> / <c>byte[]</c>
        /// is allocated — the JSON is written directly into an <see cref="System.Buffers.ArrayPool{T}"/>
        /// buffer via <see cref="Utf8JsonWriter"/>.
        /// <para/>
        /// The caller MUST dispose the returned payload (returning the buffer to the pool) once the
        /// native call has completed — typically <c>using var payload = RequestHelpers.PreparePooled(x);</c>
        /// then pass <see cref="PooledJsonPayload.TerminatedSpan"/> to the native wrapper.
        /// </summary>
        public static PooledJsonPayload PreparePooled(object data)
        {
            var payload = new PooledJsonPayload();
            try
            {
                // Serializing to an existing Utf8JsonWriter takes indentation from the writer, not
                // from the JsonSerializerOptions, so mirror WriteIndented here to keep the wire bytes
                // identical to the old ToJson()-based path.
                using (var writer = new Utf8JsonWriter(payload, new JsonWriterOptions { Indented = _jsonOptions.WriteIndented }))
                {
                    JsonSerializer.Serialize(writer, data, data.GetType(), _jsonOptions);
                }
                payload.AppendNulTerminator();
            }
            catch
            {
                // Dispose ONLY on failure. A finally here would return the buffer to the pool
                // before the caller reads TerminatedSpan; on success ownership transfers to the
                // caller's `using`.
                payload.Dispose();
                throw;
            }
            return payload;
        }
    }
}
