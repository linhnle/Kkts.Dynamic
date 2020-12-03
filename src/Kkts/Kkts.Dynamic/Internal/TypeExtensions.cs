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
    }
}
