using Kkts.Dynamic.Internal;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Kkts.Dynamic
{
    public sealed class BindingBuilder<TEntity> where TEntity : class
    {
        private readonly List<PropertyBinding> _bindings = new List<PropertyBinding>();

        internal BindingBuilder() { }

        internal IEnumerable<PropertyBinding> Bindings => _bindings;

        public BindingBuilder<TEntity> Map<TMember>(
            Expression<Func<TEntity, TMember>> entityPath,
            string dtoPath = null,
            BindingMode mode = BindingMode.TwoWay,
            bool isPrimaryKey = false,
            int primaryKeyOrder = 0,
            string nestedShape = null)
        {
            return Map(new PropertyBinding
            {
                EntityProperty = MemberPath.FromExpression(entityPath),
                DtoProperty = dtoPath,
                Mode = mode,
                IsPrimaryKey = isPrimaryKey,
                PrimaryKeyOrder = primaryKeyOrder,
                NestedShape = nestedShape
            });
        }

        public BindingBuilder<TEntity> MapConverted<TMember, TDto>(
            Expression<Func<TEntity, TMember>> entityPath, string dtoPath,
            Expression<Func<TMember, TDto>> toDto = null,
            Expression<Func<TDto, TMember>> toEntity = null,
            BindingMode mode = BindingMode.OneWayToDto,
            bool isPrimaryKey = false, int primaryKeyOrder = 0)
        {
            return Map(new PropertyBinding
            {
                EntityProperty = MemberPath.FromExpression(entityPath), DtoProperty = dtoPath,
                DtoValueType = typeof(TDto), ToDtoExpression = toDto, ToEntityExpression = toEntity,
                Mode = mode, IsPrimaryKey = isPrimaryKey, PrimaryKeyOrder = primaryKeyOrder
            });
        }

        public BindingBuilder<TEntity> MapConvertedInMemory<TMember, TDto>(
            Expression<Func<TEntity, TMember>> entityPath, string dtoPath,
            Func<TMember, TDto> toDto = null, Func<TDto, TMember> toEntity = null,
            BindingMode mode = BindingMode.OneWayToDto,
            bool isPrimaryKey = false, int primaryKeyOrder = 0)
        {
            return Map(new PropertyBinding
            {
                EntityProperty = MemberPath.FromExpression(entityPath), DtoProperty = dtoPath,
                DtoValueType = typeof(TDto), ToDtoInMemory = toDto, ToEntityInMemory = toEntity,
                Mode = mode, IsPrimaryKey = isPrimaryKey, PrimaryKeyOrder = primaryKeyOrder
            });
        }

        public BindingBuilder<TEntity> Compute<TDto>(string dtoPath, Expression<Func<TEntity, TDto>> value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return Map(new PropertyBinding { DtoProperty = dtoPath, ComputedExpression = value,
                DtoValueType = typeof(TDto), Mode = BindingMode.OneWayToDto });
        }

        public BindingBuilder<TEntity> ComputeInMemory<TDto>(string dtoPath, Func<TEntity, TDto> value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return Map(new PropertyBinding { DtoProperty = dtoPath, ComputedInMemory = value,
                DtoValueType = typeof(TDto), Mode = BindingMode.OneWayToDto });
        }

        public BindingBuilder<TEntity> Map(PropertyBinding binding)
        {
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            _bindings.Add(binding);
            return this;
        }
    }
}
