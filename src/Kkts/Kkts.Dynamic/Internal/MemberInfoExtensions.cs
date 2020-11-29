using System;
using System.Reflection;

namespace Kkts.Dynamic.Internal
{
	internal static class MemberInfoExtensions
	{
		public static Type GetMemberType(this MemberInfo memberInfo)
		{
			if (memberInfo == null) return null;

			switch (memberInfo)
			{
				case FieldInfo field:
					return field.FieldType;
				case PropertyInfo prop:
					return prop.PropertyType;
			}

			return null;
		}

		public static bool IsPrimitive(this MemberInfo memberInfo)
		{
			var type = memberInfo.GetMemberType();
			var conversionType = Nullable.GetUnderlyingType(type) ?? type;
			return conversionType == typeof(string)
				|| conversionType.IsPrimitive
				|| conversionType.IsEnum
				|| conversionType == typeof(DateTime)
				|| conversionType == typeof(DateTimeOffset)
				|| conversionType == typeof(char[])
				|| conversionType == typeof(byte[])
				|| conversionType == typeof(Guid);
		}
	}
}
