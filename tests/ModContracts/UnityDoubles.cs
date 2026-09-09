// Deliberately limited Unity doubles: contract tests, never game/GPU evidence.
using System;
namespace UnityEngine {
 public class Object {public string name; public GameObject gameObject {get{return new GameObject();}} public static void Destroy(Object o) {}}
 public class GameObject:Object {public GameObject(){}public GameObject(string s){name=s;}public T AddComponent<T>() where T:new(){return new T();}public T GetComponent<T>() where T:class{return null;}}

 public class MonoBehaviour:Object {public bool enabled=true;public Camera TestCamera; public T GetComponent<T>() where T:class {return TestCamera as T;}}
 public class Camera:Object {
  public static Camera main;public static event Action<Camera> onPreRender,onPostRender;
  public float depth;public string actualRenderingPath="Deferred";public RenderTexture targetTexture;
  public T[] GetComponents<T>(){return new T[0];}

  public Matrix4x4 projectionMatrix,nonJitteredProjectionMatrix; public DepthTextureMode depthTextureMode;
  public Transform transform=new Transform();public int pixelWidth=1920,pixelHeight=1080;public float fieldOfView=60;
  public int GetInstanceID(){return 42;} public void AddCommandBuffer(Rendering.CameraEvent e,Rendering.CommandBuffer b){}public void RemoveCommandBuffer(Rendering.CameraEvent e,Rendering.CommandBuffer b){}
 }
 public class Transform {public Vector3 position;public Quaternion rotation;}
 public struct Vector3 {public float x,y,z; public Vector3(float a,float b,float c=0){x=a;y=b;z=c;}public static float Distance(Vector3 a,Vector3 b){return 0;}}
 public struct Quaternion {public static float Angle(Quaternion a,Quaternion b){return 0;}}
 public struct Matrix4x4 {private float[] _data; public float this[int i] {get{return _data==null?0:_data[i];} set{var copy=_data==null?new float[16]:(float[])_data.Clone();copy[i]=value;_data=copy;}}}
 [Flags]public enum DepthTextureMode {None=0,Depth=1,MotionVectors=4}
 public enum RenderTextureFormat {RGHalf}public enum RenderTextureReadWrite {Linear}public enum FilterMode {Point}
 public class RenderTexture:Object {public RenderTexture(int w,int h,int d,RenderTextureFormat f,RenderTextureReadWrite r){}public string name;public bool useMipMap;public FilterMode filterMode; public bool Create(){return true;}public IntPtr GetNativeTexturePtr(){throw new Exception("No pointer acquisition expected without consent");}}
 public static class SystemInfo {public static bool SupportsRenderTextureFormat(RenderTextureFormat f){return true;}}
 public static class Mathf {public static float Max(float a,float b){return Math.Max(a,b);}public static float Abs(float a){return Math.Abs(a);}public static float Min(float a,float b){return Math.Min(a,b);}public static float Clamp(float v,float a,float b){return Max(a,Min(v,b));}}
 public static class Time {public static float timeScale=1,unscaledTime,unscaledDeltaTime;public static int frameCount;}
 public static class Debug {public static void LogWarning(object m){}public static void Log(object m){}public static void LogError(object m){}}
}
namespace UnityEngine.Rendering {
 public enum CameraEvent {AfterEverything}public enum BuiltinRenderTextureType {MotionVectors}
 public struct RenderTargetIdentifier {public RenderTargetIdentifier(UnityEngine.RenderTexture t){}}
 public class CommandBuffer {public string name;public void Clear(){}public void Release(){}public void IssuePluginEvent(IntPtr p,int f){}public void Blit(BuiltinRenderTextureType t,RenderTargetIdentifier d){}}
}

namespace UnityEngine {
 public static class Screen {public static int width=1920,height=1080;}
 public enum KeyCode {LeftControl,RightControl,LeftAlt,RightAlt,N}
 public static class Input {public static bool GetKey(KeyCode k){return false;}public static bool GetKeyDown(KeyCode k){return false;}}
 public struct Vector2 {public float x,y;public Vector2(float a,float b){x=a;y=b;}public static float Distance(Vector2 a,Vector2 b){return 0;}}
 public struct Color32 {public Color32(byte r,byte g,byte b,byte a){}public static implicit operator Color(Color32 c){return new Color();}}
 public struct Color {public Color(float r,float g,float b,float a){}public static Color Lerp(Color a,Color b,float t){return a;}}
 public enum TextureFormat {ARGB32}
 public class Texture2D:Object {public Texture2D(int w,int h,TextureFormat f,bool m){}public void SetPixel(int x,int y,Color c){}public void Apply(bool a,bool b){}}
}
namespace ColossalFramework.UI {
 using UnityEngine;using System.Collections.Generic;
 public enum UIOrientation {Vertical}
 public class UIComponent:Object {
  public float width=460,height=30;public bool isVisible,isInteractive,clipChildren,containsFocus;public Vector3 relativePosition;
  public Vector2 size {get{return new Vector2(width,height);}set{width=value.x;height=value.y;}}
  public string tooltip;public readonly List<UIComponent> Children=new List<UIComponent>();
  public T AddUIComponent<T>() where T:UIComponent,new(){var child=new T();Children.Add(child);return child;}
  public UIComponent AddUIComponent(Type t){var child=(UIComponent)Activator.CreateInstance(t);Children.Add(child);return child;}
  public T[] GetComponentsInChildren<T>() where T:UIComponent {var list=new List<T>();foreach(var c in Children){if(c is T)list.Add((T)c);list.AddRange(c.GetComponentsInChildren<T>());}return list.ToArray();}
 }
 public class UIView:UIComponent {public static UIView Current=new UIView();public float fixedWidth=1280,fixedHeight=720;public static UIView GetAView(){return Current;}}
 public class UIPanel:UIComponent {public string backgroundSprite;}
 public class UIScrollablePanel:UIPanel {public UIOrientation scrollWheelDirection;}
 public class UILabel:UIComponent {public string text;public bool autoSize,autoHeight,wordWrap;public float textScale;}
 public class UIButton:UIComponent {public string text,normalBgSprite,hoveredBgSprite,focusedBgSprite;public event Action<UIComponent,object> eventClicked;public void Click(){if(eventClicked!=null)eventClicked(this,null);}}
 public class UITextField:UIComponent {}
 public class UIDragHandle:UIComponent {public UIComponent target;}
 public class UIHelper:ICities.UIHelperBase {public object self=new UIPanel();}
}
namespace ICities {
 public class UIHelperBase {public UIHelperBase AddGroup(string name){return new ColossalFramework.UI.UIHelper();}public void AddCheckbox(string text,bool value,Action<bool> changed){}public void AddButton(string text,Action clicked){}}
 public enum LoadMode {NewGame,LoadGame}
 public class LoadingExtensionBase {public virtual void OnLevelLoaded(LoadMode m){}public virtual void OnLevelUnloading(){}}
 public interface IUserMod {string Name{get;}string Description{get;}}
}
