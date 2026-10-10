using System;
using System.Linq.Expressions;

namespace Kkts.Dynamic.Internal
{
    internal static class BindingExpressions
    {
        internal static Expression Transform(PropertyBinding binding, Expression input, bool forward, bool projection)
        {
            var expression = binding.IsComputed ? binding.ComputedExpression
                : forward ? binding.ToDtoExpression : binding.ToEntityExpression;
            var callback = binding.IsComputed ? binding.ComputedInMemory
                : forward ? binding.ToDtoInMemory : binding.ToEntityInMemory;
            if (expression != null)
                return new Substitute(expression.Parameters[0], input).Visit(expression.Body);
            if (callback != null)
            {
                if (projection)
                    throw new InvalidOperationException($"DTO path '{binding.DtoProperty}' has an in-memory-only transformation and cannot be projected.");
                return Expression.Call(Expression.Constant(callback, callback.GetType()),
                    callback.GetType().GetMethod("Invoke"), input);
            }
            return input;
        }

        internal static Expression Write(Expression root, string path, Expression value, Func<string, bool> preserveNullParent = null)
        {
            var segments = path.Split('.');
            var prepared = Expression.Variable(value.Type, "value");
            return Expression.Block(new[] { prepared }, Expression.Assign(prepared, value), WriteAt(root, 0, ""));

            Expression WriteAt(Expression parent, int index, string prefix)
            {
                var member = parent.Type.GetMemberInfo(segments[index])
                    ?? throw new InvalidOperationException($"Member '{segments[index]}' does not exist on '{parent.Type.FullName}'.");
                var access = Expression.MakeMemberAccess(parent, member);
                if (index == segments.Length - 1)
                    return Expression.Block(Expression.Assign(access, prepared), Expression.Empty());
                var child = Expression.Variable(access.Type, "child");
                var childPath = prefix.Length == 0 ? segments[index] : prefix + "." + segments[index];
                var nested = WriteAt(child, index + 1, childPath);
                var absent = Expression.Equal(child, Expression.Constant(null, child.Type));
                if (preserveNullParent != null && preserveNullParent(childPath))
                    nested = Expression.IfThen(Expression.Not(absent), nested);
                else
                    nested = Expression.Block(
                        Expression.IfThen(absent, Expression.Block(
                            Expression.Assign(child, Expression.New(child.Type.GetRequiredConstructor())),
                            Expression.Assign(access, child))), nested);
                return Expression.Block(new[] { child }, Expression.Assign(child, access), nested);
            }
        }

        private sealed class Substitute : ExpressionVisitor
        {
            private readonly ParameterExpression _parameter;
            private readonly Expression _replacement;
            internal Substitute(ParameterExpression parameter, Expression replacement)
            {
                _parameter = parameter;
                _replacement = replacement;
            }
            protected override Expression VisitParameter(ParameterExpression node) => node == _parameter ? _replacement : node;
        }
    }
}
