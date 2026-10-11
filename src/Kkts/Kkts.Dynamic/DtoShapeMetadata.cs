using Kkts.Dynamic.Internal;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kkts.Dynamic
{
    /// <summary>The configured value mapping, independent of query-provider support.</summary>
    public enum DtoBindingKind
    {
        /// <summary>An unchanged member value without a transformation or shape substitution.</summary>
        Direct,
        /// <summary>A configured member transformation or DTO value-type override.</summary>
        Converted,
        /// <summary>A value computed from the declaring entity.</summary>
        Computed,
        /// <summary>An object mapped using a selected DTO shape.</summary>
        MappedObject,
        /// <summary>An array mapped using a selected element shape.</summary>
        MappedArray,
        /// <summary>A collection mapped using a selected element shape.</summary>
        MappedCollection
    }

    /// <summary>Distinguishes configured values from synthetic DTO containers.</summary>
    public enum DtoMemberKind
    {
        /// <summary>A configured binding, including a mapped object or collection.</summary>
        MappedValue,
        /// <summary>A synthetic container with no entity binding of its own.</summary>
        Grouping
    }

    /// <summary>The configured forward operation, not expression retrieval or provider translatability.</summary>
    public enum DtoForwardExecutionKind
    {
        /// <summary>No forward operation is enabled.</summary>
        None,
        /// <summary>Ordinary entity member access.</summary>
        MemberAccess,
        /// <summary>An expression-backed conversion or computation.</summary>
        Expression,
        /// <summary>A delegate-only conversion or computation.</summary>
        InMemory,
        /// <summary>Nested shape mapping; descendants must be inspected individually.</summary>
        NestedMapping
    }

    /// <summary>Immutable configured binding provenance. Paths are relative to its declaring entity/shape.</summary>
    public sealed class DtoBindingMetadata
    {
        internal DtoBindingMetadata(string shapeName, Type entityType, string dtoPath, string entityPath,
            Type entityValueType, Type dtoValueType, BindingMode mode, bool isPrimaryKey,
            int primaryKeyOrder, DtoBindingKind kind, DtoForwardExecutionKind execution, DtoLayoutMetadata scope)
        {
            DeclaringShapeName = shapeName;
            EntityType = entityType;
            DtoPath = dtoPath;
            EntityPath = entityPath;
            EntityValueType = entityValueType;
            DtoValueType = dtoValueType;
            Mode = mode;
            IsPrimaryKey = isPrimaryKey;
            PrimaryKeyOrder = isPrimaryKey ? (int?)primaryKeyOrder : null;
            Kind = kind;
            ForwardExecutionKind = execution;
            Scope = scope;
        }

        /// <summary>The registered declaration that owns this binding.</summary>
        public string DeclaringShapeName { get; }
        /// <summary>The declaring entity root, including for parent-owned overlays.</summary>
        public Type EntityType { get; }
        /// <summary>The declaration-relative canonical DTO path.</summary>
        public string DtoPath { get; }
        /// <summary>The declaration-relative source member path; null for computed values.</summary>
        public string EntityPath { get; }
        /// <summary>The source member type; null for computed values.</summary>
        public Type EntityValueType { get; }
        /// <summary>The actual generated DTO member type.</summary>
        public Type DtoValueType { get; }
        /// <summary>The original binding direction, not an authorization policy.</summary>
        public BindingMode Mode { get; }
        /// <summary>Whether this is a key in its declaring shape, not necessarily the root shape.</summary>
        public bool IsPrimaryKey { get; }
        /// <summary>The configured key order, or null for a non-key.</summary>
        public int? PrimaryKeyOrder { get; }
        /// <summary>The configured value mapping.</summary>
        public DtoBindingKind Kind { get; }
        /// <summary>The original enabled forward representation; no provider guarantee is implied.</summary>
        public DtoForwardExecutionKind ForwardExecutionKind { get; }
        internal DtoLayoutMetadata Scope { get; }
    }

    /// <summary>An immutable immediate member of a generated DTO layout.</summary>
    public sealed class DtoMemberMetadata
    {
        internal DtoMemberMetadata(string name, Type type, DtoBindingMetadata binding,
            DtoShapeMetadata nestedShape, DtoLayoutMetadata nestedLayout)
        {
            Name = name;
            DtoValueType = type;
            Binding = binding;
            NestedShape = nestedShape;
            NestedLayout = nestedLayout;
        }

        /// <summary>The emitted local member spelling.</summary>
        public string Name { get; }
        /// <summary>The actual generated member type.</summary>
        public Type DtoValueType { get; }
        /// <summary>Grouping nodes have no binding.</summary>
        public DtoMemberKind Kind => Binding == null ? DtoMemberKind.Grouping : DtoMemberKind.MappedValue;
        /// <summary>Immutable binding provenance, or null for a grouping node.</summary>
        public DtoBindingMetadata Binding { get; }
        /// <summary>The selected registered shape, or null when no shape is substituted.</summary>
        public DtoShapeMetadata NestedShape { get; }
        /// <summary>The effective per-use object, element, or grouping layout, including overlays; otherwise null.</summary>
        public DtoLayoutMetadata NestedLayout { get; }
    }

    /// <summary>A finite immutable layout graph. Consumer recursive enumeration must stop repeated identities.</summary>
    public sealed class DtoLayoutMetadata
    {
        private IReadOnlyList<DtoMemberMetadata> _members;
        private Dictionary<string, DtoMemberMetadata> _index;

        internal DtoLayoutMetadata(Type type) { DtoType = type; }

        /// <summary>The actual generated type of this layout, including contextual overlay types.</summary>
        public Type DtoType { get; }
        /// <summary>Immediate members in ordinal case-insensitive order; recursive layouts are not expanded.</summary>
        public IReadOnlyList<DtoMemberMetadata> Members => _members;

        internal void Initialize(IEnumerable<DtoMemberMetadata> members)
        {
            if (_members != null) throw new InvalidOperationException("The DTO layout is already immutable.");
            var snapshot = members.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            _index = snapshot.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
            _members = Array.AsReadOnly(snapshot);
        }

        internal bool TryGetMember(string name, out DtoMemberMetadata member) => _index.TryGetValue(name, out member);
    }

    /// <summary>Immutable actionable metadata failure information.</summary>
    public sealed class DtoMetadataDiagnostic
    {
        internal DtoMetadataDiagnostic(string code, string shapeName, string path, string segment,
            string message, string suggestedFix)
        {
            Code = code;
            ShapeName = shapeName;
            DtoPath = path;
            FailedSegment = segment;
            Message = message;
            SuggestedFix = suggestedFix;
        }

        /// <summary>A stable machine-readable diagnostic code.</summary>
        public string Code { get; }
        /// <summary>The declared root shape name, or null for unregistered Type lookup.</summary>
        public string ShapeName { get; }
        /// <summary>The requested path, or null for Type lookup.</summary>
        public string DtoPath { get; }
        /// <summary>The failing segment when applicable; otherwise null.</summary>
        public string FailedSegment { get; }
        /// <summary>The reason for failure.</summary>
        public string Message { get; }
        /// <summary>Actionable corrective guidance.</summary>
        public string SuggestedFix { get; }
    }

    /// <summary>An exclusive successful field or diagnostic result.</summary>
    public sealed class DtoFieldResolution
    {
        internal DtoFieldResolution(DtoFieldMetadata field) { Field = field; }
        internal DtoFieldResolution(DtoMetadataDiagnostic diagnostic) { Diagnostic = diagnostic; }
        /// <summary>Whether a field was resolved.</summary>
        public bool Success => Field != null;
        /// <summary>The resolved field on success; otherwise null.</summary>
        public DtoFieldMetadata Field { get; }
        /// <summary>The failure diagnostic; null on success.</summary>
        public DtoMetadataDiagnostic Diagnostic { get; }
    }

    /// <summary>A root-context field resolution, including effective directions and element boundaries.</summary>
    public sealed class DtoFieldMetadata
    {
        internal DtoFieldMetadata(Type entityType, string dtoPath, string entityPath, DtoMemberMetadata member,
            bool forward, bool reverse, IEnumerable<string> boundaries)
        {
            EntityType = entityType;
            DtoPath = dtoPath;
            EntityPath = entityPath;
            Member = member;
            HasForwardMapping = forward;
            HasReverseMapping = reverse;
            CollectionBoundaries = Array.AsReadOnly(boundaries.ToArray());
        }

        /// <summary>The root entity type of this resolution.</summary>
        public Type EntityType { get; }
        /// <summary>The canonical root-relative public DTO path.</summary>
        public string DtoPath { get; }
        /// <summary>Structural source provenance; null for computed/grouping nodes. Element paths are not CLR chains.</summary>
        public string EntityPath { get; }
        /// <summary>The resolved immediate layout member.</summary>
        public DtoMemberMetadata Member { get; }
        /// <summary>The source member type, or null for computed/grouping values.</summary>
        public Type EntityValueType => Binding?.EntityValueType;
        /// <summary>The actual DTO member type.</summary>
        public Type DtoValueType => Member.DtoValueType;
        /// <summary>The mapped value or grouping classification.</summary>
        public DtoMemberKind Kind => Member.Kind;
        /// <summary>The original immutable binding, or null for grouping nodes.</summary>
        public DtoBindingMetadata Binding => Member.Binding;
        /// <summary>Effective forward availability, accounting for mapped ancestors and overlay ownership.</summary>
        public bool HasForwardMapping { get; }
        /// <summary>Effective reverse availability; not an update or authorization policy.</summary>
        public bool HasReverseMapping { get; }
        /// <summary>Canonical DTO paths of crossed element boundaries, not including a selected whole collection.</summary>
        public IReadOnlyList<string> CollectionBoundaries { get; }
        /// <summary>An ordinary forward structural alias, or null. It implies neither predicate equivalence nor translation.</summary>
        public string DirectEntityAlias => HasForwardMapping && Binding?.Kind == DtoBindingKind.Direct
            && CollectionBoundaries.Count == 0 ? EntityPath : null;
        /// <summary>The selected registered nested shape, or null.</summary>
        public DtoShapeMetadata NestedShape => Member.NestedShape;
        /// <summary>The effective nested layout, or null.</summary>
        public DtoLayoutMetadata NestedLayout => Member.NestedLayout;
        /// <summary>The forward representation when effectively enabled; otherwise None.</summary>
        public DtoForwardExecutionKind ForwardExecutionKind => HasForwardMapping
            ? Binding.ForwardExecutionKind : DtoForwardExecutionKind.None;
    }

    /// <summary>Immutable metadata for a successfully built named shape, usable after builder disposal.</summary>
    public sealed class DtoShapeMetadata
    {
        private IReadOnlyList<DtoFieldMetadata> _primaryKeys;

        internal DtoShapeMetadata(string name, Type entityType, DtoLayoutMetadata layout)
        {
            Name = name;
            EntityType = entityType;
            Layout = layout;
        }

        /// <summary>The explicitly declared shape name.</summary>
        public string Name { get; }
        /// <summary>The declared root entity type.</summary>
        public Type EntityType { get; }
        /// <summary>The generated root DTO type.</summary>
        public Type DtoType => Layout.DtoType;
        /// <summary>The finite effective root layout.</summary>
        public DtoLayoutMetadata Layout { get; }
        /// <summary>Root keys in exactly the stable identifier extraction order; child keys are not promoted.</summary>
        public IReadOnlyList<DtoFieldMetadata> PrimaryKeys => _primaryKeys;

        internal void InitializeKeys(IEnumerable<string> paths)
        {
            if (_primaryKeys != null) throw new InvalidOperationException("The DTO shape is already immutable.");
            _primaryKeys = Array.AsReadOnly(paths.Select(path =>
            {
                if (!Layout.TryGetMember(path, out var member) || member.Binding == null)
                    throw new InvalidOperationException($"Shape '{Name}' is missing built key metadata for '{path}'.");
                var binding = member.Binding;
                return new DtoFieldMetadata(EntityType, member.Name, binding.EntityPath, member,
                    binding.Mode != BindingMode.OneWayToEntity, binding.Mode != BindingMode.OneWayToDto, Array.Empty<string>());
            }).ToArray());
        }

        /// <summary>Retrieve exact declared-Type metadata without a builder; contextual/unregistered types throw.</summary>
        public static DtoShapeMetadata GetForType(Type dtoType)
        {
            if (TryGetForType(dtoType, out var metadata, out var diagnostic)) return metadata;
            throw new ArgumentException(diagnostic.Message + " " + diagnostic.SuggestedFix, nameof(dtoType));
        }

        /// <summary>Return false with a diagnostic for unsupported types. A null Type throws ArgumentNullException.</summary>
        public static bool TryGetForType(Type dtoType, out DtoShapeMetadata metadata, out DtoMetadataDiagnostic diagnostic)
        {
            if (dtoType == null) throw new ArgumentNullException(nameof(dtoType));
            return DtoMetadataRegistry.TryGet(dtoType, out metadata, out diagnostic);
        }

        /// <summary>Resolve a finite case-insensitive public DTO path. Syntax/lookup failures return explicit diagnostics.</summary>
        public DtoFieldResolution ResolveField(string dtoPath)
        {
            if (string.IsNullOrEmpty(dtoPath))
                return Failure("InvalidDtoPath", dtoPath, null, "A non-empty DTO path is required.");
            var segments = dtoPath.Split('.');
            foreach (var segment in segments)
            {
                if (segment.Length == 0 || segment != segment.Trim()
                    || segment.IndexOfAny(new[] { '[', ']', '*', '(', ')' }) >= 0)
                    return Failure("InvalidDtoPath", dtoPath, segment, "DTO paths require non-empty unpadded member segments without index or wildcard syntax.");
            }

            var layout = Layout;
            var contexts = new List<SourceContext> { new SourceContext(Layout, "", true, true) };
            var canonical = new List<string>();
            var boundaries = new List<string>();
            for (var i = 0; i < segments.Length; i++)
            {
                if (!layout.TryGetMember(segments[i], out var member))
                    return Failure("UnknownDtoField", dtoPath, segments[i],
                        $"Member '{segments[i]}' is not configured in shape '{Name}' at '{string.Join(".", canonical)}'.");
                canonical.Add(member.Name);
                var binding = member.Binding;
                var context = binding == null ? null : contexts.Last(c => c.Layout == binding.Scope);
                var entityPath = binding?.EntityPath == null ? null : Join(context.EntityPrefix, binding.EntityPath);
                var forward = binding != null && context.Forward && binding.Mode != BindingMode.OneWayToEntity;
                var reverse = binding != null && context.Reverse && binding.Mode != BindingMode.OneWayToDto;
                var path = string.Join(".", canonical);
                if (i == segments.Length - 1)
                    return new DtoFieldResolution(new DtoFieldMetadata(EntityType, path, entityPath, member, forward, reverse, boundaries));
                if (member.NestedLayout == null)
                    return Failure("NoNestedShape", dtoPath, segments[i + 1],
                        $"DTO value '{path}' has no configured nested shape or grouping layout.");
                if (binding != null)
                {
                    if (binding.Kind == DtoBindingKind.MappedArray || binding.Kind == DtoBindingKind.MappedCollection)
                        boundaries.Add(path);
                    contexts.Add(new SourceContext(member.NestedLayout, entityPath, forward, reverse));
                }
                layout = member.NestedLayout;
            }
            throw new InvalidOperationException("DTO resolution did not consume a path.");
        }

        private DtoFieldResolution Failure(string code, string path, string segment, string message)
        {
            return new DtoFieldResolution(new DtoMetadataDiagnostic(code, Name, path, segment, message,
                "Use a configured public DTO member from the effective layout; inspect collection elements without index syntax."));
        }

        private static string Join(string prefix, string path) => prefix.Length == 0 ? path : prefix + "." + path;

        private sealed class SourceContext
        {
            internal SourceContext(DtoLayoutMetadata layout, string entityPrefix, bool forward, bool reverse)
            {
                Layout = layout;
                EntityPrefix = entityPrefix;
                Forward = forward;
                Reverse = reverse;
            }
            internal DtoLayoutMetadata Layout { get; }
            internal string EntityPrefix { get; }
            internal bool Forward { get; }
            internal bool Reverse { get; }
        }
    }
}
