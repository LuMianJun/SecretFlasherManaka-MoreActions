namespace UnityEngine;
public enum Space { World, Self }
public static class Time { public static float deltaTime; }
public static class Debug { public static void Log(object value) {} }
public static class Gizmos { public static void DrawWireSphere(Vector3 p,float radius) {} }
public partial class Transform
{
    public Vector3 right=>rotation*Vector3.right;
    public Vector3 up=>rotation*Vector3.up;
    public Vector3 forward=>rotation*Vector3.forward;
    public Vector3 lossyScale=>new(localToWorldMatrix.GetColumn(0).magnitude,localToWorldMatrix.GetColumn(1).magnitude,localToWorldMatrix.GetColumn(2).magnitude);
    public void SetParent(Transform p)=>SetParent(p,true);
    public void Rotate(Vector3 axis,float degrees,Space space=Space.Self) { if(space==Space.World)rotation=Quaternion.AngleAxis(degrees,axis)*rotation;else localRotation=localRotation*Quaternion.AngleAxis(degrees,axis); }
    public void RotateAround(Vector3 point,Vector3 axis,float degrees) { var q=Quaternion.AngleAxis(degrees,axis);position=point+q*(position-point);rotation=q*rotation; }
    public System.Collections.IEnumerator GetEnumerator()=>Children.GetEnumerator();
}
public struct SkeletonBone { public string name;public Vector3 position;public Quaternion rotation;public Vector3 scale; }
public struct HumanDescription { public SkeletonBone[] skeleton; }
public class Avatar { public HumanDescription humanDescription; }
public partial class Animator
{
    public GameObject gameObject=new("Animator");
    public Transform transform=>gameObject.transform;
    public bool isHuman=true; public bool enabled=true;
    public Avatar avatar=new();
    public readonly Dictionary<HumanBodyBones,Transform> Bones=new();
    public Transform GetBoneTransform(HumanBodyBones name)=>Bones.GetValueOrDefault(name);
}
public partial struct Vector3
{
    public static Vector3 right=>new(1,0,0);
    public static Vector3 forward=>new(0,0,1);
    public static Vector3 operator -(Vector3 a)=>new(-a.x,-a.y,-a.z);
    public static Vector3 operator *(float a,Vector3 b)=>b*a;
    public static bool operator ==(Vector3 a,Vector3 b)=>(a-b).sqrMagnitude<1e-10f;
    public static bool operator !=(Vector3 a,Vector3 b)=>!(a==b);
    public override bool Equals(object o)=>o is Vector3 v&&this==v;
    public override int GetHashCode()=>HashCode.Combine(x,y,z);
}
public partial struct Quaternion
{
    public float sqrMagnitude()=>x*x+y*y+z*z+w*w;
    public static Quaternion Euler(Vector3 v)=>Euler(v.x,v.y,v.z);
    public static Quaternion Euler(float x,float y,float z)=>AngleAxis(y,Vector3.up)*AngleAxis(x,Vector3.right)*AngleAxis(z,Vector3.forward);
    public Vector3 eulerAngles
    {
        get
        {
            var q=normalized;var right=q*Vector3.right;var up=q*Vector3.up;var forward=q*Vector3.forward;
            float xx=MathF.Asin(Math.Clamp(-forward.y,-1,1));float yy,zz;
            if(MathF.Abs(MathF.Cos(xx))>1e-6f){yy=MathF.Atan2(forward.x,forward.z);zz=MathF.Atan2(right.y,up.y);}
            else {yy=MathF.Atan2(-right.z,right.x);zz=0;}
            float Wrap(float a){a*=Mathf.Rad2Deg;return a<0?a+360:a;}
            return new(Wrap(xx),Wrap(yy),Wrap(zz));
        }
    }
    public static Quaternion Lerp(Quaternion a,Quaternion b,float t)=>From(System.Numerics.Quaternion.Lerp(a.N,b.N,Math.Clamp(t,0,1)));
    public static bool operator ==(Quaternion a,Quaternion b)=>System.Numerics.Quaternion.Dot(a.N,b.N)>.999999f;
    public static bool operator !=(Quaternion a,Quaternion b)=>!(a==b);
    public override bool Equals(object o)=>o is Quaternion q&&this==q;
    public override int GetHashCode()=>HashCode.Combine(x,y,z,w);
}
public static partial class Mathf
{
    public const float PI=MathF.PI;
    public static float Abs(float a)=>MathF.Abs(a);
    public static float Pow(float a,float b)=>MathF.Pow(a,b);
    public static float Acos(float a)=>MathF.Acos(a);
    public static float Min(float a,float b)=>MathF.Min(a,b);
}
