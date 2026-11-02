# SMT NQOL 0.7.4 Internal Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce repeated reflection and scene-discovery work, remove verified legacy code, and reorganize the source without changing gameplay behavior.

**Architecture:** Preserve all existing runtime type names, namespace, GUID, and feature behavior. Introduce small cache/throttle helpers under `Core` and `Features/Checkout`, move existing sources into responsibility-based folders, and keep game-update-sensitive reflection logic defensive.

**Tech Stack:** C# / .NET Standard 2.1, BepInEx 5, Unity 2023 APIs, PowerShell regression harnesses, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-11-03-internal-cleanup.md`

## Global Constraints

- Preserve `cz.milan.supermarkettogether.pricegun200` and namespace `SMT.PriceGun200`.
- Version becomes `0.7.4`.
- No new gameplay features.
- Existing 201% default pricing remains unchanged.
- Checkout safety/duplicate-scan/payment invariants must remain unchanged.
- Do not rely on unverified checkout prefab hierarchy assumptions.

## Review Focus

- Reconnect/session replacement must not reuse stale Unity objects or reflection method caches.
- A type missing during startup must be retried when assemblies change and after the 5-second miss TTL.
- Cached reflected members must preserve case-insensitive property/field behavior and tolerate throwing getters.
- Held checkout must still detect newly spawned belt products while reducing global discovery frequency.
- Idle checkout state must not retain a partially pending payment after cancellation.

---

### Task 1: Regression gates for cleanup behavior

**Files:**
- Create: `src/Tests/reflection-cache.ps1`
- Create: `src/Tests/checkout-cache.ps1`
- Create: `src/Tests/structure.ps1`
- Modify: `.github/workflows/build.yml`
- Modify: `.github/workflows/release.yml`

**Interfaces:**
- Consumes: current source tree.
- Produces: failing regression checks for the missing 0.7.4 architecture and cache behavior.

- [ ] Write the three regression harnesses.
- [ ] Add them to Build and Release workflows.
- [ ] Run CI and verify the new checks fail for the intended missing behavior/files.

### Task 2: Reflection and type-discovery caches

**Files:**
- Create: `src/Core/ReflectionMemberCache.cs`
- Create: `src/Core/TypeMissCache.cs`
- Create: `src/Core/GameBridge.cs`
- Modify: `src/Plugin.cs`
- Move: `src/GameContext.cs` -> `src/Core/GameContext.cs`

**Interfaces:**
- Consumes: `GameBridge.TryGetMemberValue`, `TryGetStaticMemberValue`, `FindType` call sites.
- Produces: same public/internal `GameBridge` helper signatures backed by cached member metadata and 5-second type-miss throttling.

- [ ] Implement member metadata cache preserving case-insensitive property-first/field-fallback semantics.
- [ ] Implement type-miss throttling with assembly-count invalidation.
- [ ] Extract `GameBridge` from `Plugin.cs` without changing its active pricing behavior.
- [ ] Remove `TryResolveProductId`, `TryReadProductId*`, `LooksLikeProductIdMember`, `ProductIdNames`, and helpers used only by that legacy crawler.
- [ ] Run new cache tests and full regression suite.

### Task 3: Checkout discovery/cache and idle invariant

**Files:**
- Create: `src/Features/Checkout/CheckoutDiscoveryThrottle.cs`
- Modify/move: `src/CheckoutHoldController.cs` -> `src/Features/Checkout/CheckoutHoldController.cs`
- Modify/move: `src/CheckoutBridge.cs` -> `src/Features/Checkout/CheckoutBridge.cs`

**Interfaces:**
- Consumes: `GameBridge.FindType`, member accessors, existing scan gate/payment APIs.
- Produces: register-scoped product cache with immediate first discovery and max one retry per 0.5 seconds while no eligible product exists.

- [ ] Implement throttle helper and regression test behavior.
- [ ] Cache product components for the active register and search cached components before global discovery.
- [ ] Reset checkout product cache on hold/register reset.
- [ ] Make `CancelPending()` the only null transition for `_pendingCheckout` and make idle `RefreshPending()` return directly.
- [ ] Run checkout/cache tests and full regression suite.

### Task 4: Source layout and reproducible build cleanup

**Files:**
- Move remaining Price Gun files to `src/Features/PriceGun/`.
- Move remaining checkout files to `src/Features/Checkout/`.
- Move UI files to `src/UI/`.
- Modify all `src/Tests/*.ps1` source paths.
- Modify `SMT.NQOL.csproj`.
- Modify `.github/workflows/build.yml`.
- Modify `README.md` and `docs/OPTIMIZATION_REVIEW.md`.

**Interfaces:**
- Consumes: same namespace/types; SDK recursive compile.
- Produces: responsibility-based source layout, exact BepInEx `5.4.21`, version `0.7.4`, non-duplicated feature-branch CI.

- [ ] Move unchanged files using their existing blobs where possible.
- [ ] Update test source paths.
- [ ] Pin BepInEx and bump project/plugin version.
- [ ] Restrict push Build workflow to `main`; keep `pull_request` and manual dispatch.
- [ ] Update documentation.
- [ ] Run final PR CI and require zero build errors plus all regression checks green.

### Task 5: Review and integration

**Files:** no new runtime files unless review finds an issue.

**Interfaces:**
- Consumes: complete branch diff.
- Produces: reviewed merge-ready PR.

- [ ] Review the complete diff for behavior changes, stale references, allocations, reconnect safety, and test coverage.
- [ ] Fix Critical/Important findings with regression coverage.
- [ ] Re-run final CI.
- [ ] Squash-merge only after the final head is green.
- [ ] Verify the post-merge `main` workflow is green.
