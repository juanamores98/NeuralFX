using System;
using System.Diagnostics;
using System.IO;
using ColossalFramework.UI;
using ICities;
using NeuralFX.Config;
using NeuralFX.Protocol;
using UnityEngine;
namespace NeuralFX.Options
{
    internal static class OptionsPanel
    {
        public static void Build(UIHelperBase helper)
        {
            var status = helper.AddGroup("Estado de NeuralFX");
            var label = Note(status, "Esperando estado...");
            if (label != null) label.gameObject.AddComponent<OptionsStatus>().Label = label;
            var start = helper.AddGroup("Inicio");
            start.AddCheckbox("Activar procesamiento al cargar ciudad",ModSettings.PipelineEnabled,value => {
                ModSettings.PipelineEnabled=value;
                if (NeuralFXManager.Instance != null) NeuralFXManager.Instance.SetPipelineEnabled(value); else ModSettings.Save();
            });
            Note(start,"El menú no ejecuta inferencia. Apagar detiene feeder y CAS propios. ReShade conserva los demás efectos.");
            var ui = helper.AddGroup("Interfaz");
            ui.AddCheckbox("Botón Unified UI (se aplica al cargar ciudad)",ModSettings.EnableUui,value => {ModSettings.EnableUui=value;ModSettings.Save();});
            ui.AddCheckbox("Atajo Ctrl + Alt + N",ModSettings.EnableHotkey,value => {ModSettings.EnableHotkey=value;ModSettings.Save();});
            ui.AddCheckbox("Avisar sobre otros filtros de antialiasing",ModSettings.WarnOnAaConflict,value => {ModSettings.WarnOnAaConflict=value;ModSettings.Save();});
            ui.AddButton("Abrir/cerrar controles en ciudad",()=>NeuralFXManager.ToggleWindow());
            var experimental = helper.AddGroup("Pruebas experimentales");
            experimental.AddCheckbox("Permitir captura experimental de vectores Unity",ModSettings.ExperimentalOptIn,value => {ModSettings.ExperimentalOptIn=value;ModSettings.Save();});
            experimental.AddCheckbox("Preferir vectores Unity registrados",ModSettings.EnableNativeMotionVectors,value => {ModSettings.EnableNativeMotionVectors=value;ModSettings.Save();});
            Note(experimental,"La preferencia requiere permiso experimental y puente ABI 3. Cobertura de objetos y signo pendientes de validación; ante rechazo se utiliza el contrato óptico completo.");
            Note(experimental,"Jitter de cámara bloqueado: falta preparación y contingencia del consumidor. SR interno y separación de UI pendientes; no se anuncian como activos.");
            experimental.AddCheckbox("Capturar traza de cámaras (120 frames, límite 512 eventos)",ModSettings.EnableRenderTrace,value => {ModSettings.EnableRenderTrace=value;Rendering.RenderStageProbe.Restart();ModSettings.Save();});
            var tools = helper.AddGroup("Herramientas");
            var action = Note(tools, "");
            tools.AddButton("Abrir NeuralFX Hub",()=>Run(action,()=> {
                string exe=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NeuralFX/Hub/NeuralFX.Hub.exe");
                if (!File.Exists(exe)) throw new FileNotFoundException("Hub no instalado. Ejecuta Install-NeuralFX.ps1 desde el paquete compilado.");
                Process.Start(new ProcessStartInfo(exe){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(exe)});
                return "Hub abierto";
            }));
            tools.AddButton("Exportar estado y traza",()=>Run(action,()=> {
                string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NeuralFX/Diagnostics"); Directory.CreateDirectory(root);
                string file=Path.Combine(root,"session-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".txt");
                var manager=NeuralFXManager.Instance;
                File.WriteAllText(file,"NeuralFX bridge build 4 / ABI 3 / IPC 4\n"+(manager!=null?SessionViewState.Summary(manager.CurrentFrame)+"\n"+SessionViewState.Details(manager.CurrentFrame):"Sin ciudad")+"\n\n"+Rendering.RenderStageProbe.Export());
                return "Diagnóstico guardado: "+file;
            }));
            tools.AddButton("Reintentar guardado de preferencias",()=> {ModSettings.Save();if(action!=null)action.text=ModSettings.LastSaveError??"Preferencias guardadas";});
            Note(tools,ModSettings.MigrationNotice);
        }
        private static void Run(UILabel label,Func<string> action) {try {if(label!=null)label.text=action();else action();}catch(Exception ex){if(label!=null)label.text=ex.Message;UnityEngine.Debug.LogWarning("[NeuralFX] "+ex.Message);}}
        private static UILabel Note(UIHelperBase group,string text)
        {
            var helper=group as UIHelper; var panel=helper!=null?helper.self as UIPanel:null; if(panel==null)return null;
            var label=panel.AddUIComponent<UILabel>();label.autoSize=false;label.autoHeight=true;label.width=Mathf.Max(200,panel.width-24);label.wordWrap=true;label.textScale=.8f;label.text=text;return label;
        }
    }
    internal sealed class OptionsStatus:MonoBehaviour
    {
        public UILabel Label;
        private float _next;
        public void Update()
        {
            if(Label==null||Time.unscaledTime<_next)return;_next=Time.unscaledTime+.5f;
            var manager=NeuralFXManager.Instance;
            string value=manager!=null?SessionViewState.Summary(manager.CurrentFrame)+"\n"+manager.BridgeReason:"Sin ciudad. Instalación y compatibilidad se revisan en el Hub.";
            if(!string.IsNullOrEmpty(ModSettings.LastSaveError))value+="\nGuardado pendiente: "+ModSettings.LastSaveError;
            if(Label.text!=value)Label.text=value;
        }
    }
}
