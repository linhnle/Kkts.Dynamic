using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;

namespace Kkts.Dynamic
{
    public static class Mapper
    {
        internal static MethodInfo MapFromEntityToDtoMethodInfo = typeof(Mapper).GetMethod(nameof(MapFromEntityToDto));
        internal static MethodInfo MapFromDtoToEntityMethodInfo = typeof(Mapper).GetMethod(nameof(MapFromDtoToEntity));
        internal static MethodInfo ToDtoArrayMethodInfo = typeof(Mapper).GetMethod(nameof(ToDtoArray));
        internal static MethodInfo ToEntityArrayMethodInfo = typeof(Mapper).GetMethod(nameof(ToEntityArray));
        internal static MethodInfo ToDtoCollectionMethodInfo = typeof(Mapper).GetMethod(nameof(ToDtoCollection));
        internal static MethodInfo ToEntityCollectionMethodInfo = typeof(Mapper).GetMethod(nameof(ToEntityCollection));
        internal static MethodInfo GetTypeFromHandleMethodInfo = typeof(Type).GetMethod("GetTypeFromHandle");

        public static void MapFromDtoToEntity(object dto, object entity)
        {
            if (dto is null || entity is null) return;

            var methodInfo = dto.GetType().GetMethod(Class.InjectToMethodName);
            if (methodInfo is null) return;
            methodInfo.Invoke(dto, new object[] { entity });
        }

        public static void MapFromEntityToDto(object dto, object entity)
        {
            if (dto is null || entity is null) return;

            var methodInfo = dto.GetType().GetMethod(Class.InjectFromMethodName);
            if (methodInfo is null) return;
            methodInfo.Invoke(dto, new object[] { entity });
        }

        [Browsable(false)]
        public static object ToDtoArray(Type elementType, object entities)
        {
            if (entities is null) return null;
            var array = entities as Array;
            var methodInfo = elementType.GetMethod(Class.InjectFromMethodName);
            var result = Array.CreateInstance(elementType, array.Length);

            for (var i = 0; i < array.Length; ++i)
            {
                var entityElement = array.GetValue(i);
                if (entityElement == null) continue;
                var dto = Activator.CreateInstance(elementType);
                methodInfo.Invoke(dto, new object[] { entityElement });
                result.SetValue(dto, i);
            }

            return result;
        }

        [Browsable(false)]
        public static object ToEntityArray(Type elementType, object dtos)
        {
            if (dtos is null) return null;
            var array = dtos as Array;
            var dtoElementType = dtos.GetType().GetElementType();
            var methodInfo = dtoElementType.GetMethod(Class.InjectToMethodName);
            var result = Array.CreateInstance(elementType, array.Length);

            for (var i = 0; i < array.Length; ++i)
            {
                var dto = array.GetValue(i);
                if (dto == null) continue;
                var entity = Activator.CreateInstance(elementType);
                methodInfo.Invoke(dto, new object[] { entity });
                result.SetValue(entity, i);
            }

            return result;
        }

        [Browsable(false)]
        public static object ToDtoCollection(Type elementType, object entities)
        {
            if (entities is null) return null;
            var methodInfo = elementType.GetMethod(Class.InjectFromMethodName);
            var result = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));

            foreach(var entity in entities as IEnumerable)
            {
                var dto = Activator.CreateInstance(elementType);
                methodInfo.Invoke(dto, new object[] { entity });
                result.Add(dto);
            }

            return result;
        }

        [Browsable(false)]
        public static object ToEntityCollection(Type elementType, object dtos)
        {
            if (dtos is null) return null;
            var dtoType = dtos.GetType().GetGenericArguments()[0];
            var methodInfo = dtoType.GetMethod(Class.InjectToMethodName);
            var result = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));

            foreach (var dto in dtos as IEnumerable)
            {
                var entity = Activator.CreateInstance(elementType);
                methodInfo.Invoke(dto, new object[] { entity });
                result.Add(entity);
            }

            return result;
        }
    }
}
