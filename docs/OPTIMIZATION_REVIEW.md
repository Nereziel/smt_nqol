# Optimization and Cleanup Review

This review covers Price Gun, automated checkout, runtime reflection/discovery, UI/configuration, collider repair, tests, and CI.

## Implemented through 0.7.4

### Pricing

- Fresh/default pricing remains `2.01x / 201%`.
- Outgoing prices are normalized to two decimal places.
- The old configurable `RoundDownStep` remains removed.

### Runtime reflection

- Instance/static field and property metadata is cached by runtime `Type` and case-insensitive member name.
- Values are still read from the live object on every request; only metadata lookup is cached.
- Missing members are cached because an already loaded CLR type cannot gain a field/property later.

### Type discovery

- Successful `FindType` results remain cached.
- Failed type discovery is throttled for five seconds.
- A changed loaded-assembly count invalidates the miss immediately, allowing late-loaded types to be discovered without waiting for the TTL.

### Held checkout

- Product selection is a single-pass nearest-item search; no `OrderBy` / `ToArray` hot-path sorting remains.
- Products are cached for the active register.
- The cache stores live register-owned product components and reevaluates their live `isFinished` state each scan.
- If no eligible cached item exists, scene-wide discovery is retried at most once every 0.5 seconds.
- The cache is reset when hold/register context ends or changes.

### Checkout pending state

- An already-idle `RefreshPending()` returns immediately.
- `CancelPending()` owns the pending-payment reset path, reducing duplicated partial reset logic.

### Unity discovery

- Non-obsolete `FindObjectsByType(..., FindObjectsSortMode.None)` is used where ordering is irrelevant.

### Legacy cleanup

- The generic deep product-ID reflection crawler was removed.
- The active Price Gun path remains deliberately shelf-specific through live `productInfoArray` row resolution.

### Project layout

Runtime code is organized into `Core`, `Features/PriceGun`, `Features/Checkout`, and `UI`. `Plugin.cs` now contains the BepInEx lifecycle/Price Gun entrypoint instead of the complete reflection bridge.

### Reproducible build

- Project/plugin version: `0.7.4`.
- `BepInEx.Core` is pinned to `5.4.21` instead of a floating `5.*` range.
- Build and release workflows execute ten regression harnesses.
- Feature-branch validation is handled by pull-request CI; direct push CI is retained for `main`.

## Remaining candidates

### 1. Profile checkout scene discovery in-game

The 0.5-second fallback throttle substantially reduces repeated global lookup, but `FindObjectsByType(ProductCheckoutSpawn, ...)` remains the compatibility fallback when the register-scoped cache has no eligible item.

A future optimization should replace this only after the actual runtime prefab hierarchy is profiled. Assuming a hierarchy without live validation could make the mod faster but less reliable after game updates.

### 2. Profile collider repair

`CheckoutColliderRepair` scans `Data_Container` objects on a two-second interval. This is intentionally conservative because checkouts can be created or moved at runtime. If profiling shows measurable cost, known checkout roots could be cached and refreshed on scene/session changes with the periodic scan retained as a low-frequency safety fallback.

### 3. Cache low-frequency reflected methods only if profiling justifies it

Payment helpers still resolve a few interaction methods dynamically. They are low-frequency compared with held checkout member reads, so caching them has lower expected value and creates more invalidation complexity.

### 4. Consider generated/typed adapters only if game internals stabilize

The current reflection layer intentionally favors game-update tolerance over maximum raw speed. Replacing it with hard references to game assemblies would reduce reflection but would make updates more brittle and require distributing/building against game binaries.

## Deliberately retained

- `FixNegativeColliders` remains configurable because it is a game-specific repair with a useful emergency off switch.
- The existing plugin GUID and `SMT.PriceGun200` namespace remain for configuration compatibility.
- Verbose debug paths intentionally favor diagnostics over allocation minimization because they are disabled during normal use.
- Market-price fallbacks remain because they provide compatibility when the preferred game method changes.

## Validation limits

The PowerShell harnesses validate source behavior and important state invariants without launching Supermarket Together. They do not measure Unity frame time, GC allocations, network latency, or changed game prefabs.

For live profiling, focus on:

1. held checkout in a busy store with multiple registers;
2. scene-discovery frequency while customers unload products;
3. reflection calls and GC allocation per held-scan interval;
4. collider-repair scans in a large store;
5. disconnect/reconnect and late initialization after network/session replacement.
