using System;

namespace SMT.PriceGun200;

internal sealed class CheckoutDelay
{
    private bool _started;
    private float _readyAt;

    internal void Start(float now, float seconds)
    {
        _readyAt = now + Normalize(seconds);
        _started = true;
    }

    internal bool IsReady(float now) => _started && now >= _readyAt;
    internal bool HasTimedOut(float now, float grace) => _started && now > _readyAt + grace;
    internal void Clear() => _started = false;

    internal static float Normalize(float seconds)
    {
        return float.IsNaN(seconds) || float.IsInfinity(seconds) ? 1f : Math.Max(0f, Math.Min(seconds, 60f));
    }
}
