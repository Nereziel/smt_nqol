$ErrorActionPreference = 'Stop'
$contextSource = Get-Content (Join-Path $PSScriptRoot '../Core/GameContext.cs') -Raw
$contextSource = $contextSource.Replace('using System;', '').Replace('using System.Reflection;', '').Replace('using UnityEngine;', '')
$contextSource = $contextSource.Replace('namespace SMT.PriceGun200;', 'namespace SMT.PriceGun200 {') + "`n}"
$harness = @"
#nullable enable
using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
namespace UnityEngine {
    public class Object {
        public bool Destroyed;
        public static bool operator ==(Object? a,Object? b) { bool an=ReferenceEquals(a,null)||a!.Destroyed; bool bn=ReferenceEquals(b,null)||b!.Destroyed; return an||bn ? an==bn : ReferenceEquals(a,b); }
        public static bool operator !=(Object? a,Object? b)=>!(a==b);
        public override bool Equals(object? o)=>ReferenceEquals(this,o);
        public override int GetHashCode()=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public class GameObject { public bool activeInHierarchy=true; public Dictionary<Type,Component> Components=new(); }
    public class Component : Object { public GameObject gameObject=new(); public static int ComponentLookups; public Component? GetComponent(Type t){ ComponentLookups++; return gameObject.Components.TryGetValue(t,out var c)?c:null; } }
    public class MonoBehaviour : Component { public bool isActiveAndEnabled=true; }
}
public class FirstPersonController : Component { public static FirstPersonController? Instance; }
public class PlayerNetwork : MonoBehaviour { public bool isLocalPlayer=true; }
public class PlayerPermissions : Component { public bool Allowed=true; public bool RequestCP()=>Allowed; }
public class FsmBool { public bool Value {get;set;} }
public class FsmVariables { public static FsmVariables GlobalVariables=new(); public FsmBool? Options=new(); public static int VariableLookups; public FsmBool? FindFsmBool(string n){VariableLookups++; return Options;} }
namespace SMT.PriceGun200 {
    internal static class GameBridge {
        private static readonly Dictionary<string,Type> Types=new(StringComparer.Ordinal) {
            ["FirstPersonController"]=typeof(FirstPersonController), ["PlayerNetwork"]=typeof(PlayerNetwork),
            ["PlayerPermissions"]=typeof(PlayerPermissions), ["FsmVariables"]=typeof(FsmVariables)
        };
        internal static int TypeLookups;
        internal static Type? FindType(string name) { TypeLookups++; return Types.TryGetValue(name,out var t)?t:null; }
        internal static bool TryGetStaticMemberValue(Type t,string n,out object? v) { var p=t.GetProperty(n,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic); if(p!=null){v=p.GetValue(null);return true;} var f=t.GetField(n,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic); v=f?.GetValue(null); return f!=null; }
        internal static bool TryGetMemberValue(object o,string n,out object? v) { var t=o.GetType(); var p=t.GetProperty(n,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic); if(p!=null){v=p.GetValue(o);return true;} var f=t.GetField(n,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic); v=f?.GetValue(o); return f!=null; }
    }
}
$contextSource
namespace SMT.PriceGun200 {
    public static class ContextChecks {
        private static void Check(bool c,string m){if(!c)throw new Exception(m);}
        private static PlayerNetwork Connect(){var controller=new FirstPersonController(); var player=new PlayerNetwork{gameObject=controller.gameObject}; var permissions=new PlayerPermissions{gameObject=controller.gameObject}; controller.gameObject.Components[typeof(PlayerNetwork)]=player; controller.gameObject.Components[typeof(PlayerPermissions)]=permissions; FirstPersonController.Instance=controller; return player;}
        public static void Run(){
            Check(GameContext.LocalPlayer==null && !GameContext.HasCashierPermission(),"Missing player accepted");
            var first=Connect(); Check(ReferenceEquals(GameContext.LocalPlayer,first)&&GameContext.HasCashierPermission(),"Initial context failed"); Check(!GameContext.IsInOptions(),"Options default wrong");
            var components=Component.ComponentLookups; var variables=FsmVariables.VariableLookups; var types=GameBridge.TypeLookups;
            for(int i=0;i<1000;i++) Check(ReferenceEquals(GameContext.LocalPlayer,first)&&GameContext.HasCashierPermission()&&!GameContext.IsInOptions(),"Warm context changed");
            Check(Component.ComponentLookups==components && FsmVariables.VariableLookups==variables && GameBridge.TypeLookups==types,"Warm context repeated type/component/variable discovery");
            var permissions=(PlayerPermissions)first.gameObject.Components[typeof(PlayerPermissions)]; permissions.Allowed=false; Check(!GameContext.HasCashierPermission(),"Permission cached"); permissions.Allowed=true;
            FsmVariables.GlobalVariables.Options!.Value=true; Check(GameContext.IsInOptions(),"Live options missed");
            first.Destroyed=true; FirstPersonController.Instance!.Destroyed=true; Check(GameContext.LocalPlayer==null,"Destroyed player survived");
            var second=Connect(); Check(ReferenceEquals(GameContext.LocalPlayer,second)&&GameContext.HasCashierPermission(),"Reconnect failed");
            FsmVariables.GlobalVariables=new FsmVariables(); Check(!GameContext.IsInOptions(),"Global variables not rebound");
            FsmVariables.GlobalVariables=new FsmVariables{Options=null}; Check(!GameContext.IsInOptions(),"Missing options mishandled"); FsmVariables.GlobalVariables.Options=new FsmBool{Value=true}; Check(GameContext.IsInOptions(),"Late options did not recover");
            second.isLocalPlayer=false; Check(GameContext.LocalPlayer==null,"Remote player accepted");
        }
    }
}
"@
Add-Type -TypeDefinition $harness
[SMT.PriceGun200.ContextChecks]::Run()
Write-Output 'PASS: cached game context avoids warm rediscovery and preserves live permissions/options plus reconnect behavior.'
