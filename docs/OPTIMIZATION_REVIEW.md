# Optimization and Cleanup Review

This review covers the Price Gun path, automated checkout path, reflection/runtime discovery, UI/configuration, collider repair, tests, and GitHub Actions.

The goal is to reduce avoidable work and remove stale configuration without making the mod more brittle against Supermarket Together updates.

## Implemented in 0.7.3

### Price Gun defaults and rounding

- Default `MarketPriceMultiplier` changed from `2.00` to `2.01` (201%).
- Configurable `RoundDownStep` was removed from config handling and the F12 menu.
- The outgoing result is always normalized to two decimal places with `MidpointRounding.AwayFromZero`.
- Existing installs that explicitly contain `MarketPriceMultiplier = 2` remain at 200%; changing a default must not silently overwrite a user's saved choice.

Why: a raw `float` multiplication can produce values such as `20.099998`. Currency normalization is still useful, but exposing a separate arbitrary rounding step added configuration and code without a clear gameplay benefit.

### Held checkout product selection

The held checkout loop previously:

1. enumerated all active `ProductCheckoutSpawn` objects,
2. filtered them with LINQ,
3. sorted all matching products by distance,
4. copied the sorted sequence into a new array,
5. selected the first element.

It now performs one pass and keeps only the nearest valid product.

Effect:

- removes `OrderBy` and `ToArray` allocations from the repeated scan path;
- changes the selection work from `O(n log n)` sorting to `O(n)` selection;
- preserves the existing nearest-product behavior.

The Unity object-discovery call still returns an array, so this path is not allocation-free. See the high-priority follow-up below.

### Unity object discovery

Deprecated `FindObjectsOfType` fallbacks were replaced by `FindObjectsByType(..., FindObjectsSortMode.None)` where result ordering is irrelevant.

This removes obsolete API warnings and avoids Unity's unnecessary instance-ID sorting.

### CI actions

Build/release workflows were updated to current Node-24-era action majors and the unnecessary setup-dotnet LTS runtime pre-pass was disabled.

Regression coverage now includes dedicated checks for:

- 201% default pricing and cent normalization;
- removal of the old rounding configuration;
- low-allocation held checkout selection;
- non-obsolete camera discovery.

## High-priority follow-ups

### 1. Cache reflection member lookup

`GameBridge.TryGetMemberValue` and `TryGetStaticMemberValue` perform `Type.GetProperty` / `Type.GetField` every time they are called.

This is used frequently by checkout automation, including the repeated held-scan path and pending-payment validation.

Recommended approach:

- cache the resolved `PropertyInfo` or `FieldInfo` by `(Type, member name, static/instance)`;
- cache missing members too, but keep cache invalidation simple and bounded;
- keep invocation/value reads dynamic so reconnect/session replacement still works;
- add regression coverage before changing this shared compatibility layer.

Expected benefit: less reflection metadata lookup and fewer repeated name searches during active automation.

Risk: medium. This helper is central to the mod's game-update tolerance, so it should be changed separately from gameplay logic.

### 2. Throttle failed type discovery

`GameBridge.FindType` caches successful lookups, but a missing type is not cached.

If a game update renames or removes a type such as `ProductCheckoutSpawn` or `Data_Container`, callers can repeatedly scan all loaded assemblies:

- held checkout can retry at the scan interval;
- collider repair can retry every scan cycle.

Recommended approach:

- add a short negative-cache TTL, or
- retry only when the loaded assembly count changes / after a bounded interval.

Do not permanently cache a miss, because some game/plugin types can become available later in startup.

Expected benefit: major reduction in failure-path CPU cost after incompatible game updates.

Risk: low-to-medium with a TTL; higher with permanent negative caching.

### 3. Avoid global checkout-product discovery on every scan interval

The current held checkout still calls `FindObjectsByType(ProductCheckoutSpawn, ...)` for each scan attempt (default interval 0.15 s).

The new single-pass selection removes sorting/copying overhead, but global scene discovery remains the largest repeated operation in that feature.

Preferred future direction:

- resolve products from the active checkout/register hierarchy if the runtime hierarchy is reliable, or
- maintain a small cache/set scoped to the selected checkout and refresh it only when necessary.

This should be validated in the live game before implementation; relying on an assumed hierarchy would be worse than the current robust global lookup.

## Medium-priority cleanup

### 4. Idle checkout refresh does unnecessary reset work

`CheckoutBridge.RefreshPending()` currently calls `CancelPending()` when there is no pending checkout. `LateUpdate()` calls `RefreshPending()` every active frame.

The reset is small, but unnecessary while completely idle. A direct return would be cheaper.

This was intentionally not changed in this pass because `CancelPending()` also resets guard/delay state. A dedicated checkout state-machine test should be added first so the optimization cannot leave stale state after an unusual cancellation path.

### 5. Collider repair scans every Data_Container every two seconds

`CheckoutColliderRepair` is already rate-limited to a two-second interval and filters non-checkouts quickly, which is reasonable for a defensive repair feature.

If profiling shows this feature is significant, cache known checkout roots and refresh on scene/session changes instead of scanning all `Data_Container` objects repeatedly.

Priority is medium/low because the current scan is infrequent and protects against registers created or moved at runtime.

### 6. Split the large Plugin/GameBridge source

`Plugin.cs` contains both the BepInEx lifecycle/Price Gun code and the large reflection bridge.

Recommended structure:

```text
src/
  Plugin.cs
  Core/GameBridge.cs
  Core/GameContext.cs
  Features/PriceGun/...
  Features/Checkout/...
  UI/RuntimeMenu.cs
```

This is primarily a maintainability improvement: smaller review diffs, clearer ownership, and lower risk when modifying one feature.

It should be done as a behavior-neutral refactor with the existing regression suite green before and after.

### 7. Review likely legacy product-ID crawler

`GameBridge` still contains a generic deep product-ID reflection crawler (`TryResolveProductId` / `TryReadProductIdDeep`) while the current Price Gun entry path deliberately uses the shelf-specific `TryResolveShelfPriceTarget`.

It appears to be legacy compatibility code and is a candidate for removal, but it was not deleted in this pass because repository code-search results were incomplete. Remove it only after a complete reference check confirms that no runtime/test path uses it.

### 8. Pin the BepInEx package version

The project currently references:

```xml
<PackageReference Include="BepInEx.Core" Version="5.*" />
```

A floating major/minor range makes restore results change over time. Pin the exact tested BepInEx version once the currently resolved package is recorded.

This improves reproducibility more than runtime performance.

### 9. Reduce duplicate CI runs

The Build workflow runs on every branch `push` and on `pull_request`. Once a PR is open, a change can therefore cause both a push build and a PR build.

If Actions usage/noise matters, use push builds for `main` only and rely on `pull_request` for feature branches. Keeping all-branch pushes is also reasonable when pre-PR CI is useful.

## Low-priority observations

- `RuntimeMenu` reflects the PlayMaker options `Value` property while the menu is open. It could cache the property, but this is not a meaningful normal-game hot path.
- `CheckoutBridge.Invoke` resolves methods by reflection per interaction. Payment interactions are low-frequency, so member caching should be addressed before this.
- Verbose debug paths intentionally allocate and enumerate reflection metadata. They are diagnostic-only and should stay simple rather than optimized.
- `CheckoutPayment`, `CheckoutScanGate`, `CheckoutDelay`, and `MenuSettings` are small and focused; no meaningful simplification was found.
- `GameContext` already avoids repeated expensive type/component discovery on the warm path and has dedicated regression coverage for reconnect/options/permission behavior.

## Validation limits

The regression harnesses validate source-level behavior without launching Supermarket Together. They are useful for compatibility logic and state-machine invariants, but they do not measure Unity frame time, GC allocations, RPC latency, or changed game prefabs.

For further optimization, live profiling should focus on:

1. held checkout with a busy store and multiple registers;
2. reflection calls per held-scan interval;
3. `FindObjectsByType` frequency and allocations;
4. collider-repair scans in a large store;
5. reconnect/session replacement after network state changes.
