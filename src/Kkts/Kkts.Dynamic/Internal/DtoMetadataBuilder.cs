using System;
using System.Collections.Generic;
using System.Linq;

namespace Kkts.Dynamic.Internal
{
    internal sealed class DtoMetadataBuilder
    {
        private readonly Dictionary<Type, Layout> _layouts = new Dictionary<Type, Layout>();
        private readonly Dictionary<Class, DtoShapeMetadata> _shapes = new Dictionary<Class, DtoShapeMetadata>();

        internal static DtoMetadataPublication Stage(Class[] classes, Class[] declaredShapes)
        {
            var builder = new DtoMetadataBuilder();
            foreach (var cls in declaredShapes)
                builder._shapes.Add(cls, new DtoShapeMetadata(cls.Name, cls.MappingType, builder.GetLayout(cls.GetBuiltType()).Metadata));

            var overlays = new List<(Class Shape, PropertyBinding Binding)>();
            foreach (var cls in classes)
            {
                foreach (var binding in cls.Bindings)
                {
                    if (cls.Bindings.Any(parent => parent.Alternation.PropertyType == SpecialPropertyType.Alternative
                        && binding.DtoProperty.StartsWith(parent.DtoProperty + ".", StringComparison.OrdinalIgnoreCase)))
                        overlays.Add((cls, binding));
                    else
                        builder.Bind(cls, binding);
                }
            }
            foreach (var overlay in overlays.OrderBy(p => p.Binding.DtoProperty.Count(c => c == '.')))
                builder.Bind(overlay.Shape, overlay.Binding);

            foreach (var layout in builder._layouts.Values)
                layout.Metadata.Initialize(layout.Members.Values.Select(m => new DtoMemberMetadata(
                    m.Name, m.Type, m.Binding, m.NestedShape, m.NestedLayout)));
            foreach (var cls in declaredShapes)
                builder._shapes[cls].InitializeKeys(cls.Bindings.Where(b => b.IsPrimaryKey)
                    .OrderBy(b => b.PrimaryKeyOrder).Select(b => b.DtoProperty));

            var publication = new DtoMetadataPublication();
            var rootTypes = new HashSet<Type>(builder._shapes.Values.Select(s => s.DtoType));
            DtoMetadataRegistry.Stage(publication, builder._shapes.Values, builder._layouts.Keys.Where(t => !rootTypes.Contains(t)));
            foreach (var cls in declaredShapes)
                cls.SetMetadata(builder._shapes[cls], publication);
            return publication;
        }

        private Layout GetLayout(Type type)
        {
            if (_layouts.TryGetValue(type, out var existing)) return existing;
            var result = new Layout(type);
            _layouts.Add(type, result);
            return result;
        }

        private void Bind(Class cls, PropertyBinding binding)
        {
            var scope = GetLayout(cls.GetBuiltType());
            var current = scope;
            var segments = binding.DtoProperty.Split('.');
            var canonical = new List<string>();
            Member member = null;
            for (var i = 0; i < segments.Length; i++)
            {
                if (!current.Members.TryGetValue(segments[i], out member))
                    throw new InvalidOperationException($"Built shape '{cls.Name}' is missing DTO member '{binding.DtoProperty}'.");
                canonical.Add(member.Name);
                if (i == segments.Length - 1) break;
                var next = GetLayout(member.Type);
                member.NestedLayout = next.Metadata;
                current = next;
            }

            Type entityValueType = null;
            var entityPath = binding.IsComputed ? null : CanonicalEntityPath(cls.MappingType, binding.EntityProperty, out entityValueType);
            var kind = Kind(binding);
            member.Binding = new DtoBindingMetadata(cls.DeclaredShapeName, cls.MappingType,
                string.Join(".", canonical), entityPath, entityValueType, member.Type,
                binding.Mode, binding.IsPrimaryKey, binding.PrimaryKeyOrder, kind, Execution(binding, kind), scope.Metadata);
            if (binding.Alternation.Cls != null)
            {
                member.NestedShape = _shapes[binding.Alternation.SelectedShape];
                member.NestedLayout = GetLayout(binding.Alternation.Cls.GetBuiltType()).Metadata;
            }
        }

        private static string CanonicalEntityPath(Type root, string path, out Type valueType)
        {
            var names = new List<string>();
            valueType = root;
            foreach (var segment in path.Split('.'))
            {
                var member = valueType.GetMemberInfo(segment)
                    ?? throw new InvalidOperationException($"Resolved entity member '{path}' is missing on '{root.FullName}'.");
                names.Add(member.Name);
                valueType = member.GetMemberType();
            }
            return string.Join(".", names);
        }

        private static DtoBindingKind Kind(PropertyBinding binding)
        {
            if (binding.IsComputed) return DtoBindingKind.Computed;
            if (binding.IsTransformed) return DtoBindingKind.Converted;
            switch (binding.Alternation.PropertyType)
            {
                case SpecialPropertyType.Alternative: return DtoBindingKind.MappedObject;
                case SpecialPropertyType.Array: return DtoBindingKind.MappedArray;
                case SpecialPropertyType.Collection: return DtoBindingKind.MappedCollection;
                default: return DtoBindingKind.Direct;
            }
        }

        private static DtoForwardExecutionKind Execution(PropertyBinding binding, DtoBindingKind kind)
        {
            if (binding.Mode == BindingMode.OneWayToEntity) return DtoForwardExecutionKind.None;
            if (binding.ComputedExpression != null || binding.ToDtoExpression != null) return DtoForwardExecutionKind.Expression;
            if (binding.ComputedInMemory != null || binding.ToDtoInMemory != null) return DtoForwardExecutionKind.InMemory;
            return kind == DtoBindingKind.MappedObject || kind == DtoBindingKind.MappedArray || kind == DtoBindingKind.MappedCollection
                ? DtoForwardExecutionKind.NestedMapping : DtoForwardExecutionKind.MemberAccess;
        }

        private sealed class Layout
        {
            internal Layout(Type type)
            {
                Metadata = new DtoLayoutMetadata(type);
                Members = type.GetProperties().ToDictionary(p => p.Name, p => new Member(p.Name, p.PropertyType),
                    StringComparer.OrdinalIgnoreCase);
            }
            internal DtoLayoutMetadata Metadata { get; }
            internal Dictionary<string, Member> Members { get; }
        }

        private sealed class Member
        {
            internal Member(string name, Type type) { Name = name; Type = type; }
            internal string Name { get; }
            internal Type Type { get; }
            internal DtoBindingMetadata Binding { get; set; }
            internal DtoShapeMetadata NestedShape { get; set; }
            internal DtoLayoutMetadata NestedLayout { get; set; }
        }
    }
}
