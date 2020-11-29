using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Kkts.Dynamic.Internal
{
    internal class SelectSourceMemberExpressionTree : IDisposable
    {
        private readonly Dictionary<string, SelectSourceMemberExpressionTree> _tree = new Dictionary<string, SelectSourceMemberExpressionTree>();
        private readonly ParameterExpression _param;

        private SelectSourceMemberExpressionTree() { }

        public SelectSourceMemberExpressionTree(ParameterExpression param)
        {
            _param = param;
        }

        public string Name { get; set; }

        public MemberExpression MemberExpression { get; set; }

        public SelectSourceMemberExpressionTree Property(string name)
        {
            var segments = name.Split('.');
            var current = this;
            foreach (var segment in segments)
            {
                if (current._tree.ContainsKey(segment))
                {
                    current = current._tree[segment];
                }
                else
                {
                    var exp = Expression.PropertyOrField(current.MemberExpression ?? (Expression)_param, segment);
                    var element = new SelectSourceMemberExpressionTree
                    {
                        Name = segment,
                        MemberExpression = exp
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
