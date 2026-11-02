using System;
using System.Globalization;

namespace SMT.PriceGun200;

internal static class MenuSettings
{
    internal static bool TryNumber(string text, float min, float max, out float value)
    {
        return float.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
    }
}
