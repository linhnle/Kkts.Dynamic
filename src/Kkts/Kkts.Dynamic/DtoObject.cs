namespace Kkts.Dynamic
{
    public static class DtoObject
    {
        public static object[] GetIds(this object obj)
        {
            if (obj is null) return System.Array.Empty<object>();
            return Mapper.GetIds(obj);
        }
    }
}
