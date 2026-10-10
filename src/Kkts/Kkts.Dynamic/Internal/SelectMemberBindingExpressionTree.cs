using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Kkts.Dynamic.Internal
{
    internal sealed class SelectMemberBindingExpressionTree : IDisposable
    {
        private SelectSourceMemberExpressionTree _sourceMemberTree;
        private readonly Type _targetType;
        private readonly Dictionary<string, (MemberInfo Member, SelectMemberBindingExpressionTree Tree)> _tree;
        private readonly Dictionary<string, MemberAssignment> _memberBindings;
        private readonly List<Expression> _guards = new List<Expression>();
        private int _level;
        private Expression _presence;
        private readonly bool _isRoot;

        public SelectMemberBindingExpressionTree(ParameterExpression param, Type targetType) : this(targetType, 0)
        {
            _sourceMemberTree = new SelectSourceMemberExpressionTree(param);
            _isRoot = true;
        }

        private SelectMemberBindingExpressionTree(Type targetType, int level)
        {
            _targetType = targetType;
            _level = level;
            _tree = new Dictionary<string, (MemberInfo, SelectMemberBindingExpressionTree)>(StringComparer.OrdinalIgnoreCase);
            _memberBindings = new Dictionary<string, MemberAssignment>(StringComparer.OrdinalIgnoreCase);
        }

        public void Bind(IEnumerable<PropertyBinding> bindings)
        {
            foreach (var binding in bindings)
            {
                if (binding.Mode == BindingMode.OneWayToEntity) continue;
                var source = _sourceMemberTree.Property(binding.EntityProperty);
                var segments = binding.DtoProperty.Split('.');
                var current = this;
                for (var i = 0; i < segments.Length - 1; ++i)
                {
                    var segment = segments[i];
                    if (!current._tree.TryGetValue(segment, out var entry))
                    {
                        var member = current.GetMember(segment);
                        var child = new SelectMemberBindingExpressionTree(member.GetMemberType(), current._level);
                        current._tree.Add(segment, (member, child));
                        entry = (member, child);
                    }
                    current._guards.Add(source.Guard);
                    current = entry.Tree;
                }

                current.BindMember(segments[segments.Length - 1], source, binding.Alternation);
            }
        }

        private void BindMember(string name, SelectSourceMemberExpressionTree source, AlternationInfo alternation)
        {
            var member = GetMember(name);
            var value = source.MemberExpression;
            _guards.Add(source.Guard);
            if (alternation.PropertyType == SpecialPropertyType.Alternative)
            {
                CheckDepth();
                if (!_tree.TryGetValue(name, out var entry))
                {
                    var child = new SelectMemberBindingExpressionTree(member.GetMemberType(), _level + 1);
                    child._sourceMemberTree = source;
                    child._presence = HasValue(source);
                    _tree.Add(name, (member, child));
                    entry = (member, child);
                }
                entry.Tree.Bind(alternation.Cls.Bindings);
                return;
            }

            Expression mappedValue = value;
            Expression guard = source.Guard;
            if (alternation.PropertyType == SpecialPropertyType.Array || alternation.PropertyType == SpecialPropertyType.Collection)
            {
                CheckDepth();
                var element = Expression.Parameter(alternation.EntityElementType, "element");
                var dtoElementType = alternation.Cls.GetBuiltType();
                var elementTree = new SelectMemberBindingExpressionTree(element, dtoElementType);
                elementTree._level = _level + 1;
                elementTree.Bind(alternation.Cls.Bindings);
                var elementValue = Expression.Condition(
                    Expression.NotEqual(element, Expression.Constant(null, element.Type)),
                    elementTree.Build(), Expression.Default(dtoElementType));
                var selector = Expression.Lambda(elementValue, element);
                var select = Expression.Call(typeof(Enumerable), nameof(Enumerable.Select),
                    new[] { element.Type, dtoElementType }, value, selector);
                mappedValue = Expression.Call(typeof(Enumerable),
                    alternation.PropertyType == SpecialPropertyType.Array ? nameof(Enumerable.ToArray) : nameof(Enumerable.ToList),
                    new[] { dtoElementType }, select);
                if (mappedValue.Type != member.GetMemberType())
                    mappedValue = Expression.Convert(mappedValue, member.GetMemberType());
                guard = HasValue(source);
            }

            var safeValue = Expression.Condition(guard, mappedValue, Expression.Default(member.GetMemberType()));
            _memberBindings[name] = Expression.Bind(member, safeValue);
        }

        private MemberInfo GetMember(string name)
        {
            return _targetType.GetMemberInfo(name)
                ?? throw new InvalidOperationException($"Member '{name}' does not exist on '{_targetType.FullName}'.");
        }

        private static Expression HasValue(SelectSourceMemberExpressionTree source)
        {
            return Expression.AndAlso(source.Guard,
                Expression.NotEqual(source.MemberExpression, Expression.Constant(null, source.MemberExpression.Type)));
        }

        private void CheckDepth()
        {
            if (_level >= Class.MaxRecursionDepthToBuildSelectorExpression)
                throw new InvalidOperationException($"Selector recursion exceeds {Class.MaxRecursionDepthToBuildSelectorExpression}. Increase {nameof(Class.MaxRecursionDepthToBuildSelectorExpression)} for a finite graph or remove recursive bindings.");
        }

        internal Expression Build()
        {
            var bindings = _memberBindings.Values.Cast<MemberBinding>().ToList();
            bindings.AddRange(_tree.Select(p => Expression.Bind(p.Value.Member, p.Value.Tree.Build())));
            Expression result = Expression.MemberInit(Expression.New(_targetType), bindings);
            if (_isRoot) return result;
            var presence = _presence ?? (_guards.Count == 0
                ? (Expression)Expression.Constant(true)
                : _guards.Aggregate(Expression.OrElse));
            return Expression.Condition(presence, result, Expression.Default(_targetType));
        }

        void IDisposable.Dispose()
        {
            foreach (var entry in _tree.Values) ((IDisposable)entry.Tree).Dispose();
            _tree.Clear();
            _memberBindings.Clear();
            _guards.Clear();
            if (_sourceMemberTree != null) ((IDisposable)_sourceMemberTree).Dispose();
        }
    }
}
