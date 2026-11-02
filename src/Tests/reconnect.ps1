$ErrorActionPreference = 'Stop'
$bridgeSource = Get-Content (Join-Path $PSScriptRoot '../Core/GameBridge.cs') -Raw
function Extract-Method([string] $signature) {
    $start = $bridgeSource.IndexOf($signature)
    if ($start -lt 0) { throw "Missing method: $signature" }
    $open = $bridgeSource.IndexOf('{', $start)
    $depth = 1; $end = $open + 1
    while ($depth -gt 0 -and $end -lt $bridgeSource.Length) {
        if ($bridgeSource[$end] -eq '{') { $depth++ }
        if ($bridgeSource[$end] -eq '}') { $depth-- }
        $end++
    }
    return $bridgeSource.Substring($start, $end - $start)
}
$methods = @(
    (Extract-Method '    private bool EnsureProductListing('),
    (Extract-Method '    private static bool IsLiveProductListing(')
) -join "`n"
$harness = @"
#nullable enable
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
namespace UnityEngine {
    public enum FindObjectsInactive { Exclude, Include }
    public enum FindObjectsSortMode { None }
    public class Object {
        public bool Destroyed;
        public static Object[] Scene = Array.Empty<Object>();
        public static Object[] FindObjectsByType(Type type, FindObjectsInactive inactive, FindObjectsSortMode sort) => Scene.Where(o => !o.Destroyed && type.IsInstanceOfType(o)).ToArray();
        public static bool operator ==(Object? a, Object? b) {
            bool an = ReferenceEquals(a, null) || a!.Destroyed; bool bn = ReferenceEquals(b, null) || b!.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a,b);
        }
        public static bool operator !=(Object? a, Object? b) => !(a==b);
        public override bool Equals(object? o) => ReferenceEquals(this,o);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public class GameObject { public bool activeInHierarchy=true; }
    public class MonoBehaviour : Object { public bool isActiveAndEnabled=true; public GameObject gameObject=new GameObject(); }
}
public class ProductListing : MonoBehaviour { public static ProductListing? Instance; public bool isClient=true; public uint netId=1; }
public class ReconnectHarness {
    private Type? _productListingType;
    private object? _productListingInstance;
    private MethodInfo? _marketPriceMethod;
    private MethodInfo? _setPriceMethod;
    private static Type? FindType(string name) => typeof(ProductListing);
    private static bool TryGetStaticMemberValue(Type type,string name,out object? value) { value=type.GetField(name)?.GetValue(null); return value!=null; }
    private static bool TryGetMemberValue(object owner,string name,out object? value) { value=owner.GetType().GetField(name)?.GetValue(owner); return value!=null; }
    private void Debug(string message) { }
    public object? Current => _productListingInstance;
    public bool Resolve() => EnsureProductListing();
    public void SeedMethods() { _marketPriceMethod=_setPriceMethod=typeof(string).GetMethod("ToString",Type.EmptyTypes); }
    public bool MethodsCleared => _marketPriceMethod==null && _setPriceMethod==null;
$methods
    private static void Check(bool v,string m) { if(!v) throw new Exception(m); }
    public static void Run() {
        var bridge=new ReconnectHarness();
        var first=new ProductListing(); ProductListing.Instance=first; Object.Scene=new Object[]{first};
        Check(bridge.Resolve() && ReferenceEquals(bridge.Current,first),"Initial connection failed");
        bridge.SeedMethods(); first.Destroyed=true; Object.Scene=Array.Empty<Object>();
        Check(!bridge.Resolve() && bridge.Current==null && bridge.MethodsCleared,"Destroyed wrapper survived disconnect");
        var second=new ProductListing(); ProductListing.Instance=second; Object.Scene=new Object[]{second};
        Check(bridge.Resolve() && ReferenceEquals(bridge.Current,second),"Reconnect failed");
        bridge.SeedMethods(); var third=new ProductListing(); ProductListing.Instance=third; Object.Scene=new Object[]{third};
        Check(bridge.Resolve() && ReferenceEquals(bridge.Current,third) && bridge.MethodsCleared,"Session replacement kept methods");
        ProductListing.Instance=first; Check(bridge.Resolve() && ReferenceEquals(bridge.Current,third),"Destroyed singleton blocked fallback");
        third.isClient=false; Check(!bridge.Resolve(),"Disconnected listing accepted");
        third.isClient=true; third.netId=0; Check(!bridge.Resolve(),"Unspawned listing accepted");
        third.netId=3; third.isActiveAndEnabled=false; Check(!bridge.Resolve(),"Disabled listing accepted");
        third.isActiveAndEnabled=true; third.gameObject.activeInHierarchy=false; Check(!bridge.Resolve(),"Inactive listing accepted");
        third.gameObject.activeInHierarchy=true; Check(bridge.Resolve() && ReferenceEquals(bridge.Current,third),"Ready listing did not recover");
    }
}
"@
Add-Type -TypeDefinition $harness
[ReconnectHarness]::Run()
Write-Output 'PASS: reconnect/session replacement still rebinds live ProductListing and clears stale method caches.'
