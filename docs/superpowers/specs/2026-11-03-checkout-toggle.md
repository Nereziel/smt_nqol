# SMT NQOL 0.7.5 Checkout Toggle Design

## Goal

Replace hold-to-run checkout automation with a contextual toggle on the existing checkout hotkey.

## Behavior

- The existing checkout hotkey remains shared with manual payment confirmation.
- On key press, manual payment confirmation has first priority.
- If no manual payment action is handled and the player is aiming at a valid checkout/belt context, the same press toggles automatic checkout ON.
- While ON, the player does not need to hold the key.
- A later press while aiming at the active checkout/belt toggles automation OFF.
- Automation stops automatically when the game loses focus, the local player/session changes, the player moves more than 4 m from the checkout, the checkout becomes invalid/unavailable, cashier permission is lost, or an NPC cashier occupies it.
- Opening the F12 menu, disabling the mod/checkout feature, reloading config, disabling the plugin, or destroying it stops automation.
- Manual payment handling must not accidentally toggle automation.
- Existing scanner behavior and the 201% price default are unchanged.

## Configuration

The old `Checkout.HoldToScan` option is replaced by `Checkout.ToggleAutomation`, default `true`. The F12 menu wording is updated accordingly. A stale `HoldToScan` entry in an existing config file is harmless and no longer read.

## Version

Plugin/project version becomes `0.7.5`.
