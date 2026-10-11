using Kkts.Dynamic.Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Kkts.Dynamic
{
    public sealed class DynamicAssembly : IDisposable
    {
        private readonly ModuleBuilder _moduleBuilder;
        private readonly Dictionary<string, Class> _classMap;
        private readonly List<Class> _derivedClasses = new List<Class>();
        private BuildState _state;
        private Exception _buildError;

        private enum BuildState { Declaring, Building, Built, Faulted, Disposed }

        public DynamicAssembly() : this(Guid.NewGuid().ToString()) { }

        public DynamicAssembly(string name)
        {
            Name = !string.IsNullOrWhiteSpace(name) ? name : throw new ArgumentException($"{nameof(name)} is required", nameof(name));
            var assemblyName = new AssemblyName($"__{Name}_Dynamic_Assembly__");
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndCollect);
            _moduleBuilder = assemblyBuilder.DefineDynamicModule($"{assemblyName.Name}Module");
            _classMap = new Dictionary<string, Class>();
        }

        public string Name { get; private set; }

        [SuppressMessage("SonarAnalyzer.CSharp", "S2365", Justification = "Preserve the public array property and its independent snapshot semantics for existing consumers.")]
        public Class[] Classes
        {
            get
            {
                EnsureUsable();
                return _classMap.Values.ToArray();
            }
        }

        public Class DeclareClass<TEntity>(string name, Action<BindingBuilder<TEntity>> configure) where TEntity : class
        {
            EnsureUsable();
            if (_state != BuildState.Declaring) throw new InvalidOperationException("Classes can only be declared before building the assembly.");
            if (configure == null) throw new ArgumentNullException(nameof(configure));
            var builder = new BindingBuilder<TEntity>();
            configure(builder);
            return DeclareClass(name, typeof(TEntity), builder.Bindings);
        }

        public Class DeclareClass(string name, Type mappingType, IEnumerable<PropertyBinding> bindings)
        {
            EnsureUsable();
            if (_state != BuildState.Declaring) throw new InvalidOperationException("Classes can only be declared before building the assembly.");
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException($"{nameof(name)} is required", nameof(name));
            if (mappingType == null) throw new ArgumentNullException(nameof(mappingType));
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            if (!mappingType.IsClass || !mappingType.IsVisible || mappingType.ContainsGenericParameters)
                throw new ArgumentException("The mapping type must be a visible, closed reference class.", nameof(mappingType));

            var normalizedBindings = NormalizeBindings(bindings);
            if (_classMap.TryGetValue(name, out var existing))
            {
                if (!existing.Matches(mappingType, normalizedBindings))
                    throw new ArgumentException($"Class '{name}' is already declared with different mappings.", nameof(name));
                return existing;
            }

            var typeBuilder = _moduleBuilder.DefineType($"{name}", TypeAttributes.Public, null, new Type[] { typeof(IDtoObject) });
            var cls = new Class(typeBuilder, name, this, mappingType, normalizedBindings);
            _classMap.Add(name, cls);

            return cls;
        }

        internal Class DeclareClass(string name)
        {
            EnsureUsable();
            if (_classMap.TryGetValue(name, out var existing))
            {
                return existing;
            }
            var typeBuilder = _moduleBuilder.DefineType($"{name}", TypeAttributes.Public);
            var cls = new Class(typeBuilder, name, this, null, null);
            _classMap.Add(name, cls);

            return cls;
        }

        public void Build()
        {
            EnsureUsable();
            if (_state == BuildState.Built) return;
            if (_state == BuildState.Building) throw new InvalidOperationException("The assembly is already being built.");
            _state = BuildState.Building;
            try
            {
                var report = Validate();
                if (!report.IsValid) throw new MappingValidationException(report);
                foreach (var cls in _classMap.Values.ToArray()) cls.ResolveBindings();
                for (var i = 0; i < _derivedClasses.Count; i++) _derivedClasses[i].ResolveBindings();
                foreach (var cls in AllClasses().ToArray()) cls.BuildProperties();
                foreach (var cls in AllClasses().ToArray()) cls.BuildMethods();
                foreach (var cls in AllClasses().ToArray()) cls.BuildType();
                foreach (var cls in AllClasses()) cls.InitializeTransformations();
                foreach (var cls in AllClasses()) cls.RegisterPartialUpdates();
                var metadataPublication = DtoMetadataBuilder.Stage(
                    AllClasses().Where(c => c.MappingType != null).ToArray(),
                    _classMap.Values.Where(c => c.MappingType != null).ToArray());
                foreach (var cls in AllClasses()) cls.Clear();
                _state = BuildState.Built;
                metadataPublication.Publish();
            }
            catch (Exception error)
            {
                // Emitted metadata cannot be rolled back; never reuse a partially built assembly.
                _buildError = error;
                _state = BuildState.Faulted;
                throw;
            }
        }

        internal void EnsureUsable()
        {
            if (_state == BuildState.Disposed) throw new ObjectDisposedException(nameof(DynamicAssembly));
            if (_state == BuildState.Faulted)
                throw new InvalidOperationException("Building this assembly failed. Create a new DynamicAssembly.", _buildError);
        }

        private static PropertyBinding[] NormalizeBindings(IEnumerable<PropertyBinding> bindings)
        {
            var result = BindingValidation.Snapshot(bindings);
            var error = BindingValidation.Syntax(null, result).FirstOrDefault();
            if (error != null) throw new ArgumentException(error.Message, nameof(bindings));
            return result.OrderBy(p => p.DtoProperty, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public MappingValidationReport Validate()
        {
            EnsureUsable();
            var diagnostics = new List<MappingDiagnostic>();
            foreach (var cls in _classMap.Values) cls.ValidateBindings(diagnostics, ResolveAlternative);
            return new MappingValidationReport(diagnostics);
        }

        public MappingValidationReport ValidateClass(string name, Type mappingType, IEnumerable<PropertyBinding> bindings)
        {
            EnsureUsable();
            var diagnostics = new List<MappingDiagnostic>();
            var snapshot = bindings == null ? null : BindingValidation.Snapshot(bindings);
            if (string.IsNullOrWhiteSpace(name))
                diagnostics.Add(new MappingDiagnostic("ClassName", name, null, "Configuration", "A class name is required.", "Provide a non-empty class name."));
            if (mappingType == null || !mappingType.IsClass || !mappingType.IsVisible || mappingType.ContainsGenericParameters)
                diagnostics.Add(new MappingDiagnostic("EntityType", name, null, "Configuration", "The mapping type must be a visible, closed reference class.", "Choose a public, closed entity class."));
            if (snapshot == null)
                diagnostics.Add(new MappingDiagnostic("Bindings", name, null, "Configuration", "Bindings are required.", "Provide a binding sequence."));
            else
            {
                diagnostics.AddRange(BindingValidation.Syntax(name, snapshot));
                if (name != null && _classMap.TryGetValue(name, out var existing)
                    && (snapshot.Any(b => b == null) || !existing.Matches(mappingType, snapshot.OrderBy(b => b.DtoProperty, StringComparer.OrdinalIgnoreCase).ToArray())))
                    diagnostics.Add(new MappingDiagnostic("ClassConflict", name, null, "Configuration", "The name is already declared with different mappings.", "Use a different class name or equivalent bindings."));
                if (mappingType != null && mappingType.IsClass && mappingType.IsVisible && !mappingType.ContainsGenericParameters)
                    BindingValidation.Members(name, mappingType, snapshot,
                        (type, binding) => ResolveCandidateAlternative(type, binding, name, mappingType), diagnostics);
            }
            foreach (var cls in _classMap.Values) cls.ValidateBindings(diagnostics, ResolveAlternative);
            return new MappingValidationReport(diagnostics);
        }

        internal AlternationInfo ResolveAlternative(Type type, PropertyBinding binding)
        {
            if (binding.IsTransformed) return new AlternationInfo { Type = binding.DtoValueType ?? type };
            return Class.ResolveAlternative(type, candidate =>
            {
                if (binding.NestedShape != null)
                {
                    if (!_classMap.TryGetValue(binding.NestedShape, out var selected))
                        throw new InvalidOperationException($"Nested shape '{binding.NestedShape}' does not exist.");
                    if (selected.MappingType != candidate)
                        throw new InvalidOperationException($"Nested shape '{binding.NestedShape}' does not map '{candidate.FullName}'.");
                    return selected;
                }

                var matches = _classMap.Values.Where(c => c.MappingType == candidate).ToArray();
                if (matches.Length > 1)
                    throw new InvalidOperationException($"Nested type '{candidate.FullName}' has multiple shapes ({string.Join(", ", matches.Select(c => c.Name))}). Specify NestedShape.");
                return matches.SingleOrDefault();
            });
        }

        private AlternationInfo ResolveCandidateAlternative(Type type, PropertyBinding binding, string name, Type mappingType)
        {
            if (binding.IsTransformed) return new AlternationInfo { Type = binding.DtoValueType ?? type };
            var elementType = type.IsArray ? type.GetElementType()
                : Class.IsSupportedCollection(type) ? type.GetGenericArguments()[0] : type;
            var matchesCandidate = elementType == mappingType;
            var alreadyDeclared = name != null && _classMap.ContainsKey(name);
            if (binding.NestedShape != null && binding.NestedShape != name)
                return ResolveAlternative(type, binding);
            if (binding.NestedShape == name && name != null && !matchesCandidate)
                throw new InvalidOperationException($"Nested shape '{name}' does not map '{elementType.FullName}'.");
            if (!matchesCandidate || alreadyDeclared) return ResolveAlternative(type, binding);
            if (binding.NestedShape == null && _classMap.Values.Any(c => c.MappingType == elementType))
                throw new InvalidOperationException($"Nested type '{elementType.FullName}' has multiple shapes including candidate '{name}'. Specify NestedShape.");
            if (type.IsArray && (type.GetArrayRank() != 1 || type != elementType.MakeArrayType()))
                throw new InvalidOperationException("Mapped arrays must be single-dimensional, zero-based arrays.");
            return new AlternationInfo
            {
                Type = type,
                EntityElementType = elementType,
                PropertyType = type.IsArray ? SpecialPropertyType.Array
                    : Class.IsSupportedCollection(type) ? SpecialPropertyType.Collection : SpecialPropertyType.Alternative
            };
        }

        internal Class DeriveShape(Class baseShape, string[] overlayPaths)
        {
            var name = $"__{baseShape.Name}_{Guid.NewGuid():N}";
            var builder = _moduleBuilder.DefineType(name, TypeAttributes.Public, null, new[] { typeof(IDtoObject) });
            var derived = new Class(builder, name, this, baseShape.MappingType, BindingValidation.Snapshot(baseShape.Bindings),
                overlayPaths, baseShape.DeclaredShapeName);
            _derivedClasses.Add(derived);
            return derived;
        }

        private IEnumerable<Class> AllClasses() => _classMap.Values.Concat(_derivedClasses);

        void IDisposable.Dispose()
        {
            if (_state == BuildState.Disposed) return;
            foreach (IDisposable cls in AllClasses())
            {
                cls.Dispose();
            }

            _classMap.Clear();
            _derivedClasses.Clear();
            _state = BuildState.Disposed;
        }
    }
}
