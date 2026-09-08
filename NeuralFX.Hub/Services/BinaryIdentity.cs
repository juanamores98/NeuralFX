using System;
using System.IO;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NeuralFX.Hub.Services
{
    internal static class BinaryIdentity
    {
        // Inspect metadata and Authenticode without loading code into the Hub.
        public static void ValidateNvidia(string path, string expectedName)
        {
            if (expectedName != "nvngx_dlss.dll" && expectedName != "nvngx_dlssnr.dll") throw new InvalidDataException("Runtime no admitido.");
            using (var stream = File.OpenRead(path))
            using (var pe = new PEReader(stream))
                if (pe.PEHeaders.CoffHeader.Machine != Machine.Amd64 || (pe.PEHeaders.CoffHeader.Characteristics & Characteristics.Dll) == 0)
                    throw new InvalidDataException("Se requiere una biblioteca PE x64.");
            var file = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = path };
            IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
            try
            {
                Marshal.StructureToPtr(file, pointer, false);
                var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, UnionChoice = 1, File = pointer, ProviderFlags = 0x1000 };
                Guid action = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
                int trustResult = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                bool authenticodeValid = trustResult == 0;

                using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
                string? signer = certificate.GetNameInfo(X509NameType.SimpleName, false);
                if (signer != "NVIDIA Corporation") throw new CryptographicException("La biblioteca no está firmada por NVIDIA Corporation.");

                // nvngx_dlss.dll requires untouched Authenticode signature.
                // nvngx_dlssnr.dll may have modified fatbins for RTX 40 / RTX 30/20 architectures.
                if (expectedName == "nvngx_dlss.dll" && !authenticodeValid)
                    throw new CryptographicException("Firma Authenticode no válida para nvngx_dlss.dll.");

                ValidateProductName(FileVersionInfo.GetVersionInfo(path).ProductName, expectedName);
            }
            finally { Marshal.DestroyStructure<TrustFile>(pointer); Marshal.FreeHGlobal(pointer); }
        }
        internal static void ValidateProductName(string? productName, string expectedName)
        {
            string requiredProduct = expectedName == "nvngx_dlssnr.dll" ? "NVIDIA DLSSNR" : "NVIDIA Deep Learning SuperSampling";
            if (!string.Equals(productName, requiredProduct, StringComparison.Ordinal))
                throw new InvalidDataException("El producto firmado es '" + productName + "'; se requiere '" + requiredProduct + "'. Renombrar otra DLL no cambia el runtime.");
        }
        [DllImport("wintrust.dll", ExactSpelling = true)] private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct TrustFile { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr Handle, KnownSubject; }
        [StructLayout(LayoutKind.Sequential)] private struct TrustData
        {
            public uint Size; public IntPtr Policy, Sip; public uint UiChoice, RevocationChecks, UnionChoice; public IntPtr File;
            public uint StateAction; public IntPtr State, Url; public uint ProviderFlags, UiContext;
        }
    }
}
