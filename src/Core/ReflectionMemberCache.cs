namespace SMT.PriceGun200
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;

    internal static class ReflectionMemberCache
    {
        private static readonly Dictionary<Type, Dictionary<string, MemberInfo?>> InstanceMembers = new();
        private static readonly Dictionary<Type, Dictionary<string, MemberInfo?>> StaticMembers = new();
        private static readonly Dictionary<Type, Dictionary<string, FieldInfo?>> InstanceFallbackFields = new();
        private static readonly Dictionary<Type, Dictionary<string, FieldInfo?>> StaticFallbackFields = new();
        private static readonly object Sync = new();

        internal static bool TryGetInstanceValue(object owner, string name, out object? value)
        {
            return TryGetValue(owner, owner.GetType(), name, false, out value);
        }

        internal static bool TryGetStaticValue(Type type, string name, out object? value)
        {
            return TryGetValue(null, type, name, true, out value);
        }

        private static bool TryGetValue(object? owner, Type type, string name, bool isStatic, out object? value)
        {
            value = null;
            var member = GetMember(type, name, isStatic);
            if (member == null)
                return false;

            if (member is PropertyInfo property)
            {
                try
                {
                    value = property.GetValue(owner, null);
                    return true;
                }
                catch
                {
                    // Preserve the original helper's behavior: a throwing property may hide
                    // an inherited field with the same name, so try that cached fallback too.
                    var fallback = GetFallbackField(type, name, isStatic);
                    if (fallback == null)
                        return false;
                    try
                    {
                        value = fallback.GetValue(owner);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }

            if (member is FieldInfo field)
            {
                try
                {
                    value = field.GetValue(owner);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private static MemberInfo? GetMember(Type type, string name, bool isStatic)
        {
            var cache = isStatic ? StaticMembers : InstanceMembers;
            lock (Sync)
            {
                if (!cache.TryGetValue(type, out var members))
                {
                    members = new Dictionary<string, MemberInfo?>(StringComparer.OrdinalIgnoreCase);
                    cache[type] = members;
                }
                if (members.TryGetValue(name, out var cached))
                    return cached;

                var flags = Flags(isStatic);
                MemberInfo? resolved = null;
                var property = type.GetProperty(name, flags);
                if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
                    resolved = property;
                else
                    resolved = type.GetField(name, flags);
                members[name] = resolved;
                return resolved;
            }
        }

        private static FieldInfo? GetFallbackField(Type type, string name, bool isStatic)
        {
            var cache = isStatic ? StaticFallbackFields : InstanceFallbackFields;
            lock (Sync)
            {
                if (!cache.TryGetValue(type, out var fields))
                {
                    fields = new Dictionary<string, FieldInfo?>(StringComparer.OrdinalIgnoreCase);
                    cache[type] = fields;
                }
                if (fields.TryGetValue(name, out var cached))
                    return cached;

                var field = type.GetField(name, Flags(isStatic));
                fields[name] = field;
                return field;
            }
        }

        private static BindingFlags Flags(bool isStatic)
        {
            return (isStatic ? BindingFlags.Static : BindingFlags.Instance) |
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
        }
    }
}
