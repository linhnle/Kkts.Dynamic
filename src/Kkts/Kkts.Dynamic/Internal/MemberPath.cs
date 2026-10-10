using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace Kkts.Dynamic.Internal
{
    internal static class MemberPath
    {
        internal static string FromExpression(LambdaExpression expression)
        {
            if (expression == null) throw new ArgumentNullException(nameof(expression));
            Expression current = expression.Body;
            if (current is UnaryExpression boxing && boxing.NodeType == ExpressionType.Convert
                && boxing.Method == null && boxing.Operand.Type.IsValueType
                && boxing.Type.IsAssignableFrom(boxing.Operand.Type))
                current = boxing.Operand;

            var segments = new Stack<string>();
            while (current is MemberExpression member && member.Expression != null)
            {
                if (!(member.Member is PropertyInfo) && !(member.Member is FieldInfo))
                    throw InvalidPath();
                segments.Push(member.Member.Name);
                current = member.Expression;
            }

            if (expression.Parameters.Count != 1 || current != expression.Parameters[0] || segments.Count == 0)
                throw InvalidPath();
            return string.Join(".", segments);
        }

        private static ArgumentException InvalidPath()
        {
            return new ArgumentException(
                "Expected an instance property or field path rooted at the entity parameter. " +
                "Use a conversion or computed-value binding for calculations.", "expression");
        }
    }
}
