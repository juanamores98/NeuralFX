using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Xml.Serialization;
using NeuralFX.Rendering;
using NeuralFX.Config;
using NeuralFX.Protocol;
using UnityEngine;
class Program {
 static int checks;
 static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
 static void Main(){
  Check(Marshal.SizeOf(typeof(NativeFrame))==64,"V3 size");Check(Marshal.SizeOf(typeof(NativeResult))==80,"Result size");
  Check(Marshal.OffsetOf(typeof(NativeFrame),"Camera").ToInt32()==32,"Camera offset");Check(Marshal.OffsetOf(typeof(NativeFrame),"Magic").ToInt32()==56,"Magic offset");
  var serializer=new XmlSerializer(typeof(ModSettingsData));
  var old=(ModSettingsData)serializer.Deserialize(new StringReader("<ModSettingsData><EnableCameraJitter>true</EnableCameraJitter><EnableNativeMotionVectors>true</EnableNativeMotionVectors><EnhanceTextureClarity>true</EnhanceTextureClarity></ModSettingsData>"));
  ModSettings.Migrate(old);Check(old.SchemaVersion==3&&!old.EnableCameraJitter&&!old.EnableNativeMotionVectors&&!old.ExperimentalOptIn,"Old config safe migration");Check(old.ImportedClarityPreference&&old.ImportedJitterPreference,"Imported preferences preserved");
  var camera=new Camera();var custom=new Matrix4x4();custom[2]=.17f;camera.projectionMatrix=custom;var clean=new Matrix4x4();clean[3]=.91f;camera.nonJitteredProjectionMatrix=clean;
  var temporal=new TemporalCamera {TestCamera=camera,Bridge=new NativeBridge()};temporal.Awake();
  for(int i=0;i<1000;i++){temporal.OnPreCull();temporal.OnPostRender();Check(camera.projectionMatrix[2]==.17f&&camera.nonJitteredProjectionMatrix[3]==.91f,"Disconnected camera altered");}
  Check(camera.depthTextureMode==DepthTextureMode.None,"Disconnected depth unchanged");
  var lease=new ProjectionLease();var jittered=custom;jittered[2]=.2f;lease.Apply(camera,jittered);lease.Restore();Check(camera.projectionMatrix[2]==.17f&&camera.nonJitteredProjectionMatrix[3]==.91f,"Exact custom restoration");
  lease.Apply(camera,jittered);var external=custom;external[2]=.3f;camera.projectionMatrix=external;lease.Restore();Check(camera.projectionMatrix[2]==.3f&&lease.Conflict,"External write must survive");
  for(uint caps=0;caps<128;caps++)Check(!FramePolicy.CanJitter(true,true,caps,false,true),"No jitter without preparation");
  Check(!FramePolicy.CanJitter(false,true,127,true,true),"Off jitter");Check(!FramePolicy.CanJitter(true,false,127,true,true),"No experimental consent");
  var frames=new FrameCoordinator();uint epoch=frames.Epoch;frames.Recreate();Check(FramePolicy.Newer(frames.Epoch,epoch),"Epoch monotonic");
  SystemInfo.graphicsDeviceType=UnityEngine.Rendering.GraphicsDeviceType.Direct3D12;var unsupported=new NativeBridge();Check(!unsupported.Connect()&&!unsupported.Connected,"Foreign graphics API must never register D3D11 pointers");SystemInfo.graphicsDeviceType=UnityEngine.Rendering.GraphicsDeviceType.Direct3D11;
  Check(Marshal.SizeOf(typeof(NativeControls))==24,"Controls layout");
  var modes=new CameraModeLease();camera.depthTextureMode=DepthTextureMode.Depth;modes.Acquire(camera);modes.Release();Check(camera.depthTextureMode==DepthTextureMode.Depth,"Exact original flags restored");
  modes.Acquire(camera);camera.depthTextureMode=(DepthTextureMode)7;modes.Release();Check((int)camera.depthTextureMode==7&&modes.Conflict,"Later camera flags preserved");
  var panel=new NeuralFX.UI.TelemetryPanel();panel.Toggle();Check(panel.Visible,"Panel opens");
  var view=ColossalFramework.UI.UIView.GetAView();view.fixedWidth=320;view.fixedHeight=240;panel.Update(new TelemetryFrame(),"status");
  var root=view.GetComponentsInChildren<ColossalFramework.UI.UIPanel>()[0];Check(root.width<=320&&root.height<=240,"Panel fits small viewport");
  var field=view.AddUIComponent<ColossalFramework.UI.UITextField>();field.containsFocus=true;Check(NeuralFX.UI.TelemetryPanel.HasTextFocus(),"Text focus prevents hotkey");field.containsFocus=false;Check(!NeuralFX.UI.TelemetryPanel.HasTextFocus(),"Hotkey released after edit");
  NeuralFX.Options.OptionsPanel.Build(new ColossalFramework.UI.UIHelper());
  Check(!new ModSettingsData().EnableTerrainDepthCandidate,"Candidate defaults to Off");
  var persisted=new ModSettingsData {SchemaVersion=3,EnableTerrainDepthCandidate=true};
  var xml=new StringWriter();serializer.Serialize(xml,persisted);
  Check(((ModSettingsData)serializer.Deserialize(new StringReader(xml.ToString()))).EnableTerrainDepthCandidate,"Candidate XML roundtrip");
  Check(!((ModSettingsData)serializer.Deserialize(new StringReader("<ModSettingsData><SchemaVersion>3</SchemaVersion></ModSettingsData>"))).EnableTerrainDepthCandidate,"Existing settings keep candidate Off");
  // Exercise actual menu callbacks without writing the user's settings file.
  typeof(ModSettings).GetField("_readOnly",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).SetValue(null,true);
  var boxes=ICities.UIHelperBase.Checkboxes;
  var terrain=boxes["Candidato: corregir profundidad del terreno"];
  Check(!terrain.isEnabled&&!terrain.Value,"Candidate gated by experimental opt-in");
  boxes["Permitir capacidades sin validar"].Set(true);
  Check(!terrain.isEnabled,"Candidate requires native motion");
  boxes["Preferir vectores de movimiento de Unity"].Set(true);
  Check(terrain.isEnabled,"Candidate available with native motion");
  terrain.Set(true);Check(ModSettings.EnableTerrainDepthCandidate,"Menu enables candidate");
  terrain.Set(false);Check(!ModSettings.EnableTerrainDepthCandidate,"Menu disables candidate");
  boxes["Permitir capacidades sin validar"].Set(false);Check(!terrain.isEnabled,"Disabling experiments gates candidate");
  var state=new TelemetryFrame {Flags=RuntimeFlags.CameraPresent|RuntimeFlags.PipelineRequested|RuntimeFlags.NativeConnected|RuntimeFlags.EvaluationSucceeded};
  Check(SessionViewState.Summary(state).Contains("NR sin confirmar"),"NGX does not confirm NR");
  Console.WriteLine(checks+" mod contract checks passed (Unity doubles; no GPU/game validation).");
 }
}
