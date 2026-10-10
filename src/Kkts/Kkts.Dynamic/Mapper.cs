using System;
using Kkts.Dynamic.Internal;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Kkts.Dynamic
{
    public static class Mapper
    {
        internal static readonly MethodInfo MapFromEntityToDtoMethodInfo = typeof(Mapper).GetMethod(nameof(MapFromEntityToDto));
        internal static readonly MethodInfo MapFromDtoToEntityMethodInfo = typeof(Mapper).GetMethod(nameof(MapFromDtoToEntity));
        internal static readonly MethodInfo ToDtoArrayMethodInfo = typeof(Mapper).GetMethod(nameof(ToDtoArray));
        internal static readonly MethodInfo ToEntityArrayMethodInfo = typeof(Mapper).GetMethod(nameof(ToEntityArray));
        internal static readonly MethodInfo ToDtoCollectionMethodInfo = typeof(Mapper).GetMethod(nameof(ToDtoCollection));
        internal static readonly MethodInfo ToEntityCollectionMethodInfo = typeof(Mapper).GetMethod(nameof(ToEntityCollection));
        internal static readonly MethodInfo GetTypeFromHandleMethodInfo = typeof(Type).GetMethod("GetTypeFromHandle");
        // Weak keys allow generated types and their delegates to be collected together.
        private static readonly ConditionalWeakTable<Type, Lazy<MappingMethods>> Mappings = new ConditionalWeakTable<Type, Lazy<MappingMethods>>();
        private static readonly ConditionalWeakTable<Type, Lazy<Func<object>>> Factories = new ConditionalWeakTable<Type, Lazy<Func<object>>>();
        private static readonly ConditionalWeakTable<Type, PartialUpdatePlan> PartialUpdates = new ConditionalWeakTable<Type, PartialUpdatePlan>();

        public static void MapPartialFromDtoToEntity(object dto, object entity, IEnumerable<string> suppliedDtoPaths)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (suppliedDtoPaths == null) throw new ArgumentNullException(nameof(suppliedDtoPaths));
            GetMapping(dto.GetType()).ValidateEntity(entity);
            if (!PartialUpdates.TryGetValue(dto.GetType(), out var plan))
                throw new ArgumentException("The DTO type has no generated partial-update metadata.", nameof(dto));
            plan.Apply(dto, entity, suppliedDtoPaths);
        }

        internal static void RegisterPartialUpdates(Class shape)
        {
            PartialUpdates.Add(shape.GetBuiltType(), new PartialUpdatePlan(shape));
        }

        public static void MapFromDtoToEntity(object dto, object entity)
        {
            if (dto is null || entity is null) return;

            var mapping = GetMapping(dto.GetType());
            mapping.ValidateEntity(entity);
            mapping.ToEntity(dto, entity);
        }

        public static void MapFromEntityToDto(object dto, object entity)
        {
            if (dto is null || entity is null) return;

            var mapping = GetMapping(dto.GetType());
            mapping.ValidateEntity(entity);
            mapping.ToDto(dto, entity);
        }

        internal static object CreateMappedDto(Type dtoType, object entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            var mapping = GetMapping(dtoType);
            mapping.ValidateEntity(entity);
            var dto = GetFactory(dtoType)();
            mapping.ToDto(dto, entity);
            return dto;
        }

        [Browsable(false)]
        public static object ToDtoArray(Type elementType, object entities)
        {
            if (entities is null) return null;
            var array = GetArray(entities, nameof(entities));
            var mapping = GetMapping(elementType);
            var createDto = GetFactory(elementType);
            var result = Array.CreateInstance(elementType, array.Length);

            for (var i = 0; i < array.Length; ++i)
            {
                var entityElement = array.GetValue(i);
                if (entityElement == null) continue;
                mapping.ValidateEntity(entityElement);
                var dto = createDto();
                mapping.ToDto(dto, entityElement);
                result.SetValue(dto, i);
            }

            return result;
        }

        [Browsable(false)]
        public static object ToEntityArray(Type elementType, object dtos)
        {
            if (dtos is null) return null;
            var array = GetArray(dtos, nameof(dtos));
            var createEntity = GetFactory(elementType);
            var result = Array.CreateInstance(elementType, array.Length);

            for (var i = 0; i < array.Length; ++i)
            {
                var dto = array.GetValue(i);
                if (dto == null) continue;
                var entity = createEntity();
                MapFromDtoToEntity(dto, entity);
                result.SetValue(entity, i);
            }

            return result;
        }

        [Browsable(false)]
        public static object ToDtoCollection(Type elementType, object entities)
        {
            if (entities is null) return null;
            var source = GetEnumerable(entities, nameof(entities));
            var mapping = GetMapping(elementType);
            var createDto = GetFactory(elementType);
            var result = CreateList(elementType);

            foreach(var entity in source)
            {
                if (entity == null)
                {
                    result.Add(null);
                    continue;
                }
                mapping.ValidateEntity(entity);
                var dto = createDto();
                mapping.ToDto(dto, entity);
                result.Add(dto);
            }

            return result;
        }

        [Browsable(false)]
        public static object ToEntityCollection(Type elementType, object dtos)
        {
            if (dtos is null) return null;
            var source = GetEnumerable(dtos, nameof(dtos));
            var createEntity = GetFactory(elementType);
            var result = CreateList(elementType);

            foreach (var dto in source)
            {
                if (dto == null)
                {
                    result.Add(null);
                    continue;
                }
                var entity = createEntity();
                MapFromDtoToEntity(dto, entity);
                result.Add(entity);
            }

            return result;
        }

        internal static object[] GetIds(object dto) => GetMapping(dto.GetType()).ReadIds(dto);

        private static Array GetArray(object value, string name)
        {
            if (!(value is Array array) || array.Rank != 1 || array.GetLowerBound(0) != 0)
                throw new ArgumentException("Expected a single-dimensional, zero-based array.", name);
            return array;
        }

        private static IEnumerable GetEnumerable(object value, string name)
        {
            return value as IEnumerable ?? throw new ArgumentException("Expected an enumerable collection.", name);
        }

        private static IList CreateList(Type elementType)
        {
            if (elementType == null) throw new ArgumentNullException(nameof(elementType));
            return (IList)GetFactory(typeof(List<>).MakeGenericType(elementType))();
        }

        private static Func<object> GetFactory(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            return Factories.GetValue(type, key => new Lazy<Func<object>>(() =>
                Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(key.GetRequiredConstructor()), typeof(object))).Compile())).Value;
        }

        private static MappingMethods GetMapping(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            return Mappings.GetValue(type, key => new Lazy<MappingMethods>(() => new MappingMethods(key))).Value;
        }

        private sealed class MappingMethods
        {
            private readonly Type _entityType;
            public Action<object, object> ToDto { get; }
            public Action<object, object> ToEntity { get; }
            public Func<object, object[]> ReadIds { get; }

            public MappingMethods(Type dtoType)
            {
                var from = dtoType.GetMethod(Class.InjectFromMethodName);
                var to = dtoType.GetMethod(Class.InjectToMethodName);
                var ids = dtoType.GetMethod(Class.GetIdsMethodName);
                if (!typeof(IDtoObject).IsAssignableFrom(dtoType) || from == null || to == null || ids == null
                    || from.IsStatic || to.IsStatic || ids.IsStatic
                    || from.ReturnType != typeof(void) || to.ReturnType != typeof(void)
                    || from.GetParameters().Length != 1 || to.GetParameters().Length != 1
                    || ids.GetParameters().Length != 0 || ids.ReturnType != typeof(object[]))
                    throw new ArgumentException($"Type '{dtoType.FullName}' is not a generated DTO.", nameof(dtoType));
                _entityType = from.GetParameters()[0].ParameterType;
                if (to.GetParameters()[0].ParameterType != _entityType)
                    throw new ArgumentException($"Type '{dtoType.FullName}' has inconsistent mapping methods.", nameof(dtoType));
                var dto = Expression.Parameter(typeof(object), "dto");
                var entity = Expression.Parameter(typeof(object), "entity");
                var instance = Expression.Convert(dto, dtoType);
                var source = Expression.Convert(entity, _entityType);
                ToDto = Expression.Lambda<Action<object, object>>(Expression.Call(instance, from, source), dto, entity).Compile();
                ToEntity = Expression.Lambda<Action<object, object>>(Expression.Call(instance, to, source), dto, entity).Compile();
                ReadIds = Expression.Lambda<Func<object, object[]>>(Expression.Call(instance, ids), dto).Compile();
            }

            public void ValidateEntity(object entity)
            {
                if (!_entityType.IsInstanceOfType(entity))
                    throw new ArgumentException($"Expected an entity assignable to '{_entityType.FullName}'.", nameof(entity));
            }
        }
    }
}
