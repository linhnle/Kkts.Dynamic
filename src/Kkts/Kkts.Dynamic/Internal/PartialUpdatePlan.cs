using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Kkts.Dynamic.Internal
{
    internal sealed class PartialUpdatePlan
    {
        private readonly Node _root;

        internal PartialUpdatePlan(Class shape)
        {
            var nodes = new Dictionary<Class, Node>();
            _root = BuildNode(shape);

            Node BuildNode(Class current)
            {
                if (nodes.TryGetValue(current, out var existing)) return existing;
                var node = new Node();
                nodes.Add(current, node);
                foreach (var binding in current.Bindings)
                {
                    if (binding.Mode == BindingMode.OneWayToDto || binding.IsComputed) continue;
                    var entry = new Entry(current.GetBuiltType(), current.MappingType, binding);
                    node.Entries.Add(binding.DtoProperty, entry);
                    if (binding.Alternation.PropertyType == SpecialPropertyType.Alternative)
                        entry.Child = BuildNode(binding.Alternation.Cls);
                }
                return node;
            }
        }

        internal void Apply(object dto, object entity, IEnumerable<string> suppliedPaths)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in suppliedPaths)
            {
                if (!BindingValidation.IsPath(path))
                    throw new ArgumentException("Supplied DTO paths must contain non-empty segments.", nameof(suppliedPaths));
                paths.Add(path);
            }
            var selected = paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Select(Resolve).ToArray();
            foreach (var path in paths)
                if (paths.Any(other => other.StartsWith(path + ".", StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException($"Supplied DTO path '{path}' overlaps a supplied descendant. Select the whole object or its descendants, not both.", nameof(suppliedPaths));

            var prepared = new Dictionary<string, Prepared>(StringComparer.OrdinalIgnoreCase);
            foreach (var selection in selected)
            {
                var update = Prepare(selection, dto);
                prepared.Add(selection.Path, update);
                if (selection.Entry.Child == null || update.Value == null) continue;
                foreach (var context in selection.Contexts)
                {
                    foreach (var overlay in context.Node.Entries.Values)
                    {
                        var fullPath = context.Prefix + overlay.Path;
                        if (!fullPath.StartsWith(selection.Path + ".", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!prepared.ContainsKey(fullPath))
                        {
                            var overlayUpdate = Prepare(Resolve(fullPath), dto, true);
                            if (overlayUpdate != null) prepared.Add(fullPath, overlayUpdate);
                        }
                    }
                }
            }
            foreach (var update in prepared.Values.OrderBy(p => p.Selection.Path, StringComparer.OrdinalIgnoreCase))
            {
                var destination = entity;
                foreach (var parent in update.Selection.Parents)
                    destination = parent.GetOrCreate(destination);
                update.Selection.Entry.Write(destination, update.Value);
            }
        }

        private Selection Resolve(string path)
        {
            var node = _root;
            var remaining = path;
            var prefix = "";
            var parents = new List<Entry>();
            var contexts = new List<Context>();
            while (true)
            {
                contexts.Add(new Context { Node = node, Prefix = prefix });
                if (node.Entries.TryGetValue(remaining, out var exact))
                    return new Selection { Path = path, Entry = exact, Parents = parents.ToArray(), Contexts = contexts.ToArray() };
                var parent = node.Entries.Values.Where(e => e.Child != null
                    && remaining.StartsWith(e.Path + ".", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(e => e.Path.Length).FirstOrDefault();
                if (parent == null)
                    throw new ArgumentException($"DTO path '{path}' is not a reverse-enabled mapped value. Use an editable mapped leaf or whole object/collection; grouping, computed, and element paths are not supported.", "suppliedPaths");
                parents.Add(parent);
                prefix += parent.Path + ".";
                remaining = remaining.Substring(parent.Path.Length + 1);
                node = parent.Child;
            }
        }

        private static Prepared Prepare(Selection selection, object dto, bool skipUnavailable = false)
        {
            var source = dto;
            foreach (var parent in selection.Parents)
            {
                var read = parent.Read(source);
                source = read.Value;
                if (source == null)
                {
                    if (skipUnavailable) return null;
                    throw new ArgumentException($"Selected DTO path '{selection.Path}' has a null parent. Supply the whole object to clear it, or provide its descendant value.", "dto");
                }
            }
            var selected = selection.Entry.Read(source);
            if (!selected.Available)
            {
                if (skipUnavailable) return null;
                throw new ArgumentException($"Selected DTO path '{selection.Path}' has a null grouping parent.", "dto");
            }
            var value = selection.Entry.Convert(selected.Value);
            if (value == null && !selection.Entry.CanBeNull)
                throw new ArgumentException($"Selected DTO path '{selection.Path}' cannot assign null to a non-nullable entity member.", "dto");
            return new Prepared { Selection = selection, Value = value };
        }

        private sealed class Node
        {
            internal readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        }
        private sealed class Context { internal Node Node; internal string Prefix; }
        private sealed class Selection
        {
            internal string Path;
            internal Entry Entry;
            internal Entry[] Parents;
            internal Context[] Contexts;
        }
        private sealed class Prepared { internal Selection Selection; internal object Value; }
        private sealed class ReadValue
        {
            public ReadValue(bool available, object value) { Available = available; Value = value; }
            internal readonly bool Available;
            internal readonly object Value;
        }

        private sealed class Entry
        {
            internal readonly string Path;
            internal readonly bool CanBeNull;
            internal readonly Func<object, ReadValue> Read;
            internal readonly Func<object, object> Convert;
            internal readonly Action<object, object> Write;
            internal readonly Func<object, object> GetOrCreate;
            internal Node Child;

            internal Entry(Type dtoType, Type entityType, PropertyBinding binding)
            {
                Path = binding.DtoProperty;
                var dto = Expression.Parameter(typeof(object), "dto");
                var entity = Expression.Parameter(typeof(object), "entity");
                var value = Expression.Parameter(typeof(object), "value");
                using (var sourceTree = new SelectSourceMemberExpressionTree(Expression.Parameter(dtoType, "source")))
                {
                    var source = sourceTree.Property(Path);
                    var typedSource = (ParameterExpression)sourceTree.Value;
                    var readBody = Expression.Condition(source.Guard,
                        Expression.New(typeof(ReadValue).GetConstructor(new[] { typeof(bool), typeof(object) }),
                            Expression.Constant(true), Expression.Convert(source.MemberExpression, typeof(object))),
                        Expression.New(typeof(ReadValue).GetConstructor(new[] { typeof(bool), typeof(object) }),
                            Expression.Constant(false), Expression.Constant(null, typeof(object))));
                    Read = Expression.Lambda<Func<object, ReadValue>>(
                        new Replace(typedSource, Expression.Convert(dto, dtoType)).Visit(readBody), dto).Compile();
                    var transformed = binding.IsTransformed
                        ? BindingExpressions.Transform(binding, Expression.Convert(value, source.MemberExpression.Type), false, false)
                        : (Expression)value;
                    Convert = Expression.Lambda<Func<object, object>>(Expression.Convert(transformed, typeof(object)), value).Compile();
                }
                Type memberType;
                using (var members = new MemberTree(entityType))
                    memberType = members.PropertyOrField(binding.EntityProperty).PropertyOrFieldType;
                CanBeNull = !memberType.IsValueType || Nullable.GetUnderlyingType(memberType) != null;
                var typedEntity = Expression.Convert(entity, entityType);
                var alternative = binding.Alternation;
                if (alternative.PropertyType == SpecialPropertyType.Alternative)
                {
                    GetOrCreate = Expression.Lambda<Func<object, object>>(
                        Expression.Convert(Ensure(typedEntity, binding.EntityProperty), typeof(object)), entity).Compile();
                    var clear = BindingExpressions.Write(typedEntity, binding.EntityProperty, Expression.Constant(null, memberType));
                    var map = Expression.Call(Mapper.MapFromDtoToEntityMethodInfo, value,
                        Expression.Invoke(Expression.Constant(GetOrCreate), entity));
                    Write = Expression.Lambda<Action<object, object>>(Expression.IfThenElse(
                        Expression.Equal(value, Expression.Constant(null)), clear, map), entity, value).Compile();
                }
                else
                {
                    if (alternative.PropertyType == SpecialPropertyType.Array)
                    {
                        var elementType = alternative.EntityElementType;
                        Convert = input => Mapper.ToEntityArray(elementType, input);
                    }
                    else if (alternative.PropertyType == SpecialPropertyType.Collection)
                    {
                        var elementType = alternative.EntityElementType;
                        Convert = input => Mapper.ToEntityCollection(elementType, input);
                    }
                    Write = Expression.Lambda<Action<object, object>>(BindingExpressions.Write(typedEntity,
                        binding.EntityProperty, Expression.Convert(value, memberType)), entity, value).Compile();
                }
            }

            private static Expression Ensure(Expression root, string path)
            {
                var segments = path.Split('.');
                return At(root, 0);
                Expression At(Expression parent, int index)
                {
                    var access = Expression.MakeMemberAccess(parent, parent.Type.GetMemberInfo(segments[index]));
                    var local = Expression.Variable(access.Type, "child");
                    return Expression.Block(new[] { local }, Expression.Assign(local, access),
                        Expression.IfThen(Expression.Equal(local, Expression.Constant(null, local.Type)),
                            Expression.Block(Expression.Assign(local, Expression.New(local.Type.GetRequiredConstructor())),
                                Expression.Assign(access, local), Expression.Empty())),
                        index == segments.Length - 1 ? (Expression)local : At(local, index + 1));
                }
            }
        }

        private sealed class Replace : ExpressionVisitor
        {
            private readonly ParameterExpression _source;
            private readonly Expression _replacement;
            internal Replace(ParameterExpression source, Expression replacement) { _source = source; _replacement = replacement; }
            protected override Expression VisitParameter(ParameterExpression node) => node == _source ? _replacement : node;
        }
    }
}
