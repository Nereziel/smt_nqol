namespace SMT.PriceGun200
{
    internal sealed class CheckoutDiscoveryThrottle
    {
        private const float RetrySeconds = 0.5f;
        private bool _started;
        private float _nextDiscovery;

        internal bool ShouldDiscover(float now)
        {
            if (_started && now < _nextDiscovery)
                return false;
            _started = true;
            _nextDiscovery = now + RetrySeconds;
            return true;
        }

        internal void Reset()
        {
            _started = false;
            _nextDiscovery = 0f;
        }
    }
}
