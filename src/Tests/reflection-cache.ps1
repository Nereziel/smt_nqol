$ErrorActionPreference = 'Stop'

$memberPath = Join-Path $PSScriptRoot '../Core/ReflectionMemberCache.cs'
$typeMissPath = Join-Path $PSScriptRoot '../Core/TypeMissCache.cs'
if (-not (Test-Path $memberPath)) { throw 'Missing Core/ReflectionMemberCache.cs.' }
if (-not (Test-Path $typeMissPath)) { throw 'Missing Core/TypeMissCache.cs.' }

$memberSource = Get-Content $memberPath -Raw
$typeMissSource = Get-Content $typeMissPath -Raw
if ($memberSource -notmatch 'Dictionary<\s*Type\s*,\s*Dictionary<\s*string\s*,\s*MemberInfo\?\s*>\s*>') {
    throw 'Reflection member lookups must be cached per runtime Type.'
}
if ($memberSource -notmatch 'StringComparer\.OrdinalIgnoreCase') {
    throw 'Reflection member cache must preserve case-insensitive lookup semantics.'
}

$harness = @"
namespace CleanupCacheHarness {
    using System;
    using SMT.PriceGun200;

    internal sealed class CacheProbe {
        public int Value { get; } = 42;
        private string Hidden = "field";
        public int Throwing => throw new InvalidOperationException("expected");
        public static string StaticName { get; } = "static";
    }

    public static class ReflectionCacheChecks {
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

        public static void Run() {
            var probe = new CacheProbe();
            Check(ReflectionMemberCache.TryGetInstanceValue(probe, "value", out var value) && value is int n && n == 42,
                "Case-insensitive property lookup failed");
            Check(ReflectionMemberCache.TryGetInstanceValue(probe, "HIDDEN", out var hidden) && (string)hidden! == "field",
                "Private field fallback failed");
            Check(!ReflectionMemberCache.TryGetInstanceValue(probe, "missing", out _), "Missing member was accepted");
            Check(!ReflectionMemberCache.TryGetInstanceValue(probe, "Throwing", out _), "Throwing getter escaped cache helper");
            Check(ReflectionMemberCache.TryGetStaticValue(typeof(CacheProbe), "staticname", out var staticValue) && (string)staticValue! == "static",
                "Static property lookup failed");

            var misses = new TypeMissCache();
            long second = TimeSpan.TicksPerSecond;
            Check(!misses.ShouldSkip("MissingType", 10, 100 * second), "Fresh type miss was skipped");
            misses.RecordMiss("MissingType", 10, 100 * second);
            Check(misses.ShouldSkip("MissingType", 10, 104 * second), "Type miss was not throttled within five seconds");
            Check(!misses.ShouldSkip("MissingType", 10, 105 * second), "Type miss did not expire after five seconds");
            misses.RecordMiss("MissingType", 10, 110 * second);
            Check(!misses.ShouldSkip("MissingType", 11, 111 * second), "Assembly-count change did not invalidate type miss");
            misses.Clear("MissingType");
            Check(!misses.ShouldSkip("MissingType", 11, 111 * second), "Cleared type miss remained cached");
        }
    }
}
"@

Add-Type -TypeDefinition ("#nullable enable`n" + $memberSource + "`n" + $typeMissSource + "`n" + $harness)
[CleanupCacheHarness.ReflectionCacheChecks]::Run()
Write-Output 'PASS: reflection metadata caching and five-second type-miss throttling preserve lookup behavior.'
