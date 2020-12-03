using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Kkts.Dynamic.Internal
{
    internal class SelectMemberBindingExpressionTree : IDisposable
    {
        private SelectSourceMemberExpressionTree _sourceMemberTree;
        private readonly Type _targetType;
        private Type _sourceType;
        private readonly Dictionary<string, (MemberInfo Member, SelectMemberBindingExpressionTree Tree)> _tree;
        private readonly List<MemberAssignment> _memberBindings;

        public SelectMemberBindingExpressionTree(ParameterExpression param, Type targetType)
        {
            _sourceType = param?.Type;
            _sourceMemberTree = new SelectSourceMemberExpressionTree(param);
            _targetType = targetType;
            _tree = new Dictionary<string, (MemberInfo Member, SelectMemberBindingExpressionTree Tree)>();
            _memberBindings = new List<MemberAssignment>();
        }

        private SelectMemberBindingExpressionTree(Type targetType) : this(null, targetType)
        {
        }

        public void Bind(IEnumerable<PropertyBinding> bindings)
        {
            foreach (var binding in bindings)
            {
                if (binding.Mode == BindingMode.OneWayToEntity) continue;
                Bind(binding.EntityProperty, binding.DtoProperty ?? binding.EntityProperty, binding.Alternation);
            }
        }

        private void Bind(string sourceProperty, string targetProperty, AlternationInfo alternation)
        {
            var sourceMember = _sourceMemberTree.Property(sourceProperty);
            if (!sourceMember.MemberExpression.Member.IsPrimitive() 
                && alternation.PropertyType != SpecialPropertyType.Alternative)
            {
                return;
            }
            
            MemberInfo member;
            if (targetProperty.IndexOf('.') == -1)
            {
                if (_memberBindings.Any(p => p.Member.EqualsName(targetProperty))) return;

                if (alternation.PropertyType == SpecialPropertyType.Alternative)
                {
                    member = _targetType.GetMemberInfo(targetProperty)
                        ?? throw new InvalidOperationException($"The property {targetProperty} does not exist in type {_targetType.FullName}");
                    var memberType = member.GetMemberType();
                    var element = new SelectMemberBindingExpressionTree(memberType);
                    element._sourceType = sourceMember.MemberExpression.Member.GetMemberType();
                    element._sourceMemberTree = sourceMember;
                    _tree.Add(targetProperty, (member, element));
                    element.Bind(alternation.Cls.Bindings);
                }
                else
                {
                    member = _targetType.GetMemberInfo(targetProperty)
                        ?? throw new InvalidOperationException($"The property or field {targetProperty} does not exist, it is related to {_sourceType.FullName}");
                    var memberBinding = Expression.Bind(member, sourceMember.MemberExpression);
                    _memberBindings.Add(memberBinding);
                }

                return;
            }

            var segments = targetProperty.Split('.');
            var current = this;
            var currentType = _targetType;
            string segment;
            for (var i = 0; i < segments.Length - 1; ++i)
            {
                segment = segments[i];
                if (current._tree.ContainsKey(segment))
                {
                    current = current._tree[segment].Tree;
                    currentType = current._targetType;
                }
                else
                {
                    member = currentType.GetMemberInfo(segment) 
                        ?? throw new InvalidOperationException($"The property {targetProperty} does not exist in type {_targetType.FullName}");
                    currentType = member.GetMemberType();
                    var element = new SelectMemberBindingExpressionTree(currentType);
                    element._sourceType = current._sourceType;
                    current._tree.Add(segment, (member, element));
                    current = element;
                }
            }

            segment = segments[segments.Length - 1];
            if (current._memberBindings.Any(p => p.Member.EqualsName(segment))) return;

            if (alternation.PropertyType == SpecialPropertyType.Alternative)
            {
                member = currentType.GetMemberInfo(segment)
                        ?? throw new InvalidOperationException($"The property {targetProperty} does not exist in type {_targetType.FullName}");
                var memberType = member.GetMemberType();
                var element = new SelectMemberBindingExpressionTree(memberType);
                element._sourceType = sourceMember.MemberExpression.Member.GetMemberType();
                element._sourceMemberTree = sourceMember;
                current._tree.Add(segment, (member, element));
                element.Bind(alternation.Cls.Bindings);
            }
            else
            {
                member = currentType.GetMemberInfo(segment);
                current._memberBindings.Add(Expression.Bind(member, sourceMember.MemberExpression));
            }
        }

        internal MemberInitExpression Build()
        {
            var bindings = _memberBindings.ToList();
            bindings.AddRange(_tree.Select(p => Expression.Bind(p.Value.Member, p.Value.Tree.Build())));
            return Expression.MemberInit(Expression.New(_targetType), bindings.ToArray());
        }

        void IDisposable.Dispose()
        {
            foreach (var element in _tree.Values.ToArray())
            {
                ((IDisposable)element.Tree).Dispose();
            }

            _tree.Clear();
            ((IDisposable)_sourceMemberTree).Dispose();
        }
    }
}
