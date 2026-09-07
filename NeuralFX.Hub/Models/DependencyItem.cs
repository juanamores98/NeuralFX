using System;

namespace NeuralFX.Hub.Models
{
    public enum DependencyStatus
    {
        Missing,
        InCache,
        InstalledInGame,
        Error
    }

    public enum DependencyCategory
    {
        Runtime,
        Addon,
        NvidiaProprietary,
        Shaders,
        Config
    }

    public class DependencyItem
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DependencyCategory Category { get; set; }
        public string TargetRelativePath { get; set; } = string.Empty;
        public string SourceType { get; set; } = "PublicDownload"; // "PublicDownload", "UserProvided", "Embedded"
        public string? DownloadUrl { get; set; }
        public string? ExpectedSha256 { get; set; }
        public DependencyStatus Status { get; set; } = DependencyStatus.Missing;
        public string? LocalCachedPath { get; set; }
        public long FileSize { get; set; }
        public string StatusMessage { get; set; } = "Pendiente";
        public bool IsRequired { get; set; } = true;
    }
}
