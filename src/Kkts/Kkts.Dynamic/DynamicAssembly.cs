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
                foreach (var cls in _classMap.Values.ToArray()) cls.ValidateBindings();
                foreach (var cls in _classMap.Values.ToArray()) cls.BuildProperties();
                foreach (var cls in _classMap.Values.ToArray()) cls.BuildMethods();
                foreach (var cls in _classMap.Values.ToArray()) cls.BuildType();
                foreach (var cls in _classMap.Values) cls.Clear();
                _state = BuildState.Built;
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
            var result = new List<PropertyBinding>();
            var dtoPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entityWritePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var binding in bindings)
            {
                if (binding == null) throw new ArgumentException("Bindings cannot contain null entries.", nameof(bindings));
                var dtoProperty = binding.DtoProperty ?? binding.EntityProperty;
                ValidatePath(binding.EntityProperty, nameof(binding.EntityProperty));
                ValidatePath(dtoProperty, nameof(binding.DtoProperty));
                if (!Enum.IsDefined(typeof(BindingMode), binding.Mode))
                    throw new ArgumentException($"Invalid binding mode: {binding.Mode}.", nameof(bindings));
                if (!dtoPaths.Add(dtoProperty))
                    throw new ArgumentException($"Duplicate DTO path '{dtoProperty}'.", nameof(bindings));
                if (binding.Mode != BindingMode.OneWayToDto && !entityWritePaths.Add(binding.EntityProperty))
                    throw new ArgumentException($"Multiple bindings write entity path '{binding.EntityProperty}'.", nameof(bindings));
                if (binding.IsPrimaryKey && (binding.EntityProperty.IndexOf('.') >= 0 || dtoProperty.IndexOf('.') >= 0))
                    throw new ArgumentException("Primary keys must use top-level entity and DTO properties.", nameof(bindings));
                result.Add(new PropertyBinding
                {
                    DtoProperty = dtoProperty, EntityProperty = binding.EntityProperty,
                    IsPrimaryKey = binding.IsPrimaryKey, Mode = binding.Mode,
                    PrimaryKeyOrder = binding.PrimaryKeyOrder
                });
            }
            return result.OrderBy(p => p.DtoProperty, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static void ValidatePath(string path, string name)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Split('.').Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("Property paths must contain non-empty segments.", name);
        }

        void IDisposable.Dispose()
        {
            if (_state == BuildState.Disposed) return;
            foreach (IDisposable cls in _classMap.Values)
            {
                cls.Dispose();
            }

            _classMap.Clear();
            _state = BuildState.Disposed;
        }
    }
}
