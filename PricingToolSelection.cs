namespace SMT.PriceGun200;

internal static class PricingToolSelection
{
    // Verified against PlayerNetwork.ChangeEquipment in the installed game assembly.
    internal const int ScannerItemId = 2;

    internal static bool IsEquipped(bool isLocalPlayer, int equippedItem)
    {
        return isLocalPlayer && equippedItem == ScannerItemId;
    }
}
