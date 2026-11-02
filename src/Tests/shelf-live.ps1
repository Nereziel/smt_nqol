$ErrorActionPreference = 'Stop'
$bridgeSource = Get-Content (Join-Path $PSScriptRoot '../Core/GameBridge.cs') -Raw
function Extract-Method([string] $signature) {
    $start = $bridgeSource.IndexOf($signature)
    if ($start -lt 0) { throw "Missing method: $signature" }
    $open = $bridgeSource.IndexOf('{', $start); $depth = 1; $end = $open + 1
    while ($depth -gt 0 -and $end -lt $bridgeSource.Length) {
        if ($bridgeSource[$end] -eq '{') { $depth++ }
        if ($bridgeSource[$end] -eq '}') { $depth-- }
        $end++
    }
    $bridgeSource.Substring($start, $end - $start)
}
$resolver = Extract-Method '    private bool TryResolveShelfProductId('
$selection = Get-Content (Join-Path $PSScriptRoot '../Features/PriceGun/ShelfProductSelection.cs') -Raw
$selection = $selection.Replace('namespace SMT.PriceGun200;', 'namespace SMT.PriceGun200 {') + "`n}"
$source = @"
#nullable enable
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
namespace UnityEngine {
    public class GameObject { public bool activeInHierarchy = true; }
    public class Component { }
    public struct Vector3 { public float x,y,z; public float sqrMagnitude=>x*x+y*y+z*z; public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3{x=a.x-b.x,y=a.y-b.y,z=a.z-b.z}; }
    public class Transform {
        public string name=""; public Transform? parent; public GameObject gameObject=new(); public Vector3 position;
        public List<Transform> Children=new(); public List<Component> Components=new();
        public T[] GetComponents<T>()=>Components.OfType<T>().ToArray();
        public Transform Add(string name){var c=new Transform{name=name,parent=this};Children.Add(c);return c;}
        public Transform? Find(string name)=>Children.FirstOrDefault(c=>c.name==name);
        public int childCount=>Children.Count; public Transform GetChild(int i)=>Children[i]; public int GetSiblingIndex()=>parent==null?0:parent.Children.IndexOf(this);
    }
}
public class Data_Container : Component { public int[] productInfoArray=Array.Empty<int>(); }
public class InteractableContainer : Component { }
$selection
namespace SMT.PriceGun200 {
    public class LiveShelfChecks {
        private void Debug(string text) { }
        private static bool TryGetMemberValue(object o,string n,out object? v){var t=o.GetType();var p=t.GetProperty(n,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);if(p!=null){v=p.GetValue(o);return true;}var f=t.GetField(n,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);v=f?.GetValue(o);return f!=null;}
$resolver
        private static void Check(bool v,string r){if(!v)throw new Exception(r);}
        public static void Run(){
            var resolver=new LiveShelfChecks(); var shelf=new Transform{name="NewShelf(Clone)"}; var data=new Data_Container{productInfoArray=new int[]{0,1,-1,0}}; shelf.Components.Add(data);
            var slots=shelf.Add("RuntimeShelfRows"); var first=slots.Add("Row0"); first.Components.Add(new InteractableContainer()); var fresh=slots.Add("Row1"); fresh.Components.Add(new InteractableContainer()); var productCollider=fresh.Add("NewProductCollider"); int id;
            Check(!resolver.TryResolveShelfProductId(productCollider,new Vector3(),out id),"Unassigned row resolved");
            data.productInfoArray[2]=240;data.productInfoArray[3]=12; Check(resolver.TryResolveShelfProductId(productCollider,new Vector3(),out id)&&id==240,"Live restock failed");
            data.productInfoArray[2]=241; Check(resolver.TryResolveShelfProductId(fresh,new Vector3(),out id)&&id==241,"Replacement stale");
            data.productInfoArray=new int[]{0,1,300,5}; Check(resolver.TryResolveShelfProductId(productCollider,new Vector3(),out id)&&id==300,"Array replacement stale");
            Check(resolver.TryResolveShelfProductId(first,new Vector3(),out id)&&id==0,"Product zero rejected");
            data.productInfoArray[2]=-1; Check(!resolver.TryResolveShelfProductId(productCollider,new Vector3(),out id),"Cleared row retained product");
            Check(!resolver.TryResolveShelfProductId(new Transform{name="Wall"},new Vector3(),out id),"Non-shelf accepted");
        }
    }
}
"@
Add-Type -TypeDefinition $source
[SMT.PriceGun200.LiveShelfChecks]::Run()
Write-Output 'PASS: production shelf resolver handles live restock/replacement/clear after GameBridge split.'
