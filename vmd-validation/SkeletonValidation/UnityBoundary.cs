// Offline math/scene boundary only. Production parsers, topology builder and
// retargeter are linked unchanged; this does not emulate Unity rendering.
using N = System.Numerics;
namespace UnityEngine;
public class Object
{
    public static readonly int EngineThread = Environment.CurrentManagedThreadId;
    public Object() { if(Environment.CurrentManagedThreadId != EngineThread) throw new Exception("Unity object constructed by worker"); }
    static int nextId; readonly int id = System.Threading.Interlocked.Increment(ref nextId);
    public int GetInstanceID() => id;
    public static void DontDestroyOnLoad(Object value) {}
    public bool Destroyed;
    public static void Destroy(Object o) { if (ReferenceEquals(o,null)) return; o.Destroyed=true; if(o is GameObject g) { g.transform.Destroyed=true; foreach(var c in g.transform.Children.ToArray()) Destroy(c.gameObject); } }
    public static bool operator ==(Object a,Object b) => (ReferenceEquals(a,null)||a.Destroyed) ? ReferenceEquals(b,null)||b.Destroyed : ReferenceEquals(a,b);
    public static bool operator !=(Object a,Object b) => !(a==b);
    public override bool Equals(object o) => ReferenceEquals(this,o); public override int GetHashCode()=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}
public enum HideFlags { HideAndDontSave }
public class MonoBehaviour : Object {public MonoBehaviour(IntPtr pointer) {}}
public sealed class GameObject : Object
{
    public static int CreatedCount;
    public string name; public HideFlags hideFlags; public readonly Transform transform; readonly List<object> components=[];
    public GameObject(string name) { CreatedCount++; this.name=name; transform=new Transform(this); }
    public void SetActive(bool active) {}
    public T AddComponent<T>() where T:new() { var c=new T(); if(c is SkinnedMeshRenderer r) r.transform=transform; components.Add(c); return c; }
    public T GetComponent<T>() where T:class => components.OfType<T>().FirstOrDefault();
    public T[] GetComponentsInChildren<T>(bool includeInactive)
    {
        var result=new List<T>(); if(transform is T t) result.Add(t); result.AddRange(components.OfType<T>());
        foreach(var child in transform.Children) result.AddRange(child.gameObject.GetComponentsInChildren<T>(includeInactive)); return result.ToArray();
    }
}
public sealed class Transform : Object
{
    public readonly GameObject gameObject; public string name => gameObject.name; public Transform parent; internal readonly List<Transform> Children=[];
    public Vector3 localPosition, localScale=Vector3.one; public Quaternion localRotation=Quaternion.identity;
    public Transform(GameObject owner) { gameObject=owner; }
    public int childCount=>Children.Count; public Transform GetChild(int i)=>Children[i];
    public Matrix4x4 localToWorldMatrix => parent==null?Matrix4x4.TRS(localPosition,localRotation,localScale):parent.localToWorldMatrix*Matrix4x4.TRS(localPosition,localRotation,localScale);
    public Matrix4x4 worldToLocalMatrix => localToWorldMatrix.inverse;
    public Vector3 position { get=>localToWorldMatrix.GetColumn(3); set=>localPosition=parent==null?value:parent.worldToLocalMatrix.Point(value); }
    public Quaternion rotation { get=>parent==null?localRotation:parent.rotation*localRotation; set=>localRotation=parent==null?value:Quaternion.Inverse(parent.rotation)*value; }
    public void SetParent(Transform p,bool worldPositionStays)
    {
        var pos=position; var rot=rotation; parent?.Children.Remove(this); parent=p; p?.Children.Add(this); if(worldPositionStays) { position=pos; rotation=rot; }
    }
    public Transform Find(string path) { var current=this; foreach(var part in path.Split('/')) { current=current.Children.FirstOrDefault(c=>c.name==part); if(current==null) return null; } return current; }
    public bool IsChildOf(Transform root) { for(var t=this;t!=null;t=t.parent) if(t==root) return true; return false; }
    public Vector3 InverseTransformVector(Vector3 value) => worldToLocalMatrix.Vector(value);
    public T GetComponent<T>() where T:class => gameObject.GetComponent<T>();
    public T[] GetComponentsInChildren<T>(bool inactive)=>gameObject.GetComponentsInChildren<T>(inactive);
}
public enum AnimatorCullingMode { AlwaysAnimate, CullUpdateTransforms, CullCompletely }
public sealed class Animator : Object
{
    public AnimatorCullingMode cullingMode=AnimatorCullingMode.CullUpdateTransforms;
    public static int StringToHash(string name)
    {
        uint crc=0xffffffff; foreach(byte b in System.Text.Encoding.UTF8.GetBytes(name)) { crc^=b; for(int i=0;i<8;i++) crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0); } return unchecked((int)~crc);
    }
}
public sealed class Mesh : Object
{
    public Matrix4x4[] bindposes=[];
    public string[] ShapeNames={"eye_close","vrc.v_aa","vrc.v_ih","vrc.v_ou","vrc.v_e","vrc.v_oh","mouth_a","custom_eye"};
    public int blendShapeCount=>ShapeNames.Length;
    public string GetBlendShapeName(int i)=>ShapeNames[i];
    public int GetBlendShapeIndex(string name)=>Array.IndexOf(ShapeNames,name);
}
public sealed class SkinnedMeshRenderer : Object
{
    public Transform transform; public string name=>transform.name; public Mesh sharedMesh; public Transform[] bones=[]; readonly Dictionary<int,float> weights=[];
    public Matrix4x4 localToWorldMatrix=>transform.localToWorldMatrix;
    public float GetBlendShapeWeight(int i)=>weights.GetValueOrDefault(i); public void SetBlendShapeWeight(int i,float value)=>weights[i]=value;
}
public readonly struct Keyframe(float time,float value) { public readonly float time=time,value=value; }
public struct Vector3(float x,float y,float z)
{
    public float x=x,y=y,z=z;
    internal N.Vector3 N=>new(x,y,z); internal static Vector3 From(N.Vector3 v)=>new(v.X,v.Y,v.Z);
    public float sqrMagnitude=>x*x+y*y+z*z; public float magnitude=>MathF.Sqrt(sqrMagnitude); public Vector3 normalized=>magnitude>1e-10f?this/magnitude:zero;
    public static Vector3 zero=>new(0,0,0); public static Vector3 one=>new(1,1,1); public static Vector3 up=>new(0,1,0);
    public static Vector3 operator +(Vector3 a,Vector3 b)=>From(a.N+b.N); public static Vector3 operator -(Vector3 a,Vector3 b)=>From(a.N-b.N);
    public static Vector3 operator *(Vector3 a,float b)=>From(a.N*b); public static Vector3 operator /(Vector3 a,float b)=>From(a.N/b);
    public static float Dot(Vector3 a,Vector3 b)=>System.Numerics.Vector3.Dot(a.N,b.N); public static Vector3 Cross(Vector3 a,Vector3 b)=>From(System.Numerics.Vector3.Cross(a.N,b.N));
    public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;
    public static float Angle(Vector3 a,Vector3 b)=>MathF.Acos(Math.Clamp(Dot(a.normalized,b.normalized),-1,1))*Mathf.Rad2Deg;
    public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>From(System.Numerics.Vector3.Lerp(a.N,b.N,t));
}
public struct Quaternion(float x,float y,float z,float w)
{
    public float x=x,y=y,z=z,w=w;
    internal N.Quaternion N=>new(x,y,z,w); internal static Quaternion From(N.Quaternion q)=>new(q.X,q.Y,q.Z,q.W);
    public static Quaternion identity=>new(0,0,0,1); public Quaternion normalized=>From(System.Numerics.Quaternion.Normalize(N));
    public static Quaternion operator *(Quaternion a,Quaternion b)=>From(a.N*b.N);
    public static Vector3 operator *(Quaternion a,Vector3 b)=>Vector3.From(System.Numerics.Vector3.Transform(b.N,a.N));
    public static Quaternion Inverse(Quaternion q)=>From(System.Numerics.Quaternion.Inverse(q.N));
    public static Quaternion AngleAxis(float angle,Vector3 axis)=>From(System.Numerics.Quaternion.CreateFromAxisAngle(axis.normalized.N,angle/Mathf.Rad2Deg));
    public static Quaternion FromToRotation(Vector3 a,Vector3 b)
    {
        var x=a.normalized; var y=b.normalized; var dot=Vector3.Dot(x,y);
        if(dot>0.999999f) return identity;
        if(dot<-.999999f) { var axis=Vector3.Cross(x,new(1,0,0)); if(axis.sqrMagnitude<1e-8f) axis=Vector3.Cross(x,Vector3.up); return AngleAxis(180,axis); }
        var v=Vector3.Cross(x,y); return new Quaternion(v.x,v.y,v.z,1+dot).normalized;
    }
    public static Quaternion Slerp(Quaternion a,Quaternion b,float t)=>From(System.Numerics.Quaternion.Slerp(a.N,b.N,t));
    public static float Angle(Quaternion a,Quaternion b)=>2*MathF.Acos(Math.Clamp(MathF.Abs(System.Numerics.Quaternion.Dot(a.normalized.N,b.normalized.N)),0,1))*Mathf.Rad2Deg;
}
public readonly struct Matrix4x4(N.Matrix4x4 value)
{
    readonly N.Matrix4x4 value=value;
    public static Matrix4x4 identity=>new(N.Matrix4x4.Identity);
    public static Matrix4x4 TRS(Vector3 p,Quaternion q,Vector3 s)=>new(N.Matrix4x4.CreateScale(s.N)*N.Matrix4x4.CreateFromQuaternion(q.N)*N.Matrix4x4.CreateTranslation(p.N));
    public static Matrix4x4 operator *(Matrix4x4 a,Matrix4x4 b)=>new(b.value*a.value);
    public Matrix4x4 inverse { get { if(!N.Matrix4x4.Invert(value,out var v)) throw new Exception("Singular fixture matrix"); return new(v); } }
    public Quaternion rotation { get { N.Matrix4x4.Decompose(value,out _,out var q,out _); return Quaternion.From(q); } }
    public Vector3 GetColumn(int index)=>index switch {0=>new(value.M11,value.M12,value.M13),1=>new(value.M21,value.M22,value.M23),2=>new(value.M31,value.M32,value.M33),3=>new(value.M41,value.M42,value.M43),_=>throw new NotSupportedException()};
    public Vector3 Point(Vector3 p)=>Vector3.From(N.Vector3.Transform(p.N,value)); public Vector3 Vector(Vector3 p)=>Vector3.From(N.Vector3.TransformNormal(p.N,value));
}
public static class Mathf
{
    public const float Rad2Deg=180f/MathF.PI;
    public static float Clamp(float v,float a,float b)=>Math.Clamp(v,a,b); public static float Clamp01(float v)=>Clamp(v,0,1);
    public static float Lerp(float a,float b,float t)=>a+(b-a)*t; public static float Sqrt(float v)=>MathF.Sqrt(v);
}
