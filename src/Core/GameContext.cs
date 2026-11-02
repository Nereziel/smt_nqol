using System;
using System.Reflection;
using UnityEngine;

namespace SMT.PriceGun200;

internal static class GameContext
{
    private static Component? _controller;
    private static MonoBehaviour? _player;
    private static Component? _permissions;
    private static MethodInfo? _requestCashier;
    private static object? _globals;
    private static object? _inOptions;

    internal static MonoBehaviour? LocalPlayer
    {
        get
        {
            var type = GameBridge.FindType("FirstPersonController");
            if (type == null || !GameBridge.TryGetStaticMemberValue(type, "Instance", out var raw) ||
                raw is not Component controller || controller == null || !controller.gameObject.activeInHierarchy)
            {
                _controller = null;
                _player = null;
                _permissions = null;
                return null;
            }
            if (controller != _controller || _player == null)
            {
                _controller = controller;
                var playerType = GameBridge.FindType("PlayerNetwork");
                _player = playerType == null ? null : controller.GetComponent(playerType) as MonoBehaviour;
                _permissions = null;
            }
            return _player != null && _player.isActiveAndEnabled &&
                GameBridge.TryGetMemberValue(_player, "isLocalPlayer", out var local) && local is bool flag && flag
                ? _player : null;
        }
    }

    internal static bool HasCashierPermission()
    {
        var player = LocalPlayer;
        if (player == null)
            return false;
        if (_permissions == null)
        {
            var type = GameBridge.FindType("PlayerPermissions");
            _permissions = type == null ? null : player.GetComponent(type);
            _requestCashier = type?.GetMethod("RequestCP", Type.EmptyTypes);
        }
        return _permissions != null && _requestCashier?.Invoke(_permissions, null) is bool allowed && allowed;
    }

    internal static object? OptionsVariable
    {
        get
        {
            var type = GameBridge.FindType("FsmVariables");
            if (type == null || !GameBridge.TryGetStaticMemberValue(type, "GlobalVariables", out var globals) || globals == null)
                return null;
            if (!ReferenceEquals(globals, _globals) || _inOptions == null)
            {
                _globals = globals;
                _inOptions = globals.GetType().GetMethod("FindFsmBool", new[] { typeof(string) })?
                    .Invoke(globals, new object[] { "InOptions" });
            }
            return _inOptions;
        }
    }

    internal static bool IsInOptions()
    {
        var variable = OptionsVariable;
        return variable != null && GameBridge.TryGetMemberValue(variable, "Value", out var value) && value is bool flag && flag;
    }
}
