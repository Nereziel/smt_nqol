using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace SMT.PriceGun200;

internal sealed class CheckoutHoldController
{
    private readonly CheckoutBridge _checkout;
    private readonly ManualLogSource _log;
    private readonly CheckoutScanGate _scanGate = new();
    private readonly List<Component> _productCache = new();
    private readonly CheckoutDiscoveryThrottle _discoveryThrottle = new();
    private Component? _register;
    private MonoBehaviour? _player;
    private Component? _pendingItem;
    private Type? _productType;
    private GameObject? _productCacheRegister;
    private bool _held;
    private bool _automating;
    private bool _blocked;
    private float _startedAt;
    private float _nextAction;
    private float _sentAt;
    private bool _warnedWait;

    internal CheckoutHoldController(CheckoutBridge checkout, ManualLogSource log)
    {
        _checkout = checkout;
        _log = log;
    }

    internal bool Tick(bool held, float interval)
    {
        // A released/repressed key must not submit an item twice while its command is in flight.
        _scanGate.Observe(_scanGate.PendingItemId, _pendingItem != null);
        if (!held || !Application.isFocused)
        {
            Stop();
            return false;
        }
        if (!_held)
        {
            _held = true;
            _startedAt = Time.unscaledTime;
            _register = _checkout.FindHoldCheckout();
            _player = FindLocalPlayer();
            _blocked = _register == null || _player == null;
            ResetProductCache();
        }
        if (_blocked || _register == null || _player == null)
            return false;
        if (Time.unscaledTime - _startedAt < 0.3f)
            return true;
        _automating = true;
        if (Time.unscaledTime < _nextAction)
            return true;
        _nextAction = Time.unscaledTime + interval;
        if (_player != FindLocalPlayer() || !_player.isActiveAndEnabled ||
            (_player.transform.position - _register.transform.position).sqrMagnitude > 16f ||
            !CheckoutBridge.CanOperate() || ReadBool(_register, "isOccupiedByNPCCashier"))
        {
            _checkout.StopHeldPayment();
            _blocked = true;
            return true;
        }
        if (!_scanGate.CanSubmit)
        {
            if (!_warnedWait && Time.unscaledTime - _sentAt > 5f)
            {
                _warnedWait = true;
                _log.LogWarning("Held checkout is waiting for the server to remove the scanned item; no duplicate scan will be sent.");
            }
            return true;
        }
        if (_checkout.HasPendingPayment(_register))
            return true;

        var registerObject = _register.gameObject;
        if (_productCacheRegister != registerObject)
            ResetProductCache();

        var product = FindNearestCachedProduct(registerObject, _register.transform.position);
        if (product == null && _discoveryThrottle.ShouldDiscover(Time.unscaledTime))
        {
            RefreshProductCache(registerObject);
            product = FindNearestCachedProduct(registerObject, _register.transform.position);
        }

        if (product != null)
        {
            var interaction = CheckoutBridge.FindBehaviour(product.transform);
            if (interaction is not MonoBehaviour behaviour || !behaviour.isActiveAndEnabled)
                return true;
            var send = interaction.GetType().GetMethod("SendEvent", new[] { typeof(string) });
            if (send == null || !_scanGate.TrySubmit(product.GetInstanceID()))
                return true;
            _pendingItem = product;
            _sentAt = Time.unscaledTime;
            _warnedWait = false;
            // Same permission/readiness/single-use FSM as clicking an individual belt item.
            // Keep the guard even if invocation throws: a partially sent network command is uncertain.
            send.Invoke(interaction, new object[] { "Send_Data" });
            return true;
        }
        // Zero visible products alone is not completion: the customer may still be unloading.
        if (GameBridge.TryGetMemberValue(_register, "productsLeft", out var remaining) && remaining is int count && count == 0)
            _checkout.TryConfirmHeldPayment(_register);
        return true;
    }

    private Component? FindNearestCachedProduct(GameObject registerObject, Vector3 registerPosition)
    {
        Component? nearest = null;
        var nearestDistance = float.PositiveInfinity;
        foreach (var item in _productCache)
        {
            if (item == null || !ReadBool(item, "isClient") || !ReadBool(item, "isFinished") ||
                !GameBridge.TryGetMemberValue(item, "netId", out var netId) || netId is not uint id || id == 0 ||
                !GameBridge.TryGetMemberValue(item, "NetworkcheckoutOBJ", out var owner) || owner is not GameObject register || register != registerObject)
                continue;
            var distance = (item.transform.position - registerPosition).sqrMagnitude;
            if (distance >= nearestDistance)
                continue;
            nearestDistance = distance;
            nearest = item;
        }
        return nearest;
    }

    private void RefreshProductCache(GameObject registerObject)
    {
        _productCache.Clear();
        _productCacheRegister = registerObject;
        _productType ??= GameBridge.FindType("ProductCheckoutSpawn");
        if (_productType == null)
            return;

        foreach (var found in UnityEngine.Object.FindObjectsByType(_productType, FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (found is not Component item || item == null || !ReadBool(item, "isClient") ||
                !GameBridge.TryGetMemberValue(item, "netId", out var netId) || netId is not uint id || id == 0 ||
                !GameBridge.TryGetMemberValue(item, "NetworkcheckoutOBJ", out var owner) || owner is not GameObject register || register != registerObject)
                continue;
            _productCache.Add(item);
        }
    }

    private void ResetProductCache()
    {
        _productCache.Clear();
        _productCacheRegister = null;
        _discoveryThrottle.Reset();
    }

    internal void Stop()
    {
        if (_automating)
            _checkout.StopHeldPayment();
        _held = false;
        _automating = false;
        _blocked = false;
        _register = null;
        _player = null;
        _nextAction = 0f;
        ResetProductCache();
        // Preserve _pendingItem and the scan gate until the server destroys that item.
    }

    private static MonoBehaviour? FindLocalPlayer() => GameContext.LocalPlayer;

    private static bool ReadBool(object owner, string name)
    {
        return GameBridge.TryGetMemberValue(owner, name, out var value) && value is bool flag && flag;
    }
}
