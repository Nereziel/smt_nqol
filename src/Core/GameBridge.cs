using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace SMT.PriceGun200;

internal sealed class GameBridge
{
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

        // Legacy market-price fallback: productPrefabs[productId] -> Data_Product.
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
    private static readonly TypeMissCache TypeMisses = new();

    internal static Type? FindType(string simpleName)
    {
        if (TypeCache.TryGetValue(simpleName, out var cached))
            return cached;

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        var nowTicks = DateTime.UtcNow.Ticks;
        if (TypeMisses.ShouldSkip(simpleName, assemblies.Length, nowTicks))
            return null;

        foreach (var assembly in assemblies)
        {
            Type? exact = null;
            try
            {
                exact = assembly.GetTypes().FirstOrDefault(t => t.Name.Equals(simpleName, StringComparison.Ordinal));
            }
            catch (ReflectionTypeLoadException ex)
            {
                exact = ex.Types.FirstOrDefault(t => t != null && t.Name.Equals(simpleName, StringComparison.Ordinal));
            }
            catch
            {
                // Ignore dynamic/inaccessible assemblies.
            }

            if (exact == null)
                continue;
            TypeCache[simpleName] = exact;
            TypeMisses.Clear(simpleName);
            return exact;
        }

        TypeMisses.RecordMiss(simpleName, assemblies.Length, nowTicks);
        return null;
    }

    internal static bool TryGetStaticMemberValue(Type type, string name, out object? value)
    {
        return ReflectionMemberCache.TryGetStaticValue(type, name, out value);
    }

    internal static bool TryGetMemberValue(object obj, string name, out object? value)
    {
        return ReflectionMemberCache.TryGetInstanceValue(obj, name, out value);
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
                // Continue recursively.
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
