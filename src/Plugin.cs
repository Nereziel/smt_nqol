using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace SMT.PriceGun200;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInProcess("Supermarket Together.exe")]
public sealed partial class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "cz.milan.supermarkettogether.pricegun200";
    public const string PluginName = "SMT NQOL";
    public const string PluginVersion = "0.7.4";

    private ConfigEntry<bool> _enabled = null!;
    private ConfigEntry<bool> _scannerEnabled = null!;
    private ConfigEntry<float> _multiplier = null!;
    private ConfigEntry<float> _maxDistance = null!;
    private ConfigEntry<KeyboardShortcut> _hotkey = null!;
    private ConfigEntry<bool> _requirePricingTool = null!;
    private ConfigEntry<bool> _debugLogging = null!;
    private ConfigEntry<bool> _checkoutEnabled = null!;
    private ConfigEntry<bool> _fixCheckoutColliders = null!;
    private CheckoutColliderRepair? _colliderRepair;
    private ConfigEntry<KeyboardShortcut> _checkoutHotkey = null!;
    private CheckoutBridge? _checkout;
    private CheckoutHoldController? _checkoutHold;
    private ConfigEntry<bool> _holdToScan = null!;
    private ConfigEntry<float> _scanInterval = null!;
    private ConfigEntry<float> _afterPickupDelay = null!;
    private ConfigEntry<float> _confirmDelay = null!;

    private readonly HashSet<string> _oneShotWarnings = new();
    private GameBridge? _bridge;

    private void Awake()
    {
        _enabled = Config.Bind("General", "Enabled", true,
            "Enable or disable the mod.");

        _scannerEnabled = Config.Bind("Scanner", "Enabled", true, "Enable scanner shelf pricing independently of checkout automation.");

        _multiplier = Config.Bind("Pricing", "MarketPriceMultiplier", 2.01f,
            "Selling price multiplier relative to the raw current market price. 2.01 = 201%.");

        _maxDistance = Config.Bind("Scanner", "MaxDistance", 6.0f,
            "Maximum raycast distance used to detect the product you are aiming at.");

        _hotkey = Config.Bind("Scanner", "Hotkey", new KeyboardShortcut(KeyCode.Mouse2),
            "Press while aiming at a product. Mouse2 is the middle mouse button.");

        _requirePricingTool = Config.Bind("Scanner", "RequirePricingTool", true,
            "If true, prices can only be changed while the local player has the pricing scanner equipped (PlayerNetwork.equippedItem = 2).");

        _debugLogging = Config.Bind("Debug", "VerboseLogging", false,
            "Write detailed resolver information into the BepInEx log.");

        _fixCheckoutColliders = Config.Bind("Checkout", "FixNegativeColliders", true,
            "Replace invalid checkout BoxColliders with equivalent convex mesh boxes without changing transforms. Disabling restores original colliders.");
        _colliderRepair = new CheckoutColliderRepair(Logger);
        _checkoutEnabled = Config.Bind("Checkout", "Enabled", true,
            "Enable exact cash change and card amount confirmation at the checkout currently being operated.");
        _checkoutHotkey = Config.Bind("Checkout", "ConfirmHotkey", new KeyboardShortcut(KeyCode.Mouse1),
            "Aim at offered money/card or a cash/card payment control to accept, fill and confirm payment. Mouse1 is the right mouse button.");

        _holdToScan = Config.Bind("Checkout", "HoldToScan", true,
            "Hold ConfirmHotkey for 0.3 seconds while aiming at a checkout or its belt item to scan and finish payments until released.");
        _scanInterval = Config.Bind("Checkout", "ScanInterval", 0.15f,
            "Minimum seconds between automatic checkout actions. Each scanned item must disappear before another is sent.");
        _scanInterval.Value = Mathf.Clamp(_scanInterval.Value, 0.05f, 2f);
        _afterPickupDelay = Config.Bind("Checkout", "AfterPickupDelay", 1f,
            "Seconds to wait after the accepted cash/card payment panel opens before filling the amount. Range 0-60; 0 disables the delay.");
        _confirmDelay = Config.Bind("Checkout", "ConfirmDelay", 1f,
            "Seconds to wait after filling exact cash change/card amount before confirming payment. Range 0-60; 0 disables the delay.");
        _afterPickupDelay.Value = CheckoutDelay.Normalize(_afterPickupDelay.Value);
        _confirmDelay.Value = CheckoutDelay.Normalize(_confirmDelay.Value);
        _checkout = new CheckoutBridge(Logger, () => _debugLogging.Value,
            () => _afterPickupDelay.Value, () => _confirmDelay.Value);
        _checkoutHold = new CheckoutHoldController(_checkout, Logger);
        _multiplier.Value = Mathf.Max(0f, _multiplier.Value);
        _maxDistance.Value = Mathf.Clamp(_maxDistance.Value, 1f, 20f);

        _bridge = new GameBridge(Logger, () => _debugLogging.Value);
        Logger.LogInfo($"{PluginName} {PluginVersion} loaded. Scanner hotkey: {_hotkey.Value}; checkout hotkey: {_checkoutHotkey.Value}; multiplier: {_multiplier.Value:0.###}x");
    }

    private void Update()
    {
        try { _colliderRepair?.Tick(_enabled.Value && _fixCheckoutColliders.Value); }
        catch (Exception ex) { WarnOnce("collider-repair", $"Checkout collider repair failed: {ex}"); }
        if (UpdateMenuInput())
            return;
        if (!_enabled.Value || _bridge == null)
        {
            _checkoutHold?.Stop();
            return;
        }
        try
        {
            var holdConsumed = _checkoutHold?.Tick(_checkoutEnabled.Value && _holdToScan.Value && _checkoutHotkey.Value.IsPressed(), _scanInterval.Value) ?? false;
            // Payment has priority when both configurable shortcuts share the same key.
            if (_checkoutEnabled.Value && _checkout != null && _checkoutHotkey.Value.IsDown())
            {
                DebugLog($"Checkout hotkey pressed: {_checkoutHotkey.Value}.");
                if (_checkout.TryConfirmPayment())
                    return;
            }
            if (_scannerEnabled.Value && !holdConsumed && _hotkey.Value.IsDown())
                TrySetAimedProductPrice();
        }
        catch (Exception ex)
        {
            _checkoutHold?.Stop();
            Logger.LogError($"Hotkey action failed: {ex}");
        }
    }

    private void LateUpdate()
    {
        if (_checkout == null || _menuVisible || !_enabled.Value || !_checkoutEnabled.Value)
        {
            _checkout?.StopHeldPayment();
            return;
        }
        try
        {
            _checkout.RefreshPending();
        }
        catch (Exception ex)
        {
            Logger.LogError($"Checkout confirmation failed: {ex}");
        }
    }

    private void TrySetAimedProductPrice()
    {
        var camera = FindPlayerCamera();
        if (camera == null)
        {
            WarnOnce("camera", "Could not find the player camera.");
            return;
        }

        if (_requirePricingTool.Value && !_bridge!.IsPricingToolEquipped())
        {
            DebugLog("Local player does not have the pricing scanner equipped, or equipment state could not be read; action ignored.");
            return;
        }

        if (!Physics.Raycast(
                camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)),
                out var hit,
                _maxDistance.Value,
                _bridge!.GetScannerRaycastMask(),
                QueryTriggerInteraction.UseGlobal))
        {
            DebugLog("Raycast did not hit anything.");
            return;
        }

        if (!_bridge!.TryResolveShelfPriceTarget(hit.collider, hit.point, out var productId))
        {
            Logger.LogWarning($"Could not resolve a shelf product from scanner hit '{hit.collider.name}'. Enable Debug.VerboseLogging for the row hierarchy.");
            _bridge.DebugCollider(hit.collider);
            return;
        }

        if (!_bridge.TryGetMarketPrice(productId, out var marketPrice) || marketPrice <= 0f)
        {
            WarnOnce("market-price", $"Could not resolve market price for product ID {productId}.");
            return;
        }

        var multiplier = Mathf.Max(0f, _multiplier.Value);
        var targetPrice = NormalizePrice(marketPrice * multiplier);

        if (targetPrice <= 0f)
        {
            Logger.LogWarning($"Calculated invalid target price {targetPrice} for product ID {productId}.");
            return;
        }

        if (!_bridge.TrySetProductPrice(productId, targetPrice))
        {
            WarnOnce("set-price", "Could not locate/invoke the game's product-price update method. A game update may have renamed it.");
            return;
        }

        Logger.LogInfo($"Price update requested: product #{productId}, market {marketPrice:0.00} -> {targetPrice:0.00} ({multiplier * 100f:0.##}%).");
    }

    private static float NormalizePrice(float value)
    {
        return (float)Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private Camera? FindPlayerCamera()
    {
        if (Camera.main != null && Camera.main.isActiveAndEnabled)
            return Camera.main;

        foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (camera != null && camera.isActiveAndEnabled && camera.gameObject.activeInHierarchy)
                return camera;
        }
        return null;
    }

    private void WarnOnce(string key, string message)
    {
        if (_oneShotWarnings.Add(key))
            Logger.LogWarning(message);
    }

    private void DebugLog(string message)
    {
        if (_debugLogging.Value)
            Logger.LogInfo("[debug] " + message);
    }
}
