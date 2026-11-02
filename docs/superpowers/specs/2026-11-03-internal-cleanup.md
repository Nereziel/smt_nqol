# SMT NQOL 0.7.4 Internal Cleanup Design

## Goal

Reduce repeated runtime discovery/reflection work and simplify maintenance without adding gameplay features or changing user-visible Price Gun / checkout behavior.

## Decisions

- Keep the existing plugin GUID and `SMT.PriceGun200` namespace for configuration compatibility.
- Bump plugin/project version to `0.7.4`.
- Split the large runtime source by responsibility while keeping the same namespace and types.
- Cache reflected fields/properties by runtime `Type` and case-insensitive member name. Cache missing members as well; a loaded CLR `Type` cannot gain fields/properties later.
- Keep successful type discovery cached. Throttle failed type discovery for 5 seconds, but retry immediately when the loaded assembly count changes.
- Scope checkout product caching to the currently held register. Cache live `ProductCheckoutSpawn` components and rescan the scene only when the cache has no eligible product, at most once every 0.5 seconds. Do not assume a prefab hierarchy that has not been verified in-game.
- Make `CancelPending()` the only path that transitions checkout pending state to null, then let an already-idle `RefreshPending()` return immediately.
- Remove the generic deep product-ID crawler because the active Price Gun path deliberately resolves shelf rows through `TryResolveShelfPriceTarget`.
- Keep `FixNegativeColliders` configurable. It is a defensive game-specific workaround and removing its safety switch without live profiling would be higher risk than the configuration cost.
- Pin `BepInEx.Core` to `5.4.21` for reproducible restores.
- Run branch CI through pull requests and push CI on `main`, avoiding duplicate push + PR builds for the same feature commit.

## Target layout

```text
src/
  Plugin.cs
  Core/
    GameBridge.cs
    GameContext.cs
    ReflectionMemberCache.cs
    TypeMissCache.cs
  Features/
    PriceGun/
      PricingToolSelection.cs
      ShelfProductSelection.cs
    Checkout/
      CheckoutBridge.cs
      CheckoutColliderGeometry.cs
      CheckoutColliderRepair.cs
      CheckoutDelay.cs
      CheckoutHoldController.cs
      CheckoutPayment.cs
      CheckoutScanGate.cs
      CheckoutDiscoveryThrottle.cs
  UI/
    MenuSettings.cs
    RuntimeMenu.cs
  Tests/
```

## Validation

The existing seven regression harnesses remain required. New regression harnesses cover reflection caching, type-miss throttling, source layout/legacy removal, checkout discovery throttling, and the idle checkout invariant. Final acceptance requires a Release build with zero errors and all regression checks passing on the final branch head and on `main` after merge.
