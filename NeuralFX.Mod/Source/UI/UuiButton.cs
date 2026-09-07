using System;
using System.Reflection;
using UnityEngine;

namespace NeuralFX.UI
{
    /// <summary>
    /// Registra el botón de NeuralFX en la barra flotante de Unified UI (UUI) mediante reflexión.
    /// Si UUI no está instalado, la llamada no genera errores y el mod opera con su atajo Ctrl+Alt+N.
    /// </summary>
    internal static class UuiButton
    {
        private static object _button;

        internal static bool Register(string title, string tooltip, Texture2D icon, Action<bool> onToggle)
        {
            if (_button != null)
            {
                return true;
            }

            try
            {
                Type helpers = FindType("UnifiedUI.Helpers.UUIHelpers");
                if (helpers == null)
                {
                    return false;
                }

                MethodInfo register = null;
                foreach (MethodInfo candidate in helpers.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (candidate.Name != "RegisterCustomButton")
                    {
                        continue;
                    }

                    ParameterInfo[] p = candidate.GetParameters();
                    if (p.Length >= 5 && p[0].ParameterType == typeof(string) && p[3].ParameterType == typeof(Texture2D))
                    {
                        register = candidate;
                        break;
                    }
                }

                if (register == null)
                {
                    return false;
                }

                ParameterInfo[] wanted = register.GetParameters();
                object[] args = new object[wanted.Length];
                args[0] = title;
                args[1] = null; // group
                args[2] = tooltip;
                args[3] = icon;
                args[4] = onToggle;
                for (int i = 5; i < wanted.Length; i++)
                {
                    args[i] = wanted[i].DefaultValue == DBNull.Value ? null : wanted[i].DefaultValue;
                }

                _button = register.Invoke(null, args);
                Debug.Log("[NeuralFX] Boton registrado exitosamente en Unified UI.");
                return _button != null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[NeuralFX] Registro en Unified UI omitido o fallido: " + e.Message);
                return false;
            }
        }

        internal static void Unregister()
        {
            try
            {
                if (_button == null)
                {
                    return;
                }

                MethodInfo destroy = _button.GetType().GetMethod("Destroy") ?? _button.GetType().GetMethod("Dispose");
                if (destroy != null)
                {
                    destroy.Invoke(_button, null);
                }
                Debug.Log("[NeuralFX] Boton de Unified UI desregistrado limpiamente.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[NeuralFX] Error al desregistrar de UUI: " + e.Message);
            }
            finally
            {
                _button = null;
            }
        }

        private static Type FindType(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type found = assembly.GetType(name, false);
                    if (found != null)
                    {
                        return found;
                    }
                }
                catch
                {
                    // Ignorar assemblies no inspeccionables
                }
            }

            return null;
        }
    }
}
