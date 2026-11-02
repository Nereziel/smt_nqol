namespace SMT.PriceGun200;

internal static class ShelfProductSelection
{
    internal static bool TryRead(int[]? data, int row, out int productId)
    {
        productId = -1;
        if (data == null || row < 0 || row >= data.Length / 2)
            return false;
        var candidate = data[row * 2];
        if (candidate < 0)
            return false;
        productId = candidate;
        return true;
    }
}
