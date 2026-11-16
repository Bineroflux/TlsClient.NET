using System;
using System.IO;
using System.Runtime.InteropServices;

namespace TlsClient.Native
{
    /// <summary>
    /// Loads the bundled <c>tls-client</c> native library through the .NET runtime's
    /// native library resolver, the same machinery <c>[DllImport]</c> uses.
    /// <para/>
    /// When the consumer references a <c>TlsClient.Native.&lt;rid&gt;</c> NuGet package,
    /// the SDK emits the matching <c>runtimeTargets</c> entries in the consumer's
    /// <c>.deps.json</c>. The resolver walks the RID graph (so e.g.
    /// <c>linux-musl-x64</c> falls back to <c>linux-x64</c>), probes
    /// <c>runtimes/&lt;rid&gt;/native/</c> for framework-dependent layouts, and finds
    /// the binary next to the executable for self-contained / RID-specific publishes
    /// — no manual path construction required.
    /// </summary>
    public static class NativeLoader
    {
        /// <summary>
        /// Library name passed to the resolver (no extension, no path). The resolver
        /// adds the platform-specific prefix/suffix (<c>tls-client.dll</c> on Windows,
        /// <c>libtls-client.so</c> on Linux, <c>libtls-client.dylib</c> on macOS).
        /// </summary>
        public const string LibraryName = "tls-client";

        /// <summary>
        /// Returns the bare library name. Kept for backward compatibility with callers
        /// that previously fed the result into <see cref="LoadNativeAssembly"/>; the
        /// resolver does the actual path probing on demand inside that method.
        /// </summary>
        public static string GetLibraryPath() => LibraryName;

        /// <summary>
        /// Loads the native library. An explicit path passed to
        /// <c>NativeTlsClient.Initialize</c> — rooted, or relative to the current
        /// directory — is loaded directly. Otherwise the runtime resolver locates the
        /// library by name using the consumer's <c>.deps.json</c> and the assembly
        /// directory. As a last resort, the bundled library is also looked up where
        /// platform packages in the former layout put it
        /// (<c>runtimes/tls-client/&lt;os&gt;/&lt;arch&gt;/</c> next to the application).
        /// </summary>
        public static IntPtr LoadNativeAssembly(string libraryPath)
        {
            if (string.IsNullOrWhiteSpace(libraryPath))
                throw new ArgumentException("Library path or name must be provided.", nameof(libraryPath));

            if (Path.IsPathRooted(libraryPath) || File.Exists(libraryPath))
                return NativeLibrary.Load(Path.GetFullPath(libraryPath));

            if (NativeLibrary.TryLoad(
                    libraryPath,
                    typeof(NativeLoader).Assembly,
                    DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories,
                    out var handle))
                return handle;

            var legacyPath = libraryPath == LibraryName ? GetLegacyLibraryPath() : null;
            if (legacyPath != null && File.Exists(legacyPath))
                return NativeLibrary.Load(legacyPath);

            throw new DllNotFoundException(
                $"Unable to load the native library '{libraryPath}'. Reference the TlsClient.Native.<rid> package " +
                "for your platform, or pass the full path of the library to NativeTlsClient.Initialize.");
        }

        /// <summary>
        /// Where platform packages built before the switch to <c>runtimes/&lt;rid&gt;/native/</c>
        /// placed the library: <c>runtimes/tls-client/&lt;os&gt;/&lt;arch&gt;/tls-client.&lt;ext&gt;</c>
        /// under the application directory. <c>null</c> on platforms those packages never covered.
        /// </summary>
        internal static string? GetLegacyLibraryPath()
        {
            string? os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "darwin"
                : null;

            string? arch = (os, RuntimeInformation.ProcessArchitecture) switch
            {
                ("win", Architecture.X64) => "x64",
                ("win", Architecture.X86) => "x86",
                ("linux", Architecture.X64) => "amd64",
                ("linux", Architecture.X86) => "386",
                ("linux", Architecture.Arm64) => "arm64",
                ("linux", Architecture.Arm) => "armv7",
                ("darwin", Architecture.X64) => "amd64",
                ("darwin", Architecture.Arm64) => "arm64",
                _ => null,
            };

            if (os == null || arch == null)
                return null;

            string ext = os switch { "win" => "dll", "darwin" => "dylib", _ => "so" };
            return Path.Combine(AppContext.BaseDirectory, "runtimes", "tls-client", os, arch, $"tls-client.{ext}");
        }

        public static bool FreeNativeAssembly(IntPtr libraryHandle)
        {
            if (libraryHandle == IntPtr.Zero) return false;
            NativeLibrary.Free(libraryHandle);
            return true;
        }

        public static IntPtr GetProcAddress(IntPtr handle, string name)
        {
            return NativeLibrary.TryGetExport(handle, name, out var export) ? export : IntPtr.Zero;
        }
    }
}
