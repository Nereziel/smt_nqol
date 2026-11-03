# SMT NQOL 0.7.5 Checkout Toggle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace hold-to-run checkout automation with a contextual toggle using the existing checkout hotkey.

**Architecture:** Keep payment confirmation priority in `Plugin.Update()`. Move persistent ON/OFF automation state into `CheckoutHoldController`, which captures a valid register/player on activation and self-stops when runtime validity is lost. Preserve the existing scan gate, product cache and payment flow.

**Tech Stack:** C# / .NET Standard 2.1, BepInEx 5.4.21, Unity 2023 APIs, PowerShell regression harnesses, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-11-03-checkout-toggle.md`

## Global Constraints

- Existing checkout hotkey remains shared with manual payment confirmation.
- Manual payment confirmation has priority over automation toggle.
- Toggle activation requires a valid checkout/player context.
- Toggle stops automatically on invalid context/focus/distance/permission/NPC cashier.
- Price Gun behavior remains unchanged.
- Version becomes `0.7.5`.

## Review Focus

- A manual payment click must not also toggle automation.
- A second contextual press must stop the active automation immediately.
- Losing focus or checkout validity must clear toggle state.
- Reconnect/player replacement must not leave automation attached to stale objects.
- Pending item duplicate-scan protection must remain intact across stop/start.

---

### Task 1: Regression gate for contextual toggle

**Files:**
- Create: `src/Tests/checkout-toggle.ps1`
- Modify: `.github/workflows/build.yml`
- Modify: `.github/workflows/release.yml`

**Interfaces:**
- Consumes: `Plugin.cs`, `CheckoutHoldController.cs`, `RuntimeMenu.cs`.
- Produces: a red-first regression gate for toggle semantics and removal of hold-to-run input.

- [ ] Add assertions for payment-first ordering, `Toggle()`/persistent `Tick()` controller API, automatic stop conditions, and removal of `IsPressed()` hold wiring.
- [ ] Add the test to build/release CI.
- [ ] Verify PR CI fails before production implementation.

### Task 2: Implement persistent checkout toggle

**Files:**
- Modify: `src/Plugin.cs`
- Modify: `src/Features/Checkout/CheckoutHoldController.cs`

**Interfaces:**
- Produces: `CheckoutHoldController.Toggle()` and persistent `Tick(float interval)` behavior.

- [ ] Make the first valid contextual press activate automation and capture the checkout/player.
- [ ] Make a later contextual press deactivate it.
- [ ] Keep payment confirmation first in `Plugin.Update()` and only toggle if payment was not handled.
- [ ] Stop automatically on focus/context/distance/permission/NPC failures.
- [ ] Run the new test and full suite.

### Task 3: Config/UI/version/docs cleanup

**Files:**
- Modify: `src/Plugin.cs`
- Modify: `src/UI/RuntimeMenu.cs`
- Modify: `SMT.NQOL.csproj`
- Modify: `README.md`

**Interfaces:**
- Replace `Checkout.HoldToScan` with `Checkout.ToggleAutomation` default `true`.
- Version becomes `0.7.5`.

- [ ] Update config binding and F12 wording.
- [ ] Update README checkout controls and project version examples.
- [ ] Run full CI and review the final diff.
- [ ] Squash-merge only after the final PR head is green, then verify `main` CI.
