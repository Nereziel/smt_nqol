# SMT Price Gun 200%

**Version 0.7.2**

Small BepInEx 5 client-side mod for **Supermarket Together**.

## What it does

- Aim at a product with the pricing scanner / price gun.
- Press **middle mouse button** by default.
- The mod resolves the product's current raw market price.
- It sets the selling price to **200% of market price** by default.
- Price is rounded **down** to cents by default.
- It calls the game's own product-price update method instead of directly editing a local price field, so multiplayer synchronization can use the normal game path.

Default calculation:

```text
sellingPrice = floor((rawMarketPrice * 2.00) / 0.01) * 0.01
```

## Install

1. Install **BepInEx 5 x64** for Supermarket Together.
2. Build the project (see below), or use a compiled `SMT.PriceGun200.dll`.
3. Copy `SMT.PriceGun200.dll` to:

```text
Supermarket Together\BepInEx\plugins\SMT.PriceGun200\
```

4. Start the game.

## Controls

Default:

```text
Middle mouse button = set aimed product to 200% market price
```

The hotkey and multiplier can be changed in:

```text
BepInEx\config\cz.milan.supermarkettogether.pricegun200.cfg
```

Example:

```ini
[Pricing]
MarketPriceMultiplier = 2
RoundDownStep = 0.01

[Scanner]
Hotkey = Mouse2
MaxDistance = 6
RequirePricingTool = true
```

### Suggested values

- `2.00` = 200% market price.
- `2.01` = 201% market price.
- `RoundDownStep = 0.01` = round down to cents.
- `RoundDownStep = 0.05` = round down to nearest 5 cents.

`RequirePricingTool = true` requires the local player to hold the pricing scanner. The gate reads `PlayerNetwork.isLocalPlayer` and `equippedItem == 2`; object names and camera parenting are irrelevant. Set false only to explicitly allow use without the scanner.

## Multiplayer

The mod is designed as a **client-side pricing helper**. It tries to call the same `ProductListing` product-price update/command path used by the game instead of modifying a local-only field.

That means the host should not need this mod **if the current game build still permits normal clients to update prices**. If a host-side permissions mod blocks price changes, this mod intentionally does not bypass it.

## Build

Requirements:

- .NET SDK 8 or newer (the project itself targets `netstandard2.1`).
- Internet access for the first NuGet restore.

From this folder:

```powershell
dotnet restore
dotnet build -c Release
```

Output:

```text
bin\Release\netstandard2.1\SMT.PriceGun200.dll
```

Or run:

```powershell
.\build.ps1
```

## Compatibility strategy

The mod intentionally does **not** compile against `Assembly-CSharp.dll`.

At runtime it uses reflection and attempts several paths:

1. Find `ProductListing.Instance`.
2. Resolve product ID from the object under the crosshair.
3. Fall back to active pricing/scanner components if necessary.
4. Look for a native market-price method.
5. Fall back to `productsData` (newer builds) or `productPrefabs` (older builds).
6. Prefer `CmdUpdateProductPrice(int, float)` and then other matching product-price update methods.

This makes it more tolerant of internal game updates than a mod hardcoded to one exact `Assembly-CSharp.dll` layout.

## Troubleshooting

If clicking does nothing:

1. Open the generated config file.
2. Set:

```ini
[Debug]
VerboseLogging = true
```

3. Restart the game.
4. Aim at a product and press middle mouse.
5. Check:

```text
BepInEx\LogOutput.log
```

The log will show the hit object, nearby components, product/item/price/target members, product ID resolver, market price path, and price update method.

## 0.2.0 changes

- Supports product keys stored as enums such as `productType` / `itemType`.
- Searches the hit object, parents, nearby colliders and shelf/label sibling hierarchy.
- Supports game pricing methods whose product parameter is an enum instead of an integer.
- Expanded verbose diagnostics for future game updates.

## Notes

This is an unofficial mod and is not affiliated with the Supermarket Together developers.


## Changes in 0.3.0

- Resolve shelf products and labels through their exact row in `Data_Container.productInfoArray`.
- Do not resolve the aimed product from catalogues or global pricing components.
- Regression checks: `powershell -File Tests/verify.ps1`.


## Changes in 0.3.1

- Verify the local player holds the pricing scanner using `PlayerNetwork.equippedItem == 2` instead of camera object names.
- New configurations require the scanner by default. Existing configuration values are preserved; keep `RequirePricingTool = true`.


## Checkout in 0.4.0

Open a checkout and accept the cash or card first, then **right-click** to confirm payment. Cash fills the exact change, correcting any previous input; card enters and confirms the checkout total. The action uses the checkout aimed at by the local camera and the normal interaction FSMs, respects cashier permissions, and blocks duplicate submissions while awaiting the server. No checkout action runs outside an open payment.

```ini
[Checkout]
Enabled = true
ConfirmHotkey = Mouse1
```

Middle-click scanner pricing remains separate. Validation: Release build and `Tests/verify.ps1`; live-game confirmation is still required.


## Fix in 0.4.1

- Resolve the aimed checkout/payment terminal within 4 metres without requiring a camera event.
- An active payment takes priority if scanner and checkout shortcuts share a key.
- Debug reports the checkout shortcut press and rejection reason. Existing shortcuts are preserved: `Mouse1` means right-click, `Mouse2` middle-click.


## Checkout controls in 0.5.0

Aim at the offered card/money, or an actual cash-change or card-terminal control. Press `Checkout.ConfirmHotkey` (default `Mouse1`; existing bindings remain unchanged). The normal payment acceptance FSM runs first if needed. After the server activates the matching panel, the mod fills and confirms the amount. Already-open payment panels go directly to filling and confirmation.

Supported control hierarchies: `Payments/Payment_Money`, `Payments/Payment_Card`, `CashRegister`, `CashRegisterCanvas`, `CreditCardCanvas`, including child colliders. Unrelated shelf/sign controls are ignored. Duplicate clicks are blocked while the same transaction awaits the server.

Validation: build and regression checks passed; game assembly methods and prefab colliders inspected. Live-game payment validation remains required.


## Reconnect in 0.5.1

Each price action resolves a live, spawned client `ProductListing`. Destroyed/inactive/disconnected instances are rejected; replacing the instance clears cached pricing methods. Local player equipment already resolves the current player on each click. The price log reports a request rather than claiming server confirmation.

Run `Tests/reconnect.ps1` for production-resolver regression checks with simulated Unity destroyed-object semantics. Live multiplayer reconnect validation remains required.


## Hold-to-scan in 0.6.0

Aim at a checkout or one of its belt products and hold `Checkout.ConfirmHotkey` for 0.3 seconds. The mod scans items for that register one at a time through the normal interaction FSM, waiting for the server to remove each submitted item. Once scanning completes, it waits for the NPC to offer cash or card, then accepts that payment method and fills/confirms the correct amount.

Releasing stops further local actions; commands already sent cannot be recalled. Automation also stops on focus loss, local-player replacement, moving more than 4 metres from the register, or losing cashier permission. NPC-staffed registers are excluded. Short-click payment confirmation remains available.

```ini
[Checkout]
HoldToScan = true
ScanInterval = 0.15
```

Existing hotkeys remain unchanged. Scanner pricing is independent and limited to shelf products/labels. Validation includes the Release build, payment/price checks, serial scan gate and simulated reconnect. Live-game hold scanning and network payment completion remain unverified.


## Payment delays in 0.6.1

```ini
[Checkout]
AfterPickupDelay = 1
ConfirmDelay = 1
```

After accepted cash/card activates its panel, wait `AfterPickupDelay` seconds before filling the amount, then wait another `ConfirmDelay` seconds before final confirmation. Both cash/card and click/hold use these delays. An already-open panel starts the first delay at the click. Both values support 0–60 seconds; 0 disables the delay. Releasing a held key cancels further local steps during either wait; sent network commands cannot be recalled.


## F12 settings and performance in 0.7.0

**F12** opens/closes settings, including when the mod is disabled. **Esc** closes or cancels key capture. Opening settings stops automation, unlocks the cursor and blocks gameplay through the existing options flag. The previous cursor/options state is restored on close; release held shortcuts before resuming.

Settings cover master/scanner/checkout/hold/debug toggles, scanner requirement, both bindings, price multiplier, rounding, distance, scan interval and both payment delays. Numeric input accepts comma or dot decimals. **Apply and save** applies changes and writes the config once. **Reload config** reloads the settings file and replaces draft edits; it does not reload the DLL. Closing discards unapplied drafts.

Payment delays no longer scan all assemblies and all MonoBehaviours every frame. Successful type lookups are cached. Local player/permission components are tied to the current FirstPersonController.Instance and rebound after reconnect, with Unity destroyed-object checks. Replaced PlayMaker global variables are rebound too. Belt searches stop while a payment is pending; payment polling is limited to 20 Hz.

Validation: Release build, Tests/verify.ps1, Tests/reconnect.ps1 and Tests/context.ps1. Context tests execute 1000 warm cycles without rediscovering types/components/variables and check reconnect and live permissions. Actual F12 rendering/input and live-game frame-time improvement remain unverified.


## Scanner targeting and opaque UI in 0.7.1

Scanner raycasts use PlayerNetwork.interactableMask, matching the game scanner. InteractableContainer identifies actual shelf rows even under nonstandard parent names. Each click reads live productInfoArray, including restocked/replaced products and replaced arrays. Failed clicks identify the hit collider, with row details in debug.

The F12 window uses a fully opaque custom background and UTF-8 source text. Tests/shelf-live.ps1 checks the production resolver against live product changes without reconnect. Newly purchased stock in-game and actual menu rendering remain to be validated.


## Checkout collider repair in 0.7.2

The game prefab 7_CheckoutB has negative scale and some negative BoxCollider sizes. Checkout.FixNegativeColliders=true replaces affected checkout boxes with equivalent convex box meshes on the same GameObject. Center, absolute extents, trigger, material, contact offset and enabled state are preserved; transforms and rendering stay intact. Original boxes remain disabled for game references.

Enabled by default and available in F12. Disabling restores originals; repair also handles new checkout instances and releases its own meshes on cleanup. Discovery runs every two seconds. Initial warnings during object creation may still precede runtime repair.

Tests/colliders.ps1 checks production geometry/lifecycle using simulated Unity. Actual physics and manual checkout interaction still need live-game validation.
