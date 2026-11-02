using System;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;

namespace SMT.PriceGun200;

internal sealed class CheckoutHoldController
{
    private readonly CheckoutBridge _checkout;
    private readonly ManualLogSource _log;
    private readonly CheckoutScanGate _scanGate = new();
    private Component? _register;
    private MonoBehaviour? _player;
    private Component? _pendingItem;
    private Type? _productType;
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
        _productType ??= GameBridge.FindType("ProductCheckoutSpawn");
        if (_productType == null)
            return true;
        var products = UnityEngine.Object.FindObjectsByType(_productType, FindObjectsInactive.Exclude, FindObjectsSortMode.None).OfType<Component>()
            .Where(item => item != null && ReadBool(item, "isClient") && ReadBool(item, "isFinished") &&
                GameBridge.TryGetMemberValue(item, "netId", out var netId) && netId is uint id && id != 0 &&
                GameBridge.TryGetMemberValue(item, "NetworkcheckoutOBJ", out var owner) && owner is GameObject register && register == _register.gameObject)
            .OrderBy(item => (item.transform.position - _register.transform.position).sqrMagnitude)
            .ToArray();
        var product = products.FirstOrDefault();
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
        // Preserve _pendingItem and the scan gate until the server destroys that item.
    }

    private static MonoBehaviour? FindLocalPlayer() => GameContext.LocalPlayer;

    private static bool ReadBool(object owner, string name)
    {
        return GameBridge.TryGetMemberValue(owner, name, out var value) && value is bool flag && flag;
    }
}
