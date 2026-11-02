$ErrorActionPreference = 'Stop'
$geometry = Get-Content (Join-Path $PSScriptRoot '../CheckoutColliderGeometry.cs') -Raw
$repair = Get-Content (Join-Path $PSScriptRoot '../CheckoutColliderRepair.cs') -Raw
function Wrap-Source([string] $source) {
    $source = $source.Replace('using System;', '').Replace('using System.Collections.Generic;', '').Replace('using BepInEx.Logging;', '').Replace('using UnityEngine;', '')
    $source.Replace('namespace SMT.PriceGun200;', 'namespace SMT.PriceGun200 {') + "`n}"
}
$geometry = Wrap-Source $geometry
$repair = Wrap-Source $repair
$source = @"
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using BepInEx.Logging;
namespace BepInEx.Logging { public class ManualLogSource { public void LogInfo(object value) { } } }
namespace UnityEngine {
    public enum FindObjectsInactive { Include }
    public enum FindObjectsSortMode { None }
    public enum HideFlags { HideAndDontSave }
    public static class Time { public static float unscaledTime; }
    public class Object {
        public bool Destroyed;
        public static Object[] Scene = Array.Empty<Object>();
        private static int _nextId;
        private readonly int _id = ++_nextId;
        public int GetInstanceID() => _id;
        public static Object[] FindObjectsByType(Type type, FindObjectsInactive inactive, FindObjectsSortMode sort) => Scene.Where(o => !o.Destroyed && type.IsInstanceOfType(o)).ToArray();
        public static void Destroy(Object obj) { obj.Destroyed = true; }
        public static bool operator ==(Object? a, Object? b) {
            bool an = ReferenceEquals(a, null) || a!.Destroyed;
            bool bn = ReferenceEquals(b, null) || b!.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object? a, Object? b) => !(a == b);
        public override bool Equals(object? obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => _id;
    }
    public struct Vector3 {
        public float x,y,z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public static bool operator ==(Vector3 a, Vector3 b) => a.x==b.x && a.y==b.y && a.z==b.z;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a==b);
        public override bool Equals(object? obj) => obj is Vector3 v && this==v;
        public override int GetHashCode() => x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode();
    }
    public class Transform {
        public bool IsCheckout;
        public Vector3 lossyScale = new Vector3(1,1,1);
        public Transform? Find(string path) => IsCheckout && (path=="CashRegisterCanvas" || path=="CreditCardCanvas") ? this : null;
    }
    public struct Scene { public bool IsValid() => true; }
    public class GameObject : Object {
        public Scene scene;
        public Transform transform = new Transform();
        public List<Component> Components = new List<Component>();
        public T AddComponent<T>() where T: Component,new() { var c = new T { gameObject=this }; Components.Add(c); return c; }
    }
    public class Component : Object {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
        public T[] GetComponentsInChildren<T>(bool inactive) => gameObject.Components.OfType<T>().Where(c => c is Object o && !o.Destroyed).ToArray();
    }
    public class Collider : Component { public bool enabled=true; public bool isTrigger; public object? sharedMaterial; public float contactOffset=0.01f; }
    public class BoxCollider : Collider { public Vector3 center; public Vector3 size; }
    public class MeshCollider : Collider { public bool convex; public Mesh? sharedMesh; }
    public class Mesh : Object {
        public string name=""; public HideFlags hideFlags;
        public Vector3[] vertices=Array.Empty<Vector3>(); public int[] triangles=Array.Empty<int>();
        public void RecalculateBounds() { }
    }
}
public class Data_Container : Component { }
namespace SMT.PriceGun200 { internal static class GameBridge { internal static Type? FindType(string name) => typeof(Data_Container); } }
$geometry
$repair
namespace SMT.PriceGun200 {
    public static class ColliderChecks {
        private static void Check(bool result, string reason) { if (!result) throw new Exception(reason); }
        public static void Run() {
            Vector3[] vertices;
            Check(CheckoutColliderGeometry.NeedsReplacement(new Vector3(1,2,3), new Vector3(-1.25f,1.25f,1.25f)), "Mirrored checkout not recognized");
            Check(CheckoutColliderGeometry.NeedsReplacement(new Vector3(-2,4,6), new Vector3(1,1,1)), "Negative box size not recognized");
            Check(!CheckoutColliderGeometry.NeedsReplacement(new Vector3(2,4,6), new Vector3(1,1,1)), "Valid box selected");
            Check(CheckoutColliderGeometry.TryVertices(new Vector3(3,4,5), new Vector3(-2,4,-6), out vertices), "Valid negative box rejected");
            Check(vertices.Length==8 && vertices[0]==new Vector3(2,2,2) && vertices[7]==new Vector3(4,6,8), "Collider bounds or offset changed");
            Check(CheckoutColliderGeometry.Triangles.Length==36 && CheckoutColliderGeometry.Triangles.All(i => i>=0 && i<8), "Invalid convex box triangles");
            Check(!CheckoutColliderGeometry.TryVertices(new Vector3(), new Vector3(0,1,1), out vertices), "Degenerate box accepted");
            Check(!CheckoutColliderGeometry.TryVertices(new Vector3(), new Vector3(float.NaN,1,1), out vertices), "NaN geometry accepted");
            var register = new GameObject(); register.transform.IsCheckout=true;
            register.transform.lossyScale=new Vector3(-1.25f,1.25f,1.25f);
            var container=register.AddComponent<Data_Container>();
            var box=register.AddComponent<BoxCollider>(); box.center=new Vector3(3,4,5); box.size=new Vector3(-2,4,6); box.isTrigger=true; box.sharedMaterial=new object();
            var disabled=register.AddComponent<BoxCollider>(); disabled.size=new Vector3(1,1,1); disabled.enabled=false;
            var shelf = new GameObject(); var otherContainer=shelf.AddComponent<Data_Container>();
            var unrelated=shelf.AddComponent<BoxCollider>(); unrelated.size=new Vector3(-1,1,1);
            UnityEngine.Object.Scene=new UnityEngine.Object[] { container,otherContainer };
            var repair=new CheckoutColliderRepair(new ManualLogSource());
            repair.Tick(true);
            var replacement=register.Components.OfType<MeshCollider>().First();
            Check(!box.enabled && replacement.enabled && replacement.convex && replacement.isTrigger && ReferenceEquals(replacement.sharedMaterial,box.sharedMaterial), "Replacement settings not preserved");
            Check(ReferenceEquals(replacement.gameObject,box.gameObject) && register.transform.lossyScale.x==-1.25f && box.size.x==-2f, "Original transform, size or game object changed");
            Check(register.Components.OfType<MeshCollider>().Count()==2 && !register.Components.OfType<MeshCollider>().Last().enabled, "Inactive collider was activated");
            Check(unrelated.enabled && !shelf.Components.OfType<MeshCollider>().Any(), "Non-checkout collider modified");
            Time.unscaledTime=3; repair.Tick(true);
            Check(register.Components.OfType<MeshCollider>().Count()==2, "Duplicate replacement created");
            box.enabled=true; Time.unscaledTime=6; repair.Tick(true);
            Check(!box.enabled && replacement.enabled, "Re-enabled original collider not handled");
            box.center=new Vector3(10,10,10); Time.unscaledTime=9; repair.Tick(true);
            Check(replacement.sharedMesh!.vertices[0]==new Vector3(9,8,7), "Changed box geometry not updated");
            box.size=new Vector3(2,4,6); register.transform.lossyScale=new Vector3(1,1,1); Time.unscaledTime=12; repair.Tick(true);
            Check(box.enabled && replacement.Destroyed && replacement.sharedMesh!.Destroyed, "Normal scale did not restore originals and release owned geometry");
            register.transform.lossyScale=new Vector3(-1,1,1); Time.unscaledTime=15; repair.Tick(true);
            var live=register.Components.OfType<MeshCollider>().Where(c=>!c.Destroyed).ToArray();
            repair.Tick(false);
            Check(box.enabled && !disabled.enabled && live.All(c=>c.Destroyed && c.sharedMesh!.Destroyed), "Toggle off failed to restore and clean up");
            repair.Dispose();
        }
    }
}
"@
Add-Type -TypeDefinition $source
[SMT.PriceGun200.ColliderChecks]::Run()
Write-Output 'PASS: production collider geometry and repair lifecycle: mirrored/negative boxes, offset, disabled/trigger state, repeated ticks, changed geometry, normal-scale restoration, toggle off and cleanup. Unity runtime simulated.'
