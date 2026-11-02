using System;
using System.Globalization;

namespace SMT.PriceGun200;

internal static class CheckoutPayment
{
    internal static bool TryCashAdjustment(float expected, float current, out float adjustment)
    {
        adjustment = 0f;
        if (!IsValidAmount(expected) || !IsValidAmount(current))
            return false;
        adjustment = (float)Math.Round((double)expected - current, 2, MidpointRounding.AwayFromZero);
        return true;
    }

    internal static bool TryCardText(float total, out string text)
    {
        text = string.Empty;
        if (!IsValidAmount(total) || total <= 0f)
            return false;
        // PlayMaker ConvertStringToFloat uses float.Parse with the current culture.
        text = total.ToString("0.00", CultureInfo.CurrentCulture);
        return true;
    }

    private static bool IsValidAmount(float amount)
    {
        return !float.IsNaN(amount) && !float.IsInfinity(amount) && amount >= 0f;
    }
}

internal sealed class CheckoutConfirmationGuard
{
    private int _checkoutId;
    private int _customerId;
    private bool _pending;

    internal bool IsPending(int checkoutId, int customerId)
    {
        return _pending && _checkoutId == checkoutId && _customerId == customerId;
    }

    internal void MarkPending(int checkoutId, int customerId)
    {
        _checkoutId = checkoutId;
        _customerId = customerId;
        _pending = true;
    }

    internal void Observe(int checkoutId, int customerId, bool paymentActive)
    {
        if (!paymentActive || _checkoutId != checkoutId || _customerId != customerId)
            _pending = false;
    }
}

internal enum CheckoutControl
{
    None,
    OfferedCash,
    OfferedCard,
    CashControls,
    CardControls
}

internal static class CheckoutControls
{
    internal static CheckoutControl Classify(string path)
    {
        if (IsWithin(path, "Payments/Payment_Money")) return CheckoutControl.OfferedCash;
        if (IsWithin(path, "Payments/Payment_Card")) return CheckoutControl.OfferedCard;
        if (IsWithin(path, "CashRegister") || IsWithin(path, "CashRegisterCanvas")) return CheckoutControl.CashControls;
        if (IsWithin(path, "CreditCardCanvas")) return CheckoutControl.CardControls;
        return CheckoutControl.None;
    }

    private static bool IsWithin(string path, string root)
    {
        return path == root || path.StartsWith(root + "/", StringComparison.Ordinal);
    }
}
