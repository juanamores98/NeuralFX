// Deliberately limited Unity doubles: contract tests, never game/GPU evidence.
using System;
namespace NeuralFX { internal static class NativeInterop { public static IntPtr GetModuleHandle(string s) { return IntPtr.Zero; } public static IntPtr GetProcAddress(IntPtr p,string s) {return IntPtr.Zero;} } }
namespace UnityEngine {
 public class Object {public static void Destroy(Object o) {}}
 public class MonoBehaviour:Object {public Camera TestCamera; public T GetComponent<T>() where T:class {return TestCamera as T;}}
 public class Camera:Object {
  public Matrix4x4 projectionMatrix,nonJitteredProjectionMatrix; public DepthTextureMode depthTextureMode;
  public Transform transform=new Transform();public int pixelWidth=1920,pixelHeight=1080;public float fieldOfView=60;
  public int GetInstanceID(){return 42;} public void AddCommandBuffer(Rendering.CameraEvent e,Rendering.CommandBuffer b){}public void RemoveCommandBuffer(Rendering.CameraEvent e,Rendering.CommandBuffer b){}
 }
 public class Transform {public Vector3 position;public Quaternion rotation;}
 public struct Vector3 {public float x,y,z;public static float Distance(Vector3 a,Vector3 b){return 0;}}
 public struct Quaternion {public static float Angle(Quaternion a,Quaternion b){return 0;}}
 public struct Matrix4x4 {private float[] _data; public float this[int i] {get{return _data==null?0:_data[i];} set{var copy=_data==null?new float[16]:(float[])_data.Clone();copy[i]=value;_data=copy;}}}
 [Flags]public enum DepthTextureMode {None=0,Depth=1,MotionVectors=4}
 public enum RenderTextureFormat {RGHalf}public enum RenderTextureReadWrite {Linear}public enum FilterMode {Point}
 public class RenderTexture:Object {public RenderTexture(int w,int h,int d,RenderTextureFormat f,RenderTextureReadWrite r){}public string name;public bool useMipMap;public FilterMode filterMode; public bool Create(){return true;}public IntPtr GetNativeTexturePtr(){throw new Exception("No pointer acquisition expected without consent");}}
 public static class SystemInfo {public static bool SupportsRenderTextureFormat(RenderTextureFormat f){return true;}}
 public static class Mathf {public static float Max(float a,float b){return Math.Max(a,b);}public static float Abs(float a){return Math.Abs(a);}}
 public static class Time {public static float timeScale=1;}
 public static class Debug {public static void LogWarning(object m){}}
}
namespace UnityEngine.Rendering {
 public enum CameraEvent {AfterEverything}public enum BuiltinRenderTextureType {MotionVectors}
 public struct RenderTargetIdentifier {public RenderTargetIdentifier(UnityEngine.RenderTexture t){}}
 public class CommandBuffer {public string name;public void Clear(){}public void Release(){}public void IssuePluginEvent(IntPtr p,int f){}public void Blit(BuiltinRenderTextureType t,RenderTargetIdentifier d){}}
}
