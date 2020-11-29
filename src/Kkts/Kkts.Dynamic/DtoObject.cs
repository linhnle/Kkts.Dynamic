using Kkts.Dynamic.Internal;

namespace Kkts.Dynamic
{
    public static class DtoObject
    {
        public static object[] GetIds(this object obj)
        {
            if (obj is null) return new object[0];

            var methodInfo = obj.GetType().GetMethod(Class.GetIdsMethodName);
            if (methodInfo is null) return new object[0];
            var result = methodInfo.Invoke(obj, new object[0]);

            return result is object[] arr ? arr : new object[0];
        }
    }
}
