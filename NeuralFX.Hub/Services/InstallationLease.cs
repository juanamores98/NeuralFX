using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace NeuralFX.Hub.Services
{
    internal sealed class InstallationLease : IDisposable
    {
        private readonly Mutex _mutex;
        public InstallationLease(string directory)
        {
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(directory).ToUpperInvariant())));
            _mutex = new Mutex(false, "Local\\NeuralFX_Install_" + key);
            bool acquired;
            // A killed process can signal exit just before its mutex becomes abandoned.
            // Allow that short handover while still rejecting an active writer promptly.
            try { acquired = _mutex.WaitOne(250); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) { _mutex.Dispose(); throw new IOException("Otra operación de NeuralFX está en curso."); }
        }
        public void Dispose() { _mutex.ReleaseMutex(); _mutex.Dispose(); }
    }
}
