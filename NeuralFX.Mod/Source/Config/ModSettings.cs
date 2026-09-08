using System;
using System.IO;
using System.Xml.Serialization;
using UnityEngine;

namespace NeuralFX.Config
{
    public class ModSettingsData
    {
        public bool ForceMotionVectorsOnLoad = true;
        public bool EnableCameraJitter = true;
        public bool EnableNativeMotionVectors = true;
        public bool EnhanceTextureClarity = true;
        public bool WarnOnAaConflict = true;
        public bool EnableHotkey = true;
        public bool EnableUui = true;
        public int SchemaVersion = 2;
        public float PanelX = 40f;
        public float PanelY = 80f;
    }

    public static class ModSettings
    {
        private static ModSettingsData _data = new ModSettingsData();
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
                            _data = loaded;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NeuralFX] Error cargando configuracion: " + ex.Message);
            }
        }

        public static void Save()
        {
            try
            {
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
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NeuralFX] Error guardando configuracion: " + ex.Message);
            }
        }
    }
}
