namespace SMT.PriceGun200
{
    using System;
    using System.Collections.Generic;

    internal sealed class TypeMissCache
    {
        private const long RetryTicks = 5 * TimeSpan.TicksPerSecond;
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

        private readonly struct Entry
        {
            internal Entry(int assemblyCount, long recordedTicks)
            {
                AssemblyCount = assemblyCount;
                RecordedTicks = recordedTicks;
            }

            internal int AssemblyCount { get; }
            internal long RecordedTicks { get; }
        }

        internal bool ShouldSkip(string name, int assemblyCount, long nowTicks)
        {
            if (!_entries.TryGetValue(name, out var entry))
                return false;
            if (entry.AssemblyCount != assemblyCount || nowTicks < entry.RecordedTicks ||
                nowTicks - entry.RecordedTicks >= RetryTicks)
            {
                _entries.Remove(name);
                return false;
            }
            return true;
        }

        internal void RecordMiss(string name, int assemblyCount, long nowTicks)
        {
            _entries[name] = new Entry(assemblyCount, nowTicks);
        }

        internal void Clear(string name)
        {
            _entries.Remove(name);
        }
    }
}
