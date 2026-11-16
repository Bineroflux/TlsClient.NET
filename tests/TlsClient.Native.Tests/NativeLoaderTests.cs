using System;
using System.IO;
using System.Runtime.InteropServices;
using TlsClient.Native;
using Xunit;

namespace TlsClient.Core.Tests
{
    public class NativeLoaderTests
    {
        [Fact]
        public void LoadNativeAssembly_Resolves_Relative_Path_Against_Current_Directory()
        {
            var fullPath = Path.GetFullPath(NativeTestSetup.DllPath);
            Assert.True(File.Exists(fullPath), $"Set {NativeTestSetup.EnvVar} to an existing tls-client library (got '{fullPath}').");

            var relativePath = Path.GetRelativePath(Environment.CurrentDirectory, fullPath);
            if (Path.IsPathRooted(relativePath))
                return; // library on another drive than the current directory: no relative path exists

            // Loads the same file the other fixtures use, so the process keeps a single Go
            // runtime; the handle is deliberately not freed (a Go library must not be unloaded).
            var handle = NativeLoader.LoadNativeAssembly(relativePath);

            Assert.NotEqual(IntPtr.Zero, handle);
            Assert.NotEqual(IntPtr.Zero, NativeLoader.GetProcAddress(handle, "request"));
        }

        [Fact]
        public void LoadNativeAssembly_Throws_DllNotFound_For_Unknown_Library()
        {
            var error = Assert.Throws<DllNotFoundException>(() => NativeLoader.LoadNativeAssembly("tls-client-does-not-exist"));
            Assert.Contains("tls-client-does-not-exist", error.Message);
        }

        [Fact]
        public void GetLegacyLibraryPath_Points_To_The_Former_Package_Layout()
        {
            var path = NativeLoader.GetLegacyLibraryPath();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.ProcessArchitecture == Architecture.X64)
            {
                Assert.Equal(Path.Combine(AppContext.BaseDirectory, "runtimes", "tls-client", "win", "x64", "tls-client.dll"), path);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && RuntimeInformation.ProcessArchitecture == Architecture.X64)
            {
                Assert.Equal(Path.Combine(AppContext.BaseDirectory, "runtimes", "tls-client", "linux", "amd64", "tls-client.so"), path);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            {
                Assert.Equal(Path.Combine(AppContext.BaseDirectory, "runtimes", "tls-client", "darwin", "arm64", "tls-client.dylib"), path);
            }
        }
    }
}
