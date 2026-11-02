namespace SMT.PriceGun200;

internal sealed class CheckoutScanGate
{
    internal int PendingItemId { get; private set; }
    internal bool CanSubmit => PendingItemId == 0;

    internal bool TrySubmit(int itemId)
    {
        if (!CanSubmit || itemId == 0)
            return false;
        PendingItemId = itemId;
        return true;
    }

    internal void Observe(int itemId, bool stillPresent)
    {
        if (PendingItemId == itemId && !stillPresent)
            PendingItemId = 0;
    }
}
