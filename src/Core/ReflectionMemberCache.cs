namespace SMT.PriceGun200
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;

    internal static class ReflectionMemberCache
    {
        private static readonly Dictionary<Type, Dictionary<string, MemberInfo?>> InstanceMembers = new();
        private static readonly Dictionary<Type, Dictionary<string, MemberInfo?>> StaticMembers = new();
        private static readonly object Sync = new();

        internal static bool TryGetInstanceValue(object owner, string name, out object? value)
        {
            value = null;
            var member = GetMember(owner.GetType(), name, false);
            if (member == null)
                return false;
            try
            {
                if (member is PropertyInfo property)
                {
                    value = property.GetValue(owner, null);
                    return true;
                }
                if (member is FieldInfo field)
                {
                    value = field.GetValue(owner);
                    return true;
                }
            }
            catch
            {
                // Game properties may throw outside their intended state.
            }
            return false;
        }

        internal static bool TryGetStaticValue(Type type, string name, out object? value)
        {
            value = null;
            var member = GetMember(type, name, true);
            if (member == null)
                return false;
            try
            {
                if (member is PropertyInfo property)
                {
                    value = property.GetValue(null, null);
                    return true;
                }
                if (member is FieldInfo field)
                {
                    value = field.GetValue(null);
                    return true;
                }
            }
            catch
            {
                // Game properties may throw outside their intended state.
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

                var flags = (isStatic ? BindingFlags.Static : BindingFlags.Instance) |
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
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
    }
}
