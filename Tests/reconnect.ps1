$ErrorActionPreference = 'Stop'
$pluginSource = Get-Content (Join-Path $PSScriptRoot '../Plugin.cs') -Raw
function Extract-Method([string] $signature) {
    $start = $pluginSource.IndexOf($signature)
    if ($start -lt 0) { throw "Missing method: $signature" }
    $open = $pluginSource.IndexOf('{', $start)
    $depth = 1
    $end = $open + 1
    while ($depth -gt 0 -and $end -lt $pluginSource.Length) {
        if ($pluginSource[$end] -eq '{') { $depth++ }
        if ($pluginSource[$end] -eq '}') { $depth-- }
        $end++
    }
    return $pluginSource.Substring($start, $end - $start)
}
$methods = @(
    (Extract-Method '    private bool EnsureProductListing('),
    (Extract-Method '    private static bool IsLiveProductListing('),
    (Extract-Method '    internal static bool TryGetStaticMemberValue('),
    (Extract-Method '    internal static bool TryGetMemberValue(')
) -join "`n"
$harness = @"
#nullable enable
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
namespace UnityEngine {
    public class Object {
        public bool Destroyed;
        public static Object[] Scene = Array.Empty<Object>();
        public static Object[] FindObjectsOfType(Type type) { return Scene.Where(o => type.IsInstanceOfType(o)).ToArray(); }
        public static bool operator ==(Object? a, Object? b) {
            bool aNull = ReferenceEquals(a, null) || a!.Destroyed;
            bool bNull = ReferenceEquals(b, null) || b!.Destroyed;
            return aNull || bNull ? aNull == bNull : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object? a, Object? b) { return !(a == b); }
        public override bool Equals(object? other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
    }
    public class GameObject { public bool activeInHierarchy = true; }
    public class MonoBehaviour : Object {
        public bool isActiveAndEnabled = true;
        public GameObject gameObject = new GameObject();
    }
}
public class ProductListing : MonoBehaviour {
    public static ProductListing? Instance;
    public bool isClient = true;
    public uint netId = 1;
}
public class ReconnectHarness {
    private Type? _productListingType;
    private object? _productListingInstance;
    private MethodInfo? _marketPriceMethod;
    private MethodInfo? _setPriceMethod;
    private static Type? FindType(string name) { return typeof(ProductListing); }
    private void Debug(string message) { }
    public object? Current { get { return _productListingInstance; } }
    public bool Resolve() { return EnsureProductListing(); }
    public void SeedMethods() { _marketPriceMethod = _setPriceMethod = typeof(string).GetMethod("ToString", Type.EmptyTypes); }
    public bool MethodsCleared { get { return _marketPriceMethod == null && _setPriceMethod == null; } }
$methods
    private static void Check(bool result, string message) { if (!result) throw new Exception(message); }
    public static void Run() {
        var bridge = new ReconnectHarness();
        var first = new ProductListing();
        ProductListing.Instance = first;
        UnityEngine.Object.Scene = new UnityEngine.Object[] { first };
        Check(bridge.Resolve() && ReferenceEquals(bridge.Current, first), "Initial connection failed");
        bridge.SeedMethods();
        first.Destroyed = true;
        UnityEngine.Object.Scene = Array.Empty<UnityEngine.Object>();
        Check(!bridge.Resolve() && bridge.Current == null && bridge.MethodsCleared, "Destroyed wrapper survived disconnect");
        var second = new ProductListing();
        ProductListing.Instance = second;
        UnityEngine.Object.Scene = new UnityEngine.Object[] { second };
        Check(bridge.Resolve() && ReferenceEquals(bridge.Current, second), "Reconnect did not bind the new listing");
        bridge.SeedMethods();
        var third = new ProductListing();
        ProductListing.Instance = third;
        UnityEngine.Object.Scene = new UnityEngine.Object[] { third };
        Check(bridge.Resolve() && ReferenceEquals(bridge.Current, third) && bridge.MethodsCleared, "Session replacement kept cached methods");
        ProductListing.Instance = first;
        Check(bridge.Resolve() && ReferenceEquals(bridge.Current, third), "Destroyed singleton prevented scene fallback");
        third.isClient = false;
        Check(!bridge.Resolve(), "Disconnected network listing accepted");
        third.isClient = true;
        third.netId = 0;
        Check(!bridge.Resolve(), "Unspawned listing accepted");
        third.netId = 3;
        third.isActiveAndEnabled = false;
        Check(!bridge.Resolve(), "Disabled listing accepted");
        third.isActiveAndEnabled = true;
        third.gameObject.activeInHierarchy = false;
        Check(!bridge.Resolve(), "Inactive scene listing accepted");
        third.gameObject.activeInHierarchy = true;
        Check(bridge.Resolve() && ReferenceEquals(bridge.Current, third), "Ready listing did not recover automatically");
    }
}
"@
Add-Type -TypeDefinition $harness
[ReconnectHarness]::Run()
Write-Output 'PASS: 10 reconnect checks against production resolver methods with simulated Unity destroyed-object semantics.'
