using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

using static Atomex.Common.Libsodium.Interop.Libsodium;

namespace Atomex.Common.Libsodium
{
    public static class Sodium
    {
        private static readonly Action _misuseHandler = new(InternalError);

        private static int _initialized;
        public static bool IsInitialized => _initialized != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Initialize()
        {
            if (_initialized == 0)
            {
                InitializeCore();
                Interlocked.Exchange(ref _initialized, 1);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InitializeCore()
        {
            try
            {
                if (sodium_library_version_major() != SODIUM_LIBRARY_VERSION_MAJOR ||
                    sodium_library_version_minor() != SODIUM_LIBRARY_VERSION_MINOR)
                {
                    var version = Marshal.PtrToStringAnsi(sodium_version_string());
                    throw version != null && version != SODIUM_VERSION_STRING
                        ? new InvalidOperationException($"Libsodium version mismatch. Expected: {SODIUM_VERSION_STRING}. Actual: {version}.")
                        : new InvalidOperationException($"Libsodium version mismatch.");
                }

                if (sodium_set_misuse_handler(_misuseHandler) != 0)
                    throw new InvalidOperationException("Libsodium set misuse handelr error.");

                // sodium_init() returns 0 on success, -1 on failure, and 1 if the
                // library had already been initialized.

                if (sodium_init() < 0)
                    throw new InvalidOperationException("Libsodium init failed.");
            }
            catch (DllNotFoundException e)
            {
                throw new PlatformNotSupportedException("Libsodium dll not found.", e);
            }
            catch (BadImageFormatException e)
            {
                throw new PlatformNotSupportedException("Libsodium bad image.", e);
            }
        }

        private static void InternalError()
        {
            throw new InvalidOperationException("Libsodium internal error.");
        }
    }
}