using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Kkts.Dynamic.Internal
{
    internal sealed class SelectSourceMemberExpressionTree : IDisposable
    {
        private readonly Dictionary<string, SelectSourceMemberExpressionTree> _tree = new Dictionary<string, SelectSourceMemberExpressionTree>(StringComparer.OrdinalIgnoreCase);
        private readonly ParameterExpression _param;

        private SelectSourceMemberExpressionTree() { }

        public SelectSourceMemberExpressionTree(ParameterExpression param)
        {
            _param = param;
            Guard = Expression.NotEqual(param, Expression.Constant(null, param.Type));
        }

        public string Name { get; set; }

        public MemberExpression MemberExpression { get; set; }

        public Expression Guard { get; private set; }
        internal Expression Value => MemberExpression ?? (Expression)_param;

        public SelectSourceMemberExpressionTree Property(string name)
        {
            var segments = name.Split('.');
            var current = this;
            foreach (var segment in segments)
            {
                if (current._tree.TryGetValue(segment, out var existing))
                {
                    current = existing;
                }
                else
                {
                    var parent = current.MemberExpression ?? (Expression)_param;
                    var member = parent.Type.GetMemberInfo(segment)
                        ?? throw new InvalidOperationException($"Member '{segment}' does not exist on '{parent.Type.FullName}'.");
                    var exp = Expression.MakeMemberAccess(parent, member);
                    var guard = current.Guard;
                    if (current.MemberExpression != null && !parent.Type.IsValueType)
                        guard = Expression.AndAlso(guard, Expression.NotEqual(parent, Expression.Constant(null, parent.Type)));
                    var element = new SelectSourceMemberExpressionTree
                    {
                        Name = segment,
                        MemberExpression = exp,
                        Guard = guard
                    };
                    current._tree.Add(segment, element);
                    current = element;
                }
            }

            return current;
        }

        void IDisposable.Dispose()
        {
            foreach (IDisposable element in _tree.Values.ToArray())
            {
                element.Dispose();
            }

            _tree.Clear();
        }
    }
}
