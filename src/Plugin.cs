using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace SMT.PriceGun200;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInProcess("Supermarket Together.exe")]
public sealed partial class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "cz.milan.supermarkettogether.pricegun200";
    public const string PluginName = "SMT NQOL";
    public const string PluginVersion = "0.7.3";

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

internal sealed class GameBridge
{
    private static readonly string[] ProductIdNames =
    {
        "productid", "product_id", "productindex", "product_index", "idproduct", "productcode", "product_code",
        "producttype", "product_type", "itemid", "item_id", "itemtype", "item_type"
    };

    private readonly ManualLogSource _log;
    private readonly Func<bool> _verbose;

    private Type? _productListingType;
    private object? _productListingInstance;
    private MethodInfo? _marketPriceMethod;
    private MethodInfo? _setPriceMethod;

    public GameBridge(ManualLogSource log, Func<bool> verbose)
    {
        _log = log;
        _verbose = verbose;
    }

    public bool IsPricingToolEquipped()
    {
        var player = GameContext.LocalPlayer;
        if (player == null || !TryGetMemberValue(player, "equippedItem", out var value) || !TryConvertInt(value, out var item))
        {
            Debug("Local PlayerNetwork or equippedItem not available; pricing ignored.");
            return false;
        }
        Debug($"Local PlayerNetwork.equippedItem = {item}; pricing scanner = {PricingToolSelection.ScannerItemId}.");
        return PricingToolSelection.IsEquipped(true, item);
    }

    public int GetScannerRaycastMask()
    {
        var player = GameContext.LocalPlayer;
        if (player != null && TryGetMemberValue(player, "interactableMask", out var value))
        {
            if (value is LayerMask mask && mask.value != 0) return mask.value;
            if (value is int bits && bits != 0) return bits;
        }
        Debug("Scanner interactableMask unavailable; using default raycast layers.");
        return Physics.DefaultRaycastLayers;
    }

    public bool TryResolveShelfPriceTarget(Collider collider, Vector3 hitPoint, out int productId)
    {
        // Pricing is deliberately limited to shelf rows and labels, never checkout belt items.
        return TryResolveShelfProductId(collider.transform, hitPoint, out productId);
    }

    public bool TryResolveProductId(Collider collider, Vector3 hitPoint, out int productId)
    {
        productId = -1;
        var visited = new HashSet<int>();

        // Current game shelves store [product ID, quantity] pairs, not IDs on product prefabs.
        if (TryResolveShelfProductId(collider.transform, hitPoint, out productId))
            return true;
        // An unassigned shelf row must not fall back to another row or a prefab's default ID.
        if (collider.GetComponentsInParent<Component>(true)
            .Any(c => c != null && c.GetType().Name == "Data_Container"))
            return false;

        // 1) Exact hit object and its parent chain. This is the cheapest and safest path.
        var parentDepth = 0;
        for (var current = collider.transform; current != null && parentDepth++ < 4; current = current.parent)
        {
            foreach (var component in current.GetComponents<Component>())
            {
                if (component == null)
                    continue;

                if (TryReadProductIdDeep(component, 3, visited, out productId))
                {
                    Debug($"Resolved product ID {productId} from {component.GetType().FullName} on {current.name}.");
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryResolveShelfProductId(Transform hit, Vector3 hitPoint, out int productId)
    {
        productId = -1;
        for (var shelf = hit; shelf != null; shelf = shelf.parent)
        {
            var container = shelf.GetComponents<Component>()
                .FirstOrDefault(c => c != null && c.GetType().Name == "Data_Container");
            if (container == null)
                continue;

            if (!TryGetMemberValue(container, "productInfoArray", out var value) || value is not int[] data)
                return false;

            // Products and labels share the same row index in the game's shelf hierarchy.
            for (var row = hit; row != null && row != shelf; row = row.parent)
            {
                if (row.parent != null && row.parent.parent == shelf &&
                    (row.GetComponents<Component>().Any(c => c != null && c.GetType().Name == "InteractableContainer") ||
                     row.parent.name == "ProductContainer" || row.parent.name == "Labels"))
                {
                    var resolved = ShelfProductSelection.TryRead(data, row.GetSiblingIndex(), out productId);
                    Debug(resolved
                        ? $"Resolved live shelf product ID {productId} from row {row.GetSiblingIndex()} on {shelf.name}."
                        : $"Scanner row {row.GetSiblingIndex()} on {shelf.name} has no assigned product yet (data length {data.Length}).");
                    return resolved;
                }
            }

            // A shelf's collider can cover several rows. Only use a nearby visible label,
            // never the first product in the shelf or the global catalogue.
            var labels = shelf.Find("Labels");
            if (labels == null)
                return false;
            var nearestRow = -1;
            var nearestDistance = 0.45f * 0.45f;
            for (var i = 0; i < labels.childCount; i++)
            {
                var label = labels.GetChild(i);
                if (!label.gameObject.activeInHierarchy)
                    continue;
                var position = label.childCount > 0 ? label.GetChild(0).position : label.position;
                var distance = (position - hitPoint).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestRow = i;
                }
            }
            return ShelfProductSelection.TryRead(data, nearestRow, out productId);
        }
        return false;
    }

    public bool TryGetMarketPrice(int productId, out float marketPrice)
    {
        marketPrice = 0f;

        if (!EnsureProductListing())
            return false;

        if (TryGetMarketPriceViaMethod(productId, out marketPrice))
            return true;

        if (TryGetProductRecord(productId, out var productRecord) && productRecord != null)
        {
            if (!TryReadNumericRecursive(productRecord, "basePricePerUnit", 3, out var basePrice))
                TryReadNumericRecursive(productRecord, "basePrice", 3, out basePrice);

            var tier = 0;
            TryReadIntRecursive(productRecord, "productTier", 3, out tier);

            if (basePrice > 0f)
            {
                var inflation = GetTierInflation(tier);
                marketPrice = basePrice * inflation;
                Debug($"Market price fallback: base={basePrice}, tier={tier}, inflation={inflation}, market={marketPrice}.");
                return marketPrice > 0f;
            }
        }

        // Legacy fallback: productPrefabs[productId] -> Data_Product.
        if (TryGetMemberValue(_productListingInstance!, "productPrefabs", out var prefabs) &&
            TryGetIndexedValue(prefabs, productId, out var prefab) &&
            prefab is GameObject gameObject)
        {
            foreach (var component in gameObject.GetComponents<Component>())
            {
                if (component == null)
                    continue;

                if (!component.GetType().Name.Equals("Data_Product", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (TryReadNumericRecursive(component, "basePricePerUnit", 2, out var basePrice))
                {
                    TryReadIntRecursive(component, "productTier", 2, out var tier);
                    marketPrice = basePrice * GetTierInflation(tier);
                    return marketPrice > 0f;
                }
            }
        }

        return false;
    }

    public bool TrySetProductPrice(int productId, float price)
    {
        if (!EnsureProductListing())
            return false;

        _setPriceMethod ??= FindSetPriceMethod(_productListingType!);
        if (_setPriceMethod == null)
            return false;

        try
        {
            var args = BuildPriceArguments(_setPriceMethod, productId, price);
            _setPriceMethod.Invoke(_productListingInstance, args);
            Debug($"Invoked {_productListingType!.FullName}.{_setPriceMethod.Name}({productId}, {price}).");
            return true;
        }
        catch (TargetInvocationException ex)
        {
            _log.LogError($"Game price update method threw: {ex.InnerException ?? ex}");
            return false;
        }
        catch (Exception ex)
        {
            _log.LogError($"Failed invoking price update method: {ex}");
            return false;
        }
    }

    public void DebugCollider(Collider collider)
    {
        if (!_verbose())
            return;

        _log.LogInfo($"[debug] Hit collider: {collider.name} ({collider.GetType().FullName})");
        for (var current = collider.transform; current != null; current = current.parent)
        {
            var components = current.GetComponents<Component>()
                .Where(c => c != null)
                .ToArray();
            _log.LogInfo($"[debug] Transform {current.name}: {string.Join(", ", components.Select(c => c.GetType().FullName))}");

            foreach (var component in components)
                DebugProductLikeMembers(component);
        }

        var hitPosition = collider.bounds.center;
        foreach (var near in Physics.OverlapSphere(hitPosition, 0.5f, ~0, QueryTriggerInteraction.Collide).Take(25))
        {
            _log.LogInfo($"[debug] Nearby collider: {near.name} path={GetTransformPath(near.transform)}");
            foreach (var component in near.GetComponentsInParent<Component>(true).Where(c => c != null).Take(40))
                DebugProductLikeMembers(component);
        }
    }

    private void DebugProductLikeMembers(object obj)
    {
        if (!_verbose() || obj == null)
            return;

        var type = obj.GetType();
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var field in type.GetFields(flags))
        {
            var n = field.Name.ToLowerInvariant();
            if (!(n.Contains("product") || n.Contains("item") || n.Contains("price") || n.Contains("target") || n.Contains("selected")))
                continue;
            try
            {
                var value = field.GetValue(obj);
                _log.LogInfo($"[debug]   {type.Name}.{field.Name} ({field.FieldType.Name}) = {FormatDebugValue(value)}");
            }
            catch { }
        }

        foreach (var property in type.GetProperties(flags))
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0)
                continue;
            var n = property.Name.ToLowerInvariant();
            if (!(n.Contains("product") || n.Contains("item") || n.Contains("price") || n.Contains("target") || n.Contains("selected")))
                continue;
            try
            {
                var value = property.GetValue(obj, null);
                _log.LogInfo($"[debug]   {type.Name}.{property.Name} ({property.PropertyType.Name}) = {FormatDebugValue(value)}");
            }
            catch { }
        }
    }

    private static string FormatDebugValue(object? value)
    {
        if (value == null) return "<null>";
        if (value is UnityEngine.Object unityObject) return $"{unityObject.name} [{value.GetType().Name}]";
        var text = value.ToString() ?? "<null>";
        return text.Length <= 160 ? text : text.Substring(0, 160) + "...";
    }

    private static string GetTransformPath(Transform transform)
    {
        var parts = new List<string>();
        for (var current = transform; current != null; current = current.parent)
            parts.Add(current.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    private bool EnsureProductListing()
    {
        // Types survive reconnects; Unity network objects do not. Resolve the live instance
        // on every action, even when the old managed wrapper is still a non-null object.
        _productListingType ??= FindType("ProductListing");
        object? current = null;
        if (_productListingType != null)
        {
            if (TryGetStaticMemberValue(_productListingType, "Instance", out var singleton) && IsLiveProductListing(singleton))
                current = singleton;
            else if (typeof(UnityEngine.Object).IsAssignableFrom(_productListingType))
            {
                foreach (var candidate in UnityEngine.Object.FindObjectsByType(_productListingType, FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (!IsLiveProductListing(candidate))
                        continue;
                    current = candidate;
                    break;
                }
            }
        }
        if (!ReferenceEquals(_productListingInstance, current))
        {
            _marketPriceMethod = null;
            _setPriceMethod = null;
            Debug(current == null ? "ProductListing session ended; cached instance cleared."
                : "ProductListing live network instance refreshed for the current session.");
        }
        _productListingInstance = current;
        if (current == null)
        {
            Debug("No live, initialized ProductListing client instance; price action ignored.");
            return false;
        }
        return true;
    }

    private static bool IsLiveProductListing(object? instance)
    {
        // This typed check uses Unity's destroyed-object semantics, unlike object != null.
        if (instance is not MonoBehaviour behaviour || behaviour == null ||
            !behaviour.isActiveAndEnabled || !behaviour.gameObject.activeInHierarchy)
            return false;
        return TryGetMemberValue(instance, "isClient", out var client) && client is bool isClient && isClient &&
               TryGetMemberValue(instance, "netId", out var id) && id is uint netId && netId != 0;
    }

    private bool TryGetMarketPriceViaMethod(int productId, out float marketPrice)
    {
        marketPrice = 0f;

        if (_marketPriceMethod == null)
        {
            _marketPriceMethod = _productListingType!
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.GetParameters().Length == 1)
                .Where(m => IsProductKeyType(m.GetParameters()[0].ParameterType))
                .Where(m => IsNumericType(m.ReturnType))
                .OrderByDescending(m => m.Name.Equals("GetMarketPrice", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(m => m.Name.IndexOf("market", StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenByDescending(m => m.Name.IndexOf("price", StringComparison.OrdinalIgnoreCase) >= 0)
                .FirstOrDefault(m =>
                    m.Name.IndexOf("market", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    m.Name.IndexOf("price", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        if (_marketPriceMethod == null)
            return false;

        try
        {
            var parameterType = _marketPriceMethod.GetParameters()[0].ParameterType;
            var idArg = ConvertProductKey(productId, parameterType);
            var value = _marketPriceMethod.Invoke(_productListingInstance, new[] { idArg });
            marketPrice = Convert.ToSingle(value, CultureInfo.InvariantCulture);
            Debug($"Market price via {_marketPriceMethod.Name}: {marketPrice}.");
            return marketPrice > 0f;
        }
        catch (Exception ex)
        {
            Debug($"Market-price method {_marketPriceMethod.Name} failed: {ex.Message}");
            _marketPriceMethod = null;
            return false;
        }
    }

    private bool TryGetProductRecord(int productId, out object? productRecord)
    {
        productRecord = null;

        foreach (var memberName in new[] { "productsData", "productData", "products", "productPrefabs" })
        {
            if (!TryGetMemberValue(_productListingInstance!, memberName, out var collection))
                continue;

            if (!TryGetIndexedValue(collection, productId, out productRecord) || productRecord == null)
                continue;

            Debug($"Resolved product record from ProductListing.{memberName}[{productId}] ({productRecord.GetType().FullName}).");
            return true;
        }

        return false;
    }

    private float GetTierInflation(int tier)
    {
        if (_productListingInstance == null)
            return 1f;

        if (!TryGetMemberValue(_productListingInstance, "tierInflation", out var inflationCollection))
            return 1f;

        if (!TryGetIndexedValue(inflationCollection, tier, out var value) || value == null)
            return 1f;

        try
        {
            return Convert.ToSingle(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 1f;
        }
    }

    private bool TryReadProductId(object obj, out int productId)
    {
        return TryReadProductIdDeep(obj, 3, new HashSet<int>(), out productId);
    }

    private bool TryReadProductIdDeep(object obj, int depth, HashSet<int> visited, out int productId)
    {
        productId = -1;
        if (obj == null || depth < 0 || obj is IEnumerable ||
            obj.GetType().Name == "ProductListing" || obj.GetType().Name == "Data_Container")
            return false;

        var identity = obj is UnityEngine.Object unityObject ? unityObject.GetInstanceID() : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        if (!visited.Add(identity))
            return false;

        var type = obj.GetType();
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (var field in type.GetFields(flags))
        {
            if (!LooksLikeProductIdMember(field.Name, field.FieldType))
                continue;

            object? fieldValue;
            try { fieldValue = field.GetValue(obj); } catch { continue; }
            if (TryConvertNonNegativeInt(fieldValue, out productId))
            {
                Debug($"Product key candidate: {type.FullName}.{field.Name} = {productId} ({field.FieldType.Name}).");
                return true;
            }
        }

        foreach (var property in type.GetProperties(flags))
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0 ||
                !LooksLikeProductIdMember(property.Name, property.PropertyType))
                continue;

            try
            {
                if (TryConvertNonNegativeInt(property.GetValue(obj, null), out productId))
                {
                    Debug($"Product key candidate: {type.FullName}.{property.Name} = {productId} ({property.PropertyType.Name}).");
                    return true;
                }
            }
            catch
            {
                // Some game properties throw outside their intended state.
            }
        }

        if (depth == 0)
            return false;

        // Follow only members that plausibly lead to the currently selected product. This covers
        // currentProduct, productData, selectedItem, targetProduct, etc. without crawling the full game graph.
        foreach (var member in type.GetFields(flags).Cast<MemberInfo>().Concat(type.GetProperties(flags)))
        {
            var memberName = member.Name.ToLowerInvariant();
            if (!(memberName.Contains("product") || memberName.Contains("item") || memberName.Contains("data") ||
                  memberName.Contains("target") || memberName.Contains("selected") || memberName.Contains("current")) ||
                memberName.Contains("prefab") || memberName.Contains("listing"))
                continue;

            if (!TryGetMemberValue(obj, member.Name, out var nested) || nested == null || nested is string)
                continue;

            var nestedType = nested.GetType();

            // A newer build may store the selected product directly as an enum in a field such as productType.
            if ((nestedType.IsEnum || IsIntegerType(nestedType)) &&
                (memberName.Contains("product") || memberName.Contains("item")) &&
                TryConvertNonNegativeInt(nested, out productId))
            {
                Debug($"Nested product key candidate: {type.FullName}.{member.Name} = {productId} ({nestedType.Name}).");
                return true;
            }

            if (nestedType.IsPrimitive || nestedType.IsEnum)
                continue;

            // Collections contain catalogue entries or multiple rows, not the aimed product.
            if (nested is IEnumerable)
                continue;

            if (TryReadProductIdDeep(nested, depth - 1, visited, out productId))
                return true;
        }

        return false;
    }

    private static bool LooksLikeProductIdMember(string name, Type memberType)
    {
        if (!IsProductKeyType(memberType))
            return false;

        var normalized = name.Replace("-", "_").ToLowerInvariant();
        if (ProductIdNames.Contains(normalized))
            return true;

        if (normalized.Contains("product"))
        {
            return normalized.Contains("id") || normalized.Contains("index") || normalized.Contains("code") ||
                   normalized.Contains("type") || normalized == "product" || normalized.EndsWith("product");
        }

        return normalized.Contains("item") &&
               (normalized.Contains("id") || normalized.Contains("index") || normalized.Contains("type"));
    }

    private static MethodInfo? FindSetPriceMethod(Type type)
    {
        var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        return methods
            .Where(m => m.GetParameters().Length == 2)
            .Where(m => IsProductKeyType(m.GetParameters()[0].ParameterType))
            .Where(m => IsNumericType(m.GetParameters()[1].ParameterType))
            .OrderByDescending(m => m.Name.Equals("CmdUpdateProductPrice", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(m => m.Name.Equals("UpdateProductPrice", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(m => m.Name.IndexOf("cmd", StringComparison.OrdinalIgnoreCase) >= 0)
            .FirstOrDefault(m =>
                m.Name.IndexOf("product", StringComparison.OrdinalIgnoreCase) >= 0 &&
                m.Name.IndexOf("price", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (m.Name.IndexOf("update", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 m.Name.IndexOf("set", StringComparison.OrdinalIgnoreCase) >= 0));
    }

    private static object?[] BuildPriceArguments(MethodInfo method, int productId, float price)
    {
        var parameters = method.GetParameters();
        return new object?[]
        {
            ConvertProductKey(productId, parameters[0].ParameterType),
            Convert.ChangeType(price, parameters[1].ParameterType, CultureInfo.InvariantCulture)
        };
    }

    private static readonly Dictionary<string, Type> TypeCache = new(StringComparer.Ordinal);

    internal static Type? FindType(string simpleName)
    {
        if (TypeCache.TryGetValue(simpleName, out var cached))
            return cached;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var exact = assembly.GetTypes().FirstOrDefault(t => t.Name.Equals(simpleName, StringComparison.Ordinal));
                if (exact != null)
                {
                    TypeCache[simpleName] = exact;
                    return exact;
                }
            }
            catch (ReflectionTypeLoadException ex)
            {
                var exact = ex.Types.FirstOrDefault(t => t != null && t.Name.Equals(simpleName, StringComparison.Ordinal));
                if (exact != null)
                {
                    TypeCache[simpleName] = exact;
                    return exact;
                }
            }
            catch
            {
                // Ignore dynamic/inaccessible assemblies.
            }
        }

        return null;
    }

    internal static bool TryGetStaticMemberValue(Type type, string name, out object? value)
    {
        value = null;
        var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

        var property = type.GetProperty(name, flags);
        if (property != null && property.CanRead)
        {
            try
            {
                value = property.GetValue(null, null);
                return true;
            }
            catch
            {
                // ignored
            }
        }

        var field = type.GetField(name, flags);
        if (field != null)
        {
            try
            {
                value = field.GetValue(null);
                return true;
            }
            catch
            {
                // ignored
            }
        }

        return false;
    }

    internal static bool TryGetMemberValue(object obj, string name, out object? value)
    {
        value = null;
        var type = obj.GetType();
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

        var property = type.GetProperty(name, flags);
        if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
        {
            try
            {
                value = property.GetValue(obj, null);
                return true;
            }
            catch
            {
                // ignored
            }
        }

        var field = type.GetField(name, flags);
        if (field != null)
        {
            try
            {
                value = field.GetValue(obj);
                return true;
            }
            catch
            {
                // ignored
            }
        }

        return false;
    }

    private static bool TryGetIndexedValue(object? collection, int index, out object? value)
    {
        value = null;
        if (collection == null || index < 0)
            return false;

        if (collection is Array array)
        {
            if (index >= array.Length)
                return false;
            value = array.GetValue(index);
            return true;
        }

        if (collection is IList list)
        {
            if (index >= list.Count)
                return false;
            value = list[index];
            return true;
        }

        var type = collection.GetType();
        var countProperty = type.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var itemProperty = type.GetProperty("Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (countProperty != null && itemProperty != null)
        {
            try
            {
                var count = Convert.ToInt32(countProperty.GetValue(collection, null), CultureInfo.InvariantCulture);
                if (index >= count)
                    return false;
                value = itemProperty.GetValue(collection, new object[] { index });
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryReadNumericRecursive(object obj, string memberName, int depth, out float value)
    {
        value = 0f;
        if (depth < 0 || obj == null)
            return false;

        if (TryGetMemberValue(obj, memberName, out var direct) && direct != null)
        {
            try
            {
                value = Convert.ToSingle(direct, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                // continue recursively
            }
        }

        if (depth == 0)
            return false;

        foreach (var child in EnumerateLikelyNestedValues(obj))
        {
            if (TryReadNumericRecursive(child, memberName, depth - 1, out value))
                return true;
        }

        return false;
    }

    private static bool TryReadIntRecursive(object obj, string memberName, int depth, out int value)
    {
        value = 0;
        if (depth < 0 || obj == null)
            return false;

        if (TryGetMemberValue(obj, memberName, out var direct) && TryConvertInt(direct, out value))
            return true;

        if (depth == 0)
            return false;

        foreach (var child in EnumerateLikelyNestedValues(obj))
        {
            if (TryReadIntRecursive(child, memberName, depth - 1, out value))
                return true;
        }

        return false;
    }

    private static IEnumerable<object> EnumerateLikelyNestedValues(object obj)
    {
        var type = obj.GetType();
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (var field in type.GetFields(flags))
        {
            if (!LooksLikeNestedDataMember(field.Name, field.FieldType))
                continue;

            object? value;
            try { value = field.GetValue(obj); } catch { continue; }
            if (value != null) yield return value;
        }

        foreach (var property in type.GetProperties(flags))
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0 ||
                !LooksLikeNestedDataMember(property.Name, property.PropertyType))
                continue;

            object? value;
            try { value = property.GetValue(obj, null); } catch { continue; }
            if (value != null) yield return value;
        }
    }

    private static bool LooksLikeNestedDataMember(string name, Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type == typeof(string))
            return false;

        var n = name.ToLowerInvariant();
        return n.Contains("product") || n.Contains("data") || n.Contains("item") || n.Contains("prefab");
    }

    private static bool TryConvertNonNegativeInt(object? value, out int result)
    {
        result = -1;
        if (!TryConvertInt(value, out var converted) || converted < 0)
            return false;
        result = converted;
        return true;
    }

    private static bool TryConvertInt(object? value, out int result)
    {
        result = 0;
        if (value == null)
            return false;

        try
        {
            result = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsProductKeyType(Type type)
    {
        return IsIntegerType(type) || type.IsEnum;
    }

    private static object ConvertProductKey(int productId, Type targetType)
    {
        if (targetType.IsEnum)
            return Enum.ToObject(targetType, productId);

        return Convert.ChangeType(productId, targetType, CultureInfo.InvariantCulture);
    }

    private static bool IsIntegerType(Type type)
    {
        return type == typeof(int) || type == typeof(uint) || type == typeof(short) || type == typeof(ushort) ||
               type == typeof(byte) || type == typeof(sbyte) || type == typeof(long) || type == typeof(ulong);
    }

    private static bool IsNumericType(Type type)
    {
        return IsIntegerType(type) || type == typeof(float) || type == typeof(double) || type == typeof(decimal);
    }

    private void Debug(string message)
    {
        if (_verbose())
            _log.LogInfo("[debug] " + message);
    }
}
