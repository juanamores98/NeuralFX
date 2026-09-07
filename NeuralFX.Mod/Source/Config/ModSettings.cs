using System;
using System.IO;
using System.Xml.Serialization;
using UnityEngine;

namespace NeuralFX.Config
{
    public class ModSettingsData
    {
        public bool ForceMotionVectorsOnLoad = true;
        public bool WarnOnAaConflict = true;
        public bool EnableHotkey = true;
        public bool EnableUui = true;
    }

    public static class ModSettings
    {
        private static ModSettingsData _data = new ModSettingsData();

        public static bool ForceMotionVectorsOnLoad
        {
            get => _data.ForceMotionVectorsOnLoad;
            set => _data.ForceMotionVectorsOnLoad = value;
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
                using (var writer = new StreamWriter(path))
                {
                    serializer.Serialize(writer, _data);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NeuralFX] Error guardando configuracion: " + ex.Message);
            }
        }
    }
}
