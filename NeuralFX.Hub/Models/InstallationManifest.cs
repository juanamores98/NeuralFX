using System;
using System.Collections.Generic;

namespace NeuralFX.Hub.Models
{
    public class InstallationManifest
    {
        public string ToolName { get; set; } = "NeuralFX";
        public string Version { get; set; } = "2.0.0";
        public int SchemaVersion { get; set; }
        public DateTime InstalledAt { get; set; } = DateTime.UtcNow;
        public string GameDirectory { get; set; } = string.Empty;
        public List<string> InstalledFiles { get; set; } = new();
        public List<string> InstalledDirectories { get; set; } = new();
        public Dictionary<string, string> BackedUpFiles { get; set; } = new(); // Relative path -> game-local backup
        public Dictionary<string, string> FileChecksums { get; set; } = new(); // Relative path -> SHA256
        public Dictionary<string, string> BackupChecksums { get; set; } = new();
        public string? GameExecutableSha256 { get; set; }
        public string? PreviousManifestBackup { get; set; }
        public string? PreviousManifestSha256 { get; set; }
    }
}
