using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

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

    public class DependencyItem : INotifyPropertyChanged
    {
        private DependencyStatus _status = DependencyStatus.Missing;
        private string _statusMessage = "Pendiente";
        private bool _isDownloading = false;
        private string? _localCachedPath;
        private long _fileSize;

        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DependencyCategory Category { get; set; }
        public string TargetRelativePath { get; set; } = string.Empty;
        public string SourceType { get; set; } = "PublicDownload"; // "PublicDownload", "UserProvided", "Embedded"
        
        public string? DownloadUrl { get; set; }
        public string? OfficialWebUrl { get; set; }
        public bool CanAutoDownload { get; set; } = false;
        public string DownloadType { get; set; } = "Direct"; // "Direct", "ZipExtract", "ReShadeExeExtract"
        public string? ArchiveExtractFileName { get; set; }
        public string[]? Aliases { get; set; }
        public string? ExpectedSha256 { get; set; }

        public DependencyStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanDownload));
                    OnPropertyChanged(nameof(IsReady));
                }
            }
        }

        public string? LocalCachedPath
        {
            get => _localCachedPath;
            set
            {
                if (_localCachedPath != value)
                {
                    _localCachedPath = value;
                    OnPropertyChanged();
                }
            }
        }

        public long FileSize
        {
            get => _fileSize;
            set
            {
                if (_fileSize != value)
                {
                    _fileSize = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            set
            {
                if (_isDownloading != value)
                {
                    _isDownloading = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanDownload));
                }
            }
        }

        public bool IsRequired { get; set; } = true;

        // UI Helpers
        public bool CanDownload => CanAutoDownload && Status == DependencyStatus.Missing && !IsDownloading;
        public bool HasOfficialWeb => !string.IsNullOrEmpty(OfficialWebUrl);
        public bool CanImport => SourceType != "Embedded";
        public bool IsEmbedded => SourceType == "Embedded";
        public bool IsReady => Status == DependencyStatus.InCache || Status == DependencyStatus.InstalledInGame;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
