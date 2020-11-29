using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Kkts.Dynamic
{
    public sealed class DynamicAssembly : IDisposable
    {
        private readonly AssemblyName _assemblyName;
        private readonly ModuleBuilder _moduleBuilder;
        private readonly Dictionary<string, Class> _classMap;
        private bool _isBuilt;

        public DynamicAssembly() : this(Guid.NewGuid().ToString()) { }

        public DynamicAssembly(string name)
        {
            Name = !string.IsNullOrWhiteSpace(name) ? name : throw new ArgumentException($"{nameof(name)} is required", nameof(name));
            _assemblyName = new AssemblyName();
            _assemblyName.Name = $"__{Name}_Dynamic_Assembly__";
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(_assemblyName, AssemblyBuilderAccess.RunAndCollect);
            _moduleBuilder = assemblyBuilder.DefineDynamicModule($"{_assemblyName.Name}Module");
            _classMap = new Dictionary<string, Class>();
        }

        public string Name { get; private set; }

        public Class[] Classes => _classMap.Values.ToArray();

        public Class DeclareClass(string name, Type mappingType, IEnumerable<PropertyBinding> bindings)
        {
            if (_isBuilt) throw new InvalidOperationException("This assembly is already built. You can not declare class any more");
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException($"{nameof(name)} is required", nameof(name));
            if (mappingType == null) throw new ArgumentNullException(nameof(mappingType));
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            if (_classMap.ContainsKey(name))
            {
                return _classMap[name];
            }

            bindings = bindings.Select(p => new PropertyBinding { DtoProperty = p.DtoProperty, EntityProperty = p.EntityProperty, IsPrimaryKey = p.IsPrimaryKey, Mode = p.Mode, PrimaryKeyOrder = p.PrimaryKeyOrder }).OrderBy(p => p.DtoProperty).ToArray();
            var typeBuilder = _moduleBuilder.DefineType($"{name}", TypeAttributes.Public, null, new Type[] { typeof(IDtoObject) });
            var cls = new Class(typeBuilder, name, this, mappingType, bindings);
            _classMap.Add(name, cls);

            return cls;
        }

        internal Class DeclareClass(string name)
        {
            if (_classMap.ContainsKey(name))
            {
                return _classMap[name];
            }
            var typeBuilder = _moduleBuilder.DefineType($"{name}", TypeAttributes.Public);
            var cls = new Class(typeBuilder, name, this, null, null);
            _classMap.Add(name, cls);

            return cls;
        }

        public void Build()
        {
            if (_isBuilt) return;
            _isBuilt = true;
            foreach(var cls in _classMap.Values.ToArray())
            {
                cls.BuildProperties();
            }

            foreach (var cls in _classMap.Values.ToArray())
            {
                cls.BuildMethods();
            }

            foreach (var cls in _classMap.Values.ToArray())
            {
                cls.BuildType();
                cls.Clear();
            }
        }

        void IDisposable.Dispose()
        {
            foreach (IDisposable cls in _classMap.Values)
            {
                cls.Dispose();
            }

            _classMap.Clear();
        }
    }
}
