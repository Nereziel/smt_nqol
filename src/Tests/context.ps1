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
    (Extract-Method '    internal static Type? FindType('),
    (Extract-Method '    internal static bool TryGetStaticMemberValue('),
    (Extract-Method '    internal static bool TryGetMemberValue(')
) -join "`n"
$contextSource = Get-Content (Join-Path $PSScriptRoot '../GameContext.cs') -Raw
$contextSource = $contextSource.Replace('using System;', '').Replace('using System.Reflection;', '').Replace('using UnityEngine;', '')
$contextSource = $contextSource.Replace('namespace SMT.PriceGun200;', 'namespace SMT.PriceGun200 {') + "`n}"
$harness = @"
#nullable enable
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
namespace UnityEngine {
    public class Object {
        public bool Destroyed;
        public static bool operator ==(Object? a, Object? b) {
            bool aNull = ReferenceEquals(a, null) || a!.Destroyed;
            bool bNull = ReferenceEquals(b, null) || b!.Destroyed;
            return aNull || bNull ? aNull == bNull : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object? a, Object? b) { return !(a == b); }
        public override bool Equals(object? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public class GameObject {
        public bool activeInHierarchy = true;
        public Dictionary<Type, Component> Components = new Dictionary<Type, Component>();
    }
    public class Component : Object {
        public GameObject gameObject = new GameObject();
        public static int ComponentLookups;
        public Component? GetComponent(Type type) { ComponentLookups++; return gameObject.Components.TryGetValue(type, out var c) ? c : null; }
    }
    public class MonoBehaviour : Component { public bool isActiveAndEnabled = true; }
}
public class FirstPersonController : Component { public static FirstPersonController? Instance; }
public class PlayerNetwork : MonoBehaviour { public bool isLocalPlayer = true; }
public class PlayerPermissions : Component { public bool Allowed = true; public bool RequestCP() => Allowed; }
public class FsmBool { public bool Value { get; set; } }
public class FsmVariables {
    public static FsmVariables GlobalVariables = new FsmVariables();
    public FsmBool? Options = new FsmBool();
    public static int VariableLookups;
    public FsmBool? FindFsmBool(string name) { VariableLookups++; return Options; }
}
namespace SMT.PriceGun200 {
    // Count real production FindType assembly enumeration without changing its implementation.
    internal static class AppDomain {
        public static int Searches;
        public static Domain CurrentDomain { get { return new Domain(); } }
        public class Domain { public Assembly[] GetAssemblies() { Searches++; return System.AppDomain.CurrentDomain.GetAssemblies(); } }
    }
    internal static class GameBridge {
        private static readonly Dictionary<string, Type> TypeCache = new(StringComparer.Ordinal);
$methods
    }
}
$contextSource
namespace SMT.PriceGun200 {
    public static class ContextChecks {
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static PlayerNetwork Connect() {
            var controller = new FirstPersonController();
            var player = new PlayerNetwork { gameObject = controller.gameObject };
            var permissions = new PlayerPermissions { gameObject = controller.gameObject };
            controller.gameObject.Components[typeof(PlayerNetwork)] = player;
            controller.gameObject.Components[typeof(PlayerPermissions)] = permissions;
            FirstPersonController.Instance = controller;
            return player;
        }
        public static void Run() {
            Check(GameContext.LocalPlayer == null && !GameContext.HasCashierPermission(), "Missing player accepted");
            var first = Connect();
            Check(ReferenceEquals(GameContext.LocalPlayer, first) && GameContext.HasCashierPermission(), "Initial local context failed");
            Check(!GameContext.IsInOptions(), "Default options state wrong");
            var components = Component.ComponentLookups;
            var types = AppDomain.Searches;
            var variables = FsmVariables.VariableLookups;
            for (int i = 0; i < 1000; i++) {
                Check(ReferenceEquals(GameContext.LocalPlayer, first) && GameContext.HasCashierPermission() && !GameContext.IsInOptions(), "Warm context changed");
            }
            Check(Component.ComponentLookups == components && AppDomain.Searches == types && FsmVariables.VariableLookups == variables,
                "Warm context repeated type, component or variable discovery");
            var permissions = (PlayerPermissions)first.gameObject.Components[typeof(PlayerPermissions)];
            permissions.Allowed = false;
            Check(!GameContext.HasCashierPermission(), "Permission changes were cached incorrectly");
            permissions.Allowed = true;
            FsmVariables.GlobalVariables.Options!.Value = true;
            Check(GameContext.IsInOptions(), "Live options change missed");
            first.Destroyed = true;
            FirstPersonController.Instance!.Destroyed = true;
            Check(GameContext.LocalPlayer == null && !GameContext.HasCashierPermission(), "Destroyed player survived disconnect");
            var second = Connect();
            Check(ReferenceEquals(GameContext.LocalPlayer, second) && GameContext.HasCashierPermission(), "New local player not bound after reconnect");
            FsmVariables.GlobalVariables = new FsmVariables();
            Check(!GameContext.IsInOptions(), "Replaced global variables not rebound");
            FsmVariables.GlobalVariables = new FsmVariables { Options = null };
            Check(!GameContext.IsInOptions(), "Missing options variable not handled");
            FsmVariables.GlobalVariables.Options = new FsmBool { Value = true };
            Check(GameContext.IsInOptions(), "Late options initialization did not recover");
            second.isLocalPlayer = false;
            Check(GameContext.LocalPlayer == null, "Remote player accepted");
        }
    }
}
"@
Add-Type -TypeDefinition $harness
[SMT.PriceGun200.ContextChecks]::Run()
Write-Output 'PASS: 1000 warm context cycles without rediscovery; live permissions/options, disconnect, reconnect, missing and late options state checked. No scene-wide player search API is present in the test runtime.'
