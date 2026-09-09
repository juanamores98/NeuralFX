using System;
using System.IO;
using System.Xml.Serialization;
using UnityEngine;

namespace NeuralFX.Config
{
    public class ModSettingsData
    {
        public bool ForceMotionVectorsOnLoad = false;
        public bool PipelineEnabled = true;
        public bool ExperimentalOptIn = false;
        public bool ImportedJitterPreference;
        public bool ImportedNativeMotionPreference;
        public bool ImportedClarityPreference;
        public bool EnableRenderTrace;
        public bool EnableCameraJitter = false;
        public bool EnableNativeMotionVectors = false;
        public bool EnhanceTextureClarity = false;
        public bool WarnOnAaConflict = true;
        public bool EnableHotkey = true;
        public bool EnableUui = true;
        public int SchemaVersion = 0;
        public float PanelX = 40f;
        public float PanelY = 80f;
    }

    public static class ModSettings
    {
        private static ModSettingsData _data = new ModSettingsData { SchemaVersion = 3 };
        public static string LastSaveError { get; private set; }
        private static bool _readOnly;
        public static bool PipelineEnabled { get { return _data.PipelineEnabled; } set { _data.PipelineEnabled = value; } }
        public static bool ExperimentalOptIn { get { return _data.ExperimentalOptIn; } set { _data.ExperimentalOptIn = value; } }
        public static bool EnableRenderTrace { get { return _data.EnableRenderTrace; } set { _data.EnableRenderTrace = value; } }
        public static string MigrationNotice { get { return _data.ImportedClarityPreference ? "Se retiró el ajuste global de LOD/anisotropía. Reinicia el juego para recuperar su configuración de inicio; no existe snapshot antiguo fiable." : ""; } }
        public static ModSettingsData Migrate(ModSettingsData data)
        {
            if (data.SchemaVersion < 3) {
                data.ImportedJitterPreference = data.EnableCameraJitter;
                data.ImportedNativeMotionPreference = data.EnableNativeMotionVectors;
                data.ImportedClarityPreference = data.EnhanceTextureClarity;
                data.EnableCameraJitter = data.EnableNativeMotionVectors = data.EnhanceTextureClarity = false;
                data.ForceMotionVectorsOnLoad = false; data.ExperimentalOptIn = false;
                data.SchemaVersion = 3;
            }
            return data;
        }
        public static float PanelX { get { return _data.PanelX; } set { _data.PanelX = value; } }
        public static float PanelY { get { return _data.PanelY; } set { _data.PanelY = value; } }

        public static bool ForceMotionVectorsOnLoad
        {
            get => _data.ForceMotionVectorsOnLoad;
            set => _data.ForceMotionVectorsOnLoad = value;
        }

        public static bool EnableCameraJitter
        {
            get => _data.EnableCameraJitter;
            set => _data.EnableCameraJitter = value;
        }

        public static bool EnableNativeMotionVectors
        {
            get => _data.EnableNativeMotionVectors;
            set => _data.EnableNativeMotionVectors = value;
        }

        public static bool EnhanceTextureClarity
        {
            get => _data.EnhanceTextureClarity;
            set => _data.EnhanceTextureClarity = value;
        }

        public static bool WarnOnAaConflict
        {
            get => _data.WarnOnAaConflict;
            set => _data.WarnOnAaConflict = value;
        }

        public static bool EnableHotkey
        {
            get => _data.EnableHotkey;
            set => _data.EnableHotkey = value;
        }

        public static bool EnableUui
        {
            get => _data.EnableUui;
            set => _data.EnableUui = value;
        }

        private static string GetConfigPath()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, Path.Combine("Colossal Order", Path.Combine("Cities_Skylines", "NeuralFX.xml")));
        }

        public static void Load()
        {
            try
            {
                string path = GetConfigPath();
                if (File.Exists(path))
                {
                    var serializer = new XmlSerializer(typeof(ModSettingsData));
                    using (var reader = new StreamReader(path))
                    {
                        var loaded = serializer.Deserialize(reader) as ModSettingsData;
                        if (loaded != null)
                        {
                            if (loaded.SchemaVersion > 3) { _readOnly = true; throw new InvalidDataException("Configuración de una versión posterior: no se sobrescribe."); }
                            _data = Migrate(loaded);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LastSaveError = ex.Message;
                Debug.LogWarning("[NeuralFX] Error cargando configuracion: " + ex.Message);
            }
        }

        public static bool Save()
        {
            try
            {
                if (_readOnly) throw new InvalidDataException("Archivo de versión posterior: se conserva sin cambios.");
                string path = GetConfigPath();
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var serializer = new XmlSerializer(typeof(ModSettingsData));
                string temporary = path + ".tmp";
                using (var writer = new StreamWriter(temporary))
                {
                    serializer.Serialize(writer, _data);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                LastSaveError = null; return true;
            }
            catch (Exception ex)
            {
                LastSaveError = ex.Message;
                Debug.LogWarning("[NeuralFX] Error guardando configuracion: " + ex.Message);
                return false;
            }
        }
    }
}
