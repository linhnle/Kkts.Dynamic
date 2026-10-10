using System;
using System.Reflection;

namespace Kkts.Dynamic.Internal
{
    internal static class TypeExtensions
    {
        internal static readonly BindingFlags BindingFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

        public static MemberInfo GetMemberInfo(this Type type, string name)
        {
            return (MemberInfo)type.GetProperty(name, BindingFlags) ?? type.GetField(name, BindingFlags);
        }

        internal static ConstructorInfo GetRequiredConstructor(this Type type)
        {
            var constructor = type.GetConstructor(Type.EmptyTypes);
            if (!type.IsClass || type.IsAbstract || !type.IsVisible || constructor == null)
                throw new InvalidOperationException($"Type '{type.FullName}' must be a visible, non-abstract class with a public parameterless constructor for reverse mapping.");
            return constructor;
        }
    }
}
