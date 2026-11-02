# SMT NQOL

A client-side BepInEx 5 quality-of-life mod for **Supermarket Together** that combines two independent features in one DLL:

- **Price Gun helper** — quickly set the aimed shelf product to a configurable percentage of its current market price.
- **Automated Checkout** — assist with belt scanning and cash/card payment confirmation while using a checkout.

The features are configured separately and can be enabled or disabled independently.

## Features

### Price Gun

Aim at a shelf product or label with the pricing scanner and press the configured hotkey.

Default behavior:

- Hotkey: **middle mouse button** (`Mouse2`)
- Multiplier: **2.01x / 201%** of the current market price
- Pricing scanner required by default
- Final outgoing price is normalized to two decimal places to avoid floating-point noise
- Uses the game's normal product-price update path instead of directly editing a local price field

Default calculation:

```text
sellingPrice = round(rawMarketPrice * 2.01, 2)
```

The old configurable `RoundDownStep` setting was removed. Currency-style cent normalization is always applied internally; there is no separate rounding control anymore.

Example configuration:

```ini
[Scanner]
Enabled = true
Hotkey = Mouse2
MaxDistance = 6
RequirePricingTool = true

[Pricing]
MarketPriceMultiplier = 2.01
```

`RequirePricingTool = true` checks the local player's equipped item and only allows the action while the pricing scanner is equipped.

### Automated Checkout

The checkout helper can automate the repetitive parts of operating a register while still using the game's normal interaction flow.

Default behavior:

- Hotkey: **right mouse button** (`Mouse1`)
- Short press: accept/complete the currently aimed cash or card payment
- Hold for about **0.3 seconds** while aiming at a checkout or belt product: scan products one at a time and continue into payment handling
- Waits for the server/game state before submitting the next belt item
- Caches belt products for the active register and throttles scene-wide fallback discovery
- Supports exact cash change and card total entry
- Respects cashier permission checks
- Stops local automation when the key is released, focus/context is lost, the player changes, or the register is no longer valid
- Includes optional repair for invalid negative checkout `BoxCollider` geometry found on some checkout prefabs

Example configuration:

```ini
[Checkout]
Enabled = true
ConfirmHotkey = Mouse1
HoldToScan = true
ScanInterval = 0.15
AfterPickupDelay = 1
ConfirmDelay = 1
FixNegativeColliders = true
```

`AfterPickupDelay` and `ConfirmDelay` accept values from `0` to `60` seconds.

## In-game settings

Press **F12** to open the settings window.

The menu includes the master toggle, Price Gun settings, checkout settings, hotkeys, price multiplier, distances, payment delays, collider repair and verbose logging.

Press **Esc** to close the menu or cancel key capture. Unapplied draft changes are discarded when the menu closes.

## Installation

1. Install **BepInEx 5 x64** for Supermarket Together.
2. Download the latest release ZIP.
3. Extract `SMT.NQOL.dll` to a dedicated plugin folder, for example:

```text
Supermarket Together\BepInEx\plugins\SMT.NQOL\SMT.NQOL.dll
```

4. Start the game.

The mod creates its BepInEx configuration file on first run.

### Configuration compatibility

The existing plugin GUID is intentionally retained so current installations keep using the same configuration file:

```text
BepInEx\config\cz.milan.supermarkettogether.pricegun200.cfg
```

This avoids resetting existing key bindings and settings during the repository/project rename. Existing installations that already saved `MarketPriceMultiplier = 2` will keep that explicit value; `2.01` is the default for fresh/missing settings and can also be selected from the F12 menu.

## Multiplayer

The mod is designed as a client-side helper.

For pricing, it attempts to call the game's normal product-price update command path. It does not intentionally bypass host-side permissions or restrictions.

Checkout automation also uses the normal interaction/state paths and requires cashier permission. Commands already sent to the game/server cannot be recalled when a held action is cancelled.

Game updates can change internal Unity/PlayMaker objects, so live-game validation may occasionally be required even when the project and regression harnesses still pass.

## Compatibility and performance approach

The project intentionally does **not** compile directly against `Assembly-CSharp.dll`.

Runtime integration uses reflection and defensive resolution so the mod can tolerate some internal game changes. Version 0.7.4 also reduces repeated runtime discovery without caching live game values incorrectly:

- reflected fields/properties are resolved once per runtime type/member name, while their values are still read live;
- successful type resolution is cached and missing types are retried after five seconds or immediately when the loaded assembly count changes;
- the current live player/session objects are rebound instead of permanently caching stale network instances;
- shelf rows are resolved from live `productInfoArray` data;
- held checkout keeps a register-scoped product cache and performs scene-wide discovery only as a throttled fallback;
- checkout controls and transaction state are validated before submitting actions;
- product-price updates still go through the game's own update method where possible.

## Project structure

```text
.
├─ .github/workflows/
│  ├─ build.yml
│  └─ release.yml
├─ src/
│  ├─ Plugin.cs
│  ├─ Core/
│  │  ├─ GameBridge.cs
│  │  ├─ GameContext.cs
│  │  ├─ ReflectionMemberCache.cs
│  │  └─ TypeMissCache.cs
│  ├─ Features/
│  │  ├─ PriceGun/
│  │  │  ├─ PricingToolSelection.cs
│  │  │  └─ ShelfProductSelection.cs
│  │  └─ Checkout/
│  │     ├─ CheckoutBridge.cs
│  │     ├─ CheckoutHoldController.cs
│  │     ├─ CheckoutDiscoveryThrottle.cs
│  │     └─ Checkout*.cs
│  ├─ UI/
│  │  ├─ MenuSettings.cs
│  │  └─ RuntimeMenu.cs
│  └─ Tests/
├─ docs/
├─ SMT.NQOL.csproj
├─ build.ps1
├─ build.cmd
├─ LICENSE
└─ README.md
```

The source files keep the existing `SMT.PriceGun200` namespace and plugin GUID for configuration compatibility; the project, plugin display name and release artifact use the broader `SMT.NQOL` name.

## Building

Requirements:

- .NET SDK 8 or newer
- Internet access for the first NuGet restore

Build from the repository root:

```powershell
dotnet restore SMT.NQOL.csproj
dotnet build SMT.NQOL.csproj -c Release --no-restore
```

Or use:

```powershell
.\build.ps1
```

Output:

```text
bin\Release\netstandard2.1\SMT.NQOL.dll
```

The BepInEx dependency is pinned to the tested `5.4.21` package version so restores remain reproducible.

## Regression checks

The repository includes PowerShell source-level regression harnesses for the parts that can be validated without launching the game:

```powershell
pwsh -File src/Tests/pricing.ps1
pwsh -File src/Tests/optimization.ps1
pwsh -File src/Tests/reflection-cache.ps1
pwsh -File src/Tests/checkout-cache.ps1
pwsh -File src/Tests/structure.ps1
pwsh -File src/Tests/verify.ps1
pwsh -File src/Tests/reconnect.ps1
pwsh -File src/Tests/context.ps1
pwsh -File src/Tests/shelf-live.ps1
pwsh -File src/Tests/colliders.ps1
```

These cover pricing and cent normalization, reflection/type caching, checkout discovery throttling, source cleanup, scanner/shelf behavior, checkout state and amounts, reconnect/session replacement, cached game context behavior and checkout collider lifecycle.

They do not replace live-game testing for rendering, network timing, changed game prefabs or newly changed game internals.

## GitHub Actions

### Build

`.github/workflows/build.yml` runs for pull requests, pushes to `main`, and manual dispatch. It restores dependencies, builds Release, runs all regression harnesses and uploads `SMT.NQOL.dll` as an artifact. Feature branches use the pull-request run so the same commit is not built twice after a PR is open.

### Release

Push a version tag matching `v*`, for example:

```text
v0.7.4
```

`.github/workflows/release.yml` builds and tests the project, packages the DLL with `README.md` and `LICENSE`, and creates a GitHub Release with a ZIP attachment and generated release notes.

## Troubleshooting

If an action does nothing, enable verbose logging:

```ini
[Debug]
VerboseLogging = true
```

Then check:

```text
BepInEx\LogOutput.log
```

Useful things to verify first:

- the master feature and the specific Price Gun/Checkout feature are enabled;
- the configured hotkey is correct;
- the pricing scanner is equipped when `RequirePricingTool = true`;
- the player has cashier permission for checkout actions;
- the aimed object is an actual shelf row/label or checkout control;
- a recent game update has not changed the relevant internal objects.

## License

See [LICENSE](LICENSE).

This is an unofficial mod and is not affiliated with the Supermarket Together developers.
