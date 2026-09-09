using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace NeuralFX.Hub.Models
{
    public enum ComponentGameState { Unknown, Missing, Incomplete, Unmanaged, Modified, Configured, Installed, DifferentCopy }
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
        private bool _isInGame = false;
        private bool _isInCache = false;
        private ComponentGameState _gameState;
        private string _gameStatusDetail = "Pendiente de verificación", _gameFilesDetail = "";
        public ComponentGameState GameState
        {
            get => _gameState;
            set { _gameState = value; OnPropertyChanged(); OnPropertyChanged(nameof(GameStatusText)); OnPropertyChanged(nameof(GameStatusFg)); OnPropertyChanged(nameof(GameStatusBg)); OnPropertyChanged(nameof(GameStatusBorder)); }
        }
        public string GameStatusDetail { get => _gameStatusDetail; set { _gameStatusDetail = value; OnPropertyChanged(); } }
        public string GameFilesDetail { get => _gameFilesDetail; set { _gameFilesDetail = value; OnPropertyChanged(); } }

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
        public string Version { get; set; } = "";
        public string Author { get; set; } = "";
        public string Distributor { get; set; } = "";
        public string LicenseStatus { get; set; } = "";
        public string TrustPolicy { get; set; } = "PinnedHash";
        public string ProvenanceDescription => "Versión: " + Version + "\nAutor: " + (Author.Length > 0 ? Author : "Ver fuente indicada") +
            "\nDistribuidor: " + (Distributor.Length > 0 ? Distributor : OfficialWebUrl) +
            "\nCondiciones: " + (LicenseStatus.Length > 0 ? LicenseStatus : "Consultar licencia de la fuente; no se certifican permisos por tener un hash") +
            "\nVerificación: " + (TrustPolicy == "PinnedFileHashAndNvidiaAuthenticode" ? "SHA-256 del archivo y firma Authenticode NVIDIA íntegra" : "Hash fijado del componente");
        // Exact archive suffix -> game-relative destination, including companion resources.
        public Dictionary<string, string> PackageFiles { get; set; } = new();
        public string? ReleaseRepository { get; set; }
        public string ReleaseTagPrefix { get; set; } = "";
        public Dictionary<string, string> ExpectedFileSha256 { get; set; } = new();
        internal Dictionary<string, string> AvailableChecksums { get; } = new(StringComparer.OrdinalIgnoreCase);

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
                    OnPropertyChanged(nameof(ImportActionText));
                }
            }
        }

        public bool IsInGame
        {
            get => _isInGame;
            set
            {
                if (_isInGame != value)
                {
                    _isInGame = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(GameStatusText));
                    OnPropertyChanged(nameof(GameStatusFg));
                    OnPropertyChanged(nameof(GameStatusBg));
                    OnPropertyChanged(nameof(GameStatusBorder));
                }
            }
        }

        public bool IsInCache
        {
            get => _isInCache;
            set
            {
                if (_isInCache != value)
                {
                    _isInCache = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CacheStatusText));
                    OnPropertyChanged(nameof(CacheStatusFg));
                    OnPropertyChanged(nameof(CacheStatusBg));
                    OnPropertyChanged(nameof(CacheStatusBorder));
                    OnPropertyChanged(nameof(CanDownload));
                    OnPropertyChanged(nameof(IsReady));
                    OnPropertyChanged(nameof(ImportActionText));
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
                    OnPropertyChanged(nameof(CacheStatusText));
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
        public bool CanDownload => CanAutoDownload && !IsInCache && !IsDownloading;
        public bool HasOfficialWeb => !string.IsNullOrEmpty(OfficialWebUrl);
        public bool CanImport => SourceType != "Embedded" && SourceType != "Bundled";
        public bool IsEmbedded => SourceType == "Embedded";
        public bool IsReady => IsInCache;

        // Visual Presentation Badges
        public string ImportActionText => IsInCache ? "Cambiar archivo…" : "Importar archivo…";
        public string GameStatusText => GameState switch
        {
            ComponentGameState.Installed => "INSTALADO",
            ComponentGameState.Configured => "INSTALADO · AJUSTADO",
            ComponentGameState.DifferentCopy => "INSTALADO · OTRA COPIA",
            ComponentGameState.Missing => "NO INSTALADO",
            ComponentGameState.Incomplete => "INCOMPLETO",
            ComponentGameState.Modified => "MODIFICADO",
            ComponentGameState.Unmanaged => "SIN REGISTRO",
            _ => "SIN VERIFICAR"
        };
        public string GameStatusFg => GameState is ComponentGameState.Installed or ComponentGameState.Configured ? "#75DEC6" : "#EFCA91";
        public string GameStatusBg => GameState is ComponentGameState.Installed or ComponentGameState.Configured ? "#1B382B" : "#3A3022";
        public string GameStatusBorder => GameState is ComponentGameState.Installed or ComponentGameState.Configured ? "#2E5A44" : "#675337";

        public string CacheStatusText
        {
            get
            {
                if (IsEmbedded) return "Se genera al instalar";
                if (SourceType == "Bundled") return IsInCache ? "Incluido en el Hub" : "Falta el puente en el Hub";
                if (IsInCache)
                {
                    double mb = FileSize / (1024.0 * 1024.0);
                    return mb >= 0.1 ? $"Copia lista · {mb:F1} MB" : "Copia lista · < 0.1 MB";
                }
                return CanAutoDownload ? "Falta descargar" : "Falta importar";
            }
        }

        public string CacheStatusFg
        {
            get
            {
                if (IsEmbedded) return "#4EC9B0";
                if (IsInCache) return "#569CD6";
                return "#CE9178";
            }
        }

        public string CacheStatusBg
        {
            get
            {
                if (IsEmbedded) return "#1E3A20";
                if (IsInCache) return "#1C2B38";
                return "#38251C";
            }
        }

        public string CacheStatusBorder
        {
            get
            {
                if (IsEmbedded) return "#2E5A35";
                if (IsInCache) return "#284A64";
                return "#553828";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
