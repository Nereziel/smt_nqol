$ErrorActionPreference = 'Stop'
$source = Get-Content (Join-Path $PSScriptRoot '../Features/PriceGun/ShelfProductSelection.cs') -Raw
$source = $source.Replace('namespace SMT.PriceGun200;', 'namespace SMT.PriceGun200 {') + "`n}"
$toolSource = Get-Content (Join-Path $PSScriptRoot '../Features/PriceGun/PricingToolSelection.cs') -Raw
$toolSource = $toolSource.Replace('namespace SMT.PriceGun200;', 'namespace SMT.PriceGun200 {') + "`n}"
$source += "`n" + $toolSource
$checkoutSource = Get-Content (Join-Path $PSScriptRoot '../Features/Checkout/CheckoutPayment.cs') -Raw
$checkoutSource = $checkoutSource.Replace('using System;', '').Replace('using System.Globalization;', '')
$checkoutSource = $checkoutSource.Replace('namespace SMT.PriceGun200;', 'namespace SMT.PriceGun200 { using System; using System.Globalization;') + "`n}"
$source += "`n" + $checkoutSource
$gateSource = Get-Content (Join-Path $PSScriptRoot '../Features/Checkout/CheckoutScanGate.cs') -Raw
$gateSource = $gateSource.Replace('namespace SMT.PriceGun200;', 'namespace SMT.PriceGun200 {') + "`n}"
$source += "`n" + $gateSource
$delaySource = Get-Content (Join-Path $PSScriptRoot '../Features/Checkout/CheckoutDelay.cs') -Raw
$delaySource = $delaySource.Replace('using System;', '')
$delaySource = $delaySource.Replace('namespace SMT.PriceGun200;', 'namespace SMT.PriceGun200 { using System;') + "`n}"
$source += "`n" + $delaySource
$menuSource = Get-Content (Join-Path $PSScriptRoot '../UI/MenuSettings.cs') -Raw
$menuSource = $menuSource.Replace('using System;', '').Replace('using System.Globalization;', '')
$menuSource = $menuSource.Replace('namespace SMT.PriceGun200;', 'namespace SMT.PriceGun200 { using System; using System.Globalization;') + "`n}"
$source += "`n" + $menuSource
$checks = @"
namespace SMT.PriceGun200 {
    public static class SelectionChecks {
        public static void Run() {
            for (int item = 0; item <= 17; item++) {
                if (PricingToolSelection.IsEquipped(true, item) != (item == 2))
                    throw new System.Exception("Wrong local equipment gate: " + item);
            }
            if (PricingToolSelection.IsEquipped(false, 2)) throw new System.Exception("Remote scanner accepted");
            if (CheckoutControls.Classify("Payments/Payment_Money") != CheckoutControl.OfferedCash) throw new System.Exception("Offered cash not recognized");
            if (CheckoutControls.Classify("Payments/Payment_Card/Mesh") != CheckoutControl.OfferedCard) throw new System.Exception("Card child collider not recognized");
            if (CheckoutControls.Classify("CashRegister/MoneyGivenBack") != CheckoutControl.CashControls) throw new System.Exception("Change control not recognized");
            if (CheckoutControls.Classify("CashRegisterCanvas/Container/CurrentAmount") != CheckoutControl.CashControls) throw new System.Exception("Cash display not recognized");
            if (CheckoutControls.Classify("CreditCardCanvas/Container/CreditCardContainer/ButtonNumber_Accept") != CheckoutControl.CardControls) throw new System.Exception("Card accept control not recognized");
            foreach (string path in new string[] { "", "CheckoutLaneSign", "ProductContainer/0", "Payments/Payment_Cardboard", "OtherCheckout/CashRegister" }) {
                if (CheckoutControls.Classify(path) != CheckoutControl.None) throw new System.Exception("Unrelated checkout control accepted: " + path);
            }
            var scanGate = new CheckoutScanGate();
            if (!scanGate.CanSubmit || scanGate.TrySubmit(0)) throw new System.Exception("Invalid initial scan gate");
            if (!scanGate.TrySubmit(123)) throw new System.Exception("Initial scan rejected");
            if (scanGate.TrySubmit(123) || scanGate.TrySubmit(456)) throw new System.Exception("Duplicate or concurrent scan submitted");
            scanGate.Observe(123, true);
            if (scanGate.CanSubmit) throw new System.Exception("Scan released before server removal");
            scanGate.Observe(456, false);
            if (scanGate.CanSubmit) throw new System.Exception("Unrelated item removal released scan");
            scanGate.Observe(123, true);
            if (scanGate.TrySubmit(123)) throw new System.Exception("Release/repress resent an in-flight item");
            scanGate.Observe(123, false);
            if (!scanGate.CanSubmit || !scanGate.TrySubmit(456)) throw new System.Exception("Next item blocked after server removal");
            scanGate.Observe(456, false);
            if (!scanGate.TrySubmit(-25)) throw new System.Exception("Valid negative Unity instance ID rejected");
            var pickupDelay = new CheckoutDelay();
            var confirmDelay = new CheckoutDelay();
            if (pickupDelay.IsReady(100f)) throw new System.Exception("Unstarted delay ready");
            pickupDelay.Start(10f, 1f);
            if (pickupDelay.IsReady(10.99f) || !pickupDelay.IsReady(11f)) throw new System.Exception("Pickup second not respected");
            confirmDelay.Start(11f, 1f);
            if (confirmDelay.IsReady(11.99f) || !confirmDelay.IsReady(12f)) throw new System.Exception("Independent confirmation second not respected");
            if (confirmDelay.HasTimedOut(12f, 1f) || !confirmDelay.HasTimedOut(13.01f, 1f)) throw new System.Exception("Validation timeout swallowed configured delay");
            pickupDelay.Start(0f, 30f);
            if (pickupDelay.IsReady(29f) || !pickupDelay.IsReady(30f)) throw new System.Exception("Long configured delay ignored");
            pickupDelay.Start(20f, 0f);
            if (!pickupDelay.IsReady(20f)) throw new System.Exception("Zero delay not supported");
            pickupDelay.Clear();
            if (pickupDelay.IsReady(999f)) throw new System.Exception("Cancelled delay still ready");
            pickupDelay.Start(30f, 2f);
            if (pickupDelay.IsReady(31f) || !pickupDelay.IsReady(32f)) throw new System.Exception("New customer reused old deadline");
            if (CheckoutDelay.Normalize(-5f) != 0f || CheckoutDelay.Normalize(70f) != 60f || CheckoutDelay.Normalize(float.NaN) != 1f || CheckoutDelay.Normalize(float.PositiveInfinity) != 1f) throw new System.Exception("Invalid configured delay not normalized");
            float menuValue;
            if (!MenuSettings.TryNumber("1,25", 0f, 60f, out menuValue) || menuValue != 1.25f) throw new System.Exception("Czech settings decimal rejected");
            if (!MenuSettings.TryNumber(" 0.15 ", 0.05f, 2f, out menuValue) || menuValue != 0.15f) throw new System.Exception("English settings decimal rejected");
            foreach (string invalid in new string[] { "", "NaN", "Infinity", "-1", "61", "oops", "1,2,3" }) {
                if (MenuSettings.TryNumber(invalid, 0f, 60f, out menuValue)) throw new System.Exception("Invalid setting accepted: " + invalid);
            }
            if (MenuSettings.TryNumber("0", 0.05f, 2f, out menuValue)) throw new System.Exception("Zero scan interval accepted");
            float delta;
            if (!CheckoutPayment.TryCashAdjustment(12.35f, 2f, out delta) || System.Math.Abs(delta - 10.35f) > 0.0001f) throw new System.Exception("Cash remainder incorrect");
            if (!CheckoutPayment.TryCashAdjustment(5f, 7f, out delta) || delta != -2f) throw new System.Exception("Overpaid change not corrected");
            if (!CheckoutPayment.TryCashAdjustment(0f, 0f, out delta) || delta != 0f) throw new System.Exception("Exact payment rejected");
            if (!CheckoutPayment.TryCashAdjustment(0.3f, 0.1f, out delta) || delta != 0.2f) throw new System.Exception("Cent rounding incorrect");
            foreach (float invalid in new float[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity }) {
                if (CheckoutPayment.TryCashAdjustment(invalid, 0f, out delta) || CheckoutPayment.TryCashAdjustment(1f, invalid, out delta)) throw new System.Exception("Invalid cash amount accepted");
                string invalidText;
                if (CheckoutPayment.TryCardText(invalid, out invalidText)) throw new System.Exception("Invalid card total accepted");
            }
            string cardText;
            if (CheckoutPayment.TryCardText(0f, out cardText)) throw new System.Exception("Empty card transaction accepted");
            var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
            try {
                foreach (string culture in new string[] { "cs-CZ", "en-US" }) {
                    System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
                    if (!CheckoutPayment.TryCardText(23.45f, out cardText) || float.Parse(cardText) != 23.45f) throw new System.Exception("Card parsing failed: " + culture);
                }
            } finally { System.Globalization.CultureInfo.CurrentCulture = originalCulture; }
            var guard = new CheckoutConfirmationGuard();
            if (guard.IsPending(10, 20)) throw new System.Exception("Fresh transaction blocked");
            guard.MarkPending(10, 20);
            guard.Observe(10, 20, true);
            if (!guard.IsPending(10, 20)) throw new System.Exception("Duplicate confirmation allowed while awaiting server");
            guard.Observe(10, 20, false);
            if (guard.IsPending(10, 20)) throw new System.Exception("Next payment blocked after panel reset");
            guard.MarkPending(10, 20);
            guard.Observe(10, 21, true);
            if (guard.IsPending(10, 20) || guard.IsPending(10, 21)) throw new System.Exception("Next customer blocked");
            guard.MarkPending(10, 20);
            guard.Observe(11, 20, true);
            if (guard.IsPending(11, 20)) throw new System.Exception("Different register blocked");
            int id;
            int[] rows = { 0, 12, 57, 3, 9, 0, -1, 0 };
            if (!ShelfProductSelection.TryRead(rows, 0, out id) || id != 0) throw new System.Exception("Product zero rejected");
            if (!ShelfProductSelection.TryRead(rows, 1, out id) || id != 57) throw new System.Exception("Second row resolved incorrectly");
            if (!ShelfProductSelection.TryRead(rows, 2, out id) || id != 9) throw new System.Exception("Empty stocked row lost its assigned product");
            if (ShelfProductSelection.TryRead(rows, 3, out id) || id != -1) throw new System.Exception("Unassigned row accepted");
            if (ShelfProductSelection.TryRead(rows, -1, out id)) throw new System.Exception("Missing row accepted");
            if (ShelfProductSelection.TryRead(rows, 4, out id)) throw new System.Exception("Out of range row accepted");
            if (ShelfProductSelection.TryRead(null, 0, out id)) throw new System.Exception("Null data accepted");
            if (ShelfProductSelection.TryRead(new int[] { 7 }, 0, out id)) throw new System.Exception("Incomplete pair accepted");
            if (ShelfProductSelection.TryRead(rows, int.MaxValue, out id)) throw new System.Exception("Overflow row accepted");
        }
    }
}
"@
Add-Type -TypeDefinition ("#nullable enable`n" + $source + "`n" + $checks)
[SMT.PriceGun200.SelectionChecks]::Run()
Write-Output 'PASS: shelf selection, scanner gate, checkout controls, amounts, cultures, duplicate confirmations serial belt scan gate and two-stage payment delays and menu numeric settings.'
