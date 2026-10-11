using Kkts.Dynamic.Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace Kkts.Dynamic
{
    public sealed class Class : IDisposable
    {
        [SuppressMessage("SonarAnalyzer.CSharp", "S1104", Justification = "Changing the existing public field to a property would break binary compatibility.")]
        [SuppressMessage("SonarAnalyzer.CSharp", "S2223", Justification = "Existing consumers configure selector recursion through this mutable public field.")]
        public static uint MaxRecursionDepthToBuildSelectorExpression = 4;
        public const string InjectFromMethodName = "__Inject_From__";
        public const string InjectToMethodName = "__Inject_To__";
        public const string GetIdsMethodName = "__Get_Ids__";
        private readonly TypeBuilder _typeBuilder;
        private readonly ConstructorBuilder _ctorBuilder;
        private readonly Dictionary<string, PropertyBuilder> _propertyBuilders;
        private readonly Dictionary<string, Class> _subClasses;
        private readonly Dictionary<string, Class> _alternativeProperties;
        private readonly Type _mappingType;
        private readonly MemberTree _mappingTypeMember;
        private DynamicAssembly _assembly;
        private readonly IEnumerable<PropertyBinding> _bindings;
        private readonly bool _isInternal;
        private bool _isBuiltProperties;
        private bool _isBuiltMethods;
        private Type _type;
        private LambdaExpression _selector;
        private bool _disposed;
        private readonly string[] _overlayPaths;
        private FieldBuilder _forwardTransformations;
        private FieldBuilder _reverseTransformations;

        internal Class(TypeBuilder typeBuilder, string name, DynamicAssembly assembly, Type mappingType, IEnumerable<PropertyBinding> bindings, string[] overlayPaths = null)
        {
            _typeBuilder = typeBuilder;
            Name = name;
            _assembly = assembly;
            _mappingType = mappingType;
            _bindings = bindings;
            _overlayPaths = overlayPaths ?? Array.Empty<string>();
            _isInternal = mappingType == null;
            _mappingTypeMember = new MemberTree(_mappingType);
            _propertyBuilders = new Dictionary<string, PropertyBuilder>(StringComparer.OrdinalIgnoreCase);
            _subClasses = new Dictionary<string, Class>(StringComparer.OrdinalIgnoreCase);
            _alternativeProperties = new Dictionary<string, Class>(StringComparer.OrdinalIgnoreCase);
            _ctorBuilder = DeclareConstructor();
        }

        public string Name { get; private set; }

        public DynamicAssembly Assembly => _assembly;

        internal ConstructorBuilder Constructor => _ctorBuilder;

        internal Dictionary<string, Class> SubClasses => _subClasses;

        internal Dictionary<string, PropertyBuilder> PropertyBuilders => _propertyBuilders;

        internal TypeBuilder TypeBuilder => _typeBuilder;

        internal IEnumerable<PropertyBinding> Bindings => _bindings;
        internal Type MappingType => _mappingType;
        internal void RegisterPartialUpdates()
        {
            if (!_isInternal) Mapper.RegisterPartialUpdates(this);
        }
        internal bool HasAlternativeProperty(string name) => _alternativeProperties.ContainsKey(name);

        public Type GetBuiltType()
        {
            EnsureUsable();
            return BuildType();
        }

        public object CreateDto(object entity)
        {
            EnsureUsable();
            return Mapper.CreateMappedDto(GetBuiltType(), entity);
        }

        private void EnsureUsable()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Class));
            _assembly?.EnsureUsable();
        }

        internal bool Matches(Type mappingType, PropertyBinding[] bindings)
        {
            return _mappingType == mappingType && _bindings.Count() == bindings.Length
                && _bindings.Zip(bindings, (left, right) =>
                    left.EntityProperty == right.EntityProperty && left.DtoProperty == right.DtoProperty
                    && left.Mode == right.Mode && left.IsPrimaryKey == right.IsPrimaryKey
                    && left.PrimaryKeyOrder == right.PrimaryKeyOrder
                    && left.NestedShape == right.NestedShape
                    && left.DtoValueType == right.DtoValueType
                    && left.ToDtoExpression == right.ToDtoExpression && left.ToEntityExpression == right.ToEntityExpression
                    && ReferenceEquals(left.ToDtoInMemory, right.ToDtoInMemory) && ReferenceEquals(left.ToEntityInMemory, right.ToEntityInMemory)
                    && left.ComputedExpression == right.ComputedExpression && ReferenceEquals(left.ComputedInMemory, right.ComputedInMemory)).All(equal => equal);
        }

        internal void ValidateBindings(List<MappingDiagnostic> diagnostics, Func<Type, PropertyBinding, AlternationInfo> resolveAlternative)
        {
            if (_isInternal) return;
            BindingValidation.Members(Name, _mappingType, _bindings.ToArray(), resolveAlternative, diagnostics);
        }

        internal void Clear()
        {
            _assembly = null;
            _subClasses.Clear();
            _alternativeProperties.Clear();
            _propertyBuilders.Clear();

        }

        internal Type BuildType()
        {
            if (!_isInternal && !_isBuiltProperties) throw new InvalidOperationException("The class does not build yet, please call DynamicAssembly.Build");
            if (_type != null) return _type;
            _type = _typeBuilder.CreateTypeInfo();
            foreach (var cls in _subClasses)
            {
                cls.Value._typeBuilder.CreateTypeInfo();
            }

            return _type;
        }

        public LambdaExpression BuildSelectorExpression()
        {
            EnsureUsable();
            if (_type == null) throw new InvalidOperationException($"Should call BuildType before calling {nameof(BuildSelectorExpression)}");
            if (_selector != null) return _selector;
            ValidateProjection(new HashSet<Class>(), "");
            var param = Expression.Parameter(_mappingType, "p");
            using (var bindingTree = new SelectMemberBindingExpressionTree(param, _type))
            {
                bindingTree.Bind(_bindings);
                var body = bindingTree.Build();
                _selector = Expression.Lambda(body, param);

                return _selector;
            }
        }

        private void ValidateProjection(HashSet<Class> visited, string prefix)
        {
            if (!visited.Add(this)) return;
            foreach (var binding in _bindings)
            {
                if (binding.Mode == BindingMode.OneWayToEntity) continue;
                var path = prefix + binding.DtoProperty;
                if ((binding.IsComputed && binding.ComputedExpression == null && binding.ComputedInMemory != null)
                    || (!binding.IsComputed && binding.ToDtoExpression == null && binding.ToDtoInMemory != null))
                    throw new InvalidOperationException($"Shape '{Name}', DTO path '{path}' has an in-memory-only transformation and cannot be projected.");
                binding.Alternation?.Cls?.ValidateProjection(visited, path + ".");
            }
        }

        internal void BuildProperties()
        {
            if (_isInternal || _isBuiltProperties) return;
            _isBuiltProperties = true;
            foreach (var binding in _bindings)
            {
                var memberInfo = binding.IsComputed ? null : _mappingTypeMember.PropertyOrField(binding.EntityProperty);
                var alternation = binding.Alternation ?? (binding.IsComputed
                    ? new AlternationInfo { Type = binding.DtoValueType }
                    : FindAlternativeType(memberInfo.PropertyOrFieldType, binding));
                binding.Alternation = alternation;
                if (alternation.Cls != null) alternation.Cls.BuildProperties();
                var propType = alternation.Type;
                if (alternation.PropertyType == SpecialPropertyType.Array)
                {
                    propType = alternation.Type.MakeArrayType();
                }
                else if (alternation.PropertyType == SpecialPropertyType.Collection)
                {
                    propType = typeof(IEnumerable<>).MakeGenericType(alternation.Type);
                }

                var attributesData = binding.Mode != BindingMode.OneWayToDto && memberInfo != null
                    && propType == memberInfo.PropertyOrFieldType ? memberInfo.GetCustomAttributesData() : null;

                DeclareProperty(binding.DtoProperty, propType, attributesData, binding.Alternation.PropertyType == SpecialPropertyType.Alternative, alternation.Cls);
            }
        }

        internal void BuildMethods()
        {
            if (_isInternal || _isBuiltMethods) return;
            _isBuiltMethods = true;
            var transformationType = typeof(Action<,>).MakeGenericType(_typeBuilder, _mappingType);
            if (_bindings.Any(b => b.IsTransformed && b.Mode != BindingMode.OneWayToEntity))
                _forwardTransformations = _typeBuilder.DefineField("__ForwardTransformations", transformationType, FieldAttributes.Private | FieldAttributes.Static);
            if (_bindings.Any(b => b.IsTransformed && b.Mode != BindingMode.OneWayToDto))
                _reverseTransformations = _typeBuilder.DefineField("__ReverseTransformations", transformationType, FieldAttributes.Private | FieldAttributes.Static);
            using (var injectFromBinding = new InjectFromMemberBindingTree(this, _mappingType, _mappingTypeMember))
            {
                injectFromBinding.Bind(_bindings.Where(b => !b.IsTransformed));
                DeclareInjectFromMethod(injectFromBinding);
            }

            using (var injectToBinding = new InjectToMemberBindingTree(this, _mappingType, _mappingTypeMember))
            {
                injectToBinding.Bind(_bindings.Where(b => !b.IsTransformed));
                DeclareInjectToMethod(injectToBinding);
            }

            DeclareGetPrimaryKeysMethod();
        }

        private AlternationInfo FindAlternativeType(Type propType, PropertyBinding binding)
        {
            return _assembly.ResolveAlternative(propType, binding);
        }

        internal static AlternationInfo ResolveAlternative(Type propType, Func<Type, Class> findClass)
        {
            var result = new AlternationInfo();
            Class cls;
            if (propType.IsArray)
            {
                var elementType = propType.GetElementType();
                cls = findClass(elementType);
                if (cls != null)
                {
                    if (propType.GetArrayRank() != 1 || propType != elementType.MakeArrayType())
                        throw new InvalidOperationException("Mapped arrays must be single-dimensional, zero-based arrays.");
                    result.PropertyType = SpecialPropertyType.Array;
                    result.Type = cls._typeBuilder;
                    result.Cls = cls;
                    result.AlternativeCtor = cls.Constructor;
                    result.EntityElementType = elementType;
                    return result;
                }
            }

            if (propType.IsGenericType)
            {
                var elementType = propType.GetGenericArguments()[0];
                if (IsSupportedCollection(propType))
                {
                    cls = findClass(elementType);
                    if (cls != null)
                    {
                        result.PropertyType = SpecialPropertyType.Collection;
                        result.Type = cls._typeBuilder;
                        result.Cls = cls;
                        result.AlternativeCtor = cls.Constructor;
                        result.EntityElementType = elementType;
                        return result;
                    }
                }
            }

            cls = findClass(propType);

            if (cls != null)
            {
                result.PropertyType = SpecialPropertyType.Alternative;
                result.Type = cls._typeBuilder;
                result.Cls = cls;
                result.AlternativeCtor = cls.Constructor;
            }
            else
            {
                result.Type = propType;
            }

            return result;
        }

        internal static bool IsSupportedCollection(Type type)
        {
            if (!type.IsGenericType) return false;
            var definition = type.GetGenericTypeDefinition();
            return definition == typeof(List<>) || definition == typeof(IList<>)
                || definition == typeof(ICollection<>) || definition == typeof(IEnumerable<>);
        }

        internal void ResolveBindings()
        {
            if (_isInternal) return;
            foreach (var binding in _bindings)
            {
                var member = binding.IsComputed ? null : _mappingTypeMember.PropertyOrField(binding.EntityProperty);
                binding.Alternation = binding.IsComputed
                    ? new AlternationInfo { Type = binding.DtoValueType }
                    : FindAlternativeType(member.PropertyOrFieldType, binding);
                if (binding.Alternation.PropertyType != SpecialPropertyType.Alternative) continue;
                var paths = _bindings.Select(b => b.DtoProperty).Concat(_overlayPaths)
                    .Where(path => path.StartsWith(binding.DtoProperty + ".", StringComparison.OrdinalIgnoreCase))
                    .Select(path => path.Substring(binding.DtoProperty.Length + 1)).ToArray();
                if (paths.Length == 0) continue;
                var derived = _assembly.DeriveShape(binding.Alternation.Cls, paths);
                binding.Alternation.Cls = derived;
                binding.Alternation.Type = derived.TypeBuilder;
                binding.Alternation.AlternativeCtor = derived.Constructor;
            }
        }

        private void DeclareProperty(string name, Type propType, IList<CustomAttributeData> customAttributesData, bool isAlternativeType, Class alternativeCls)
        {
            if (_propertyBuilders.ContainsKey(name)) return;
            var segments = name.Split('.');

            if (segments.Length == 1)
            {
                AddProperty(name, propType, customAttributesData, isAlternativeType, alternativeCls);
                return;
            }

            DeclareNestedProperty(name, propType, customAttributesData, isAlternativeType, alternativeCls, segments);
        }

        private void AddProperty(string name, Type propType, IList<CustomAttributeData> attributes, bool isAlternativeType, Class alternativeCls)
        {
            var prop = DeclareAutoProperty(name, propType, _typeBuilder, attributes);
            _propertyBuilders.Add(name, prop);
            if (isAlternativeType) _alternativeProperties.Add(name, alternativeCls);
        }

        private void DeclareNestedProperty(string name, Type propType, IList<CustomAttributeData> attributes, bool isAlternativeType, Class alternativeCls, string[] segments)
        {
            var propName = string.Empty;
            var classSuffix = new StringBuilder();
            var currentCls = this;
            var ticks = DateTime.Now.Ticks;
            for (var i = 0; i < segments.Length; ++i)
            {
                var segment = segments[i];
                classSuffix.Append(segment);
                propName = propName == string.Empty ? segment : $"{propName}.{segment}";
                if (_subClasses.TryGetValue(propName, out var existing))
                {
                    currentCls = existing;
                    continue;
                }
                if (i == segments.Length - 1)
                {
                    if (currentCls._propertyBuilders.ContainsKey(segment)) break;
                    currentCls.AddProperty(segment, propType, attributes, isAlternativeType, alternativeCls);
                    currentCls._propertyBuilders.Add(name, currentCls._propertyBuilders[segment]);
                    break;
                }
                currentCls = GetOrDeclareNestedClass(currentCls, segment, propName, $"{Name}{classSuffix}{ticks}", i > 0);
            }
        }

        private Class GetOrDeclareNestedClass(Class parent, string segment, string path, string className, bool isNested)
        {
            if (parent._alternativeProperties.TryGetValue(segment, out var alternative))
            {
                if (parent != this) parent._subClasses.Add(segment, alternative);
                _subClasses.Add(path, alternative);
                return alternative;
            }
            var cls = _assembly.DeclareClass(className);
            _subClasses.Add(path, cls);
            if (isNested) parent._subClasses.Add(segment, cls);
            parent.AddProperty(segment, cls._typeBuilder, null, false, null);
            return cls;
        }

        private void DeclareInjectFromMethod(InjectFromMemberBindingTree memberBindings)
        {
            var attr = MethodAttributes.Public
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig;
            var methodGetBuilder = _typeBuilder.DefineMethod(InjectFromMethodName, attr, null, new[] { _mappingType });
            var methodIL = methodGetBuilder.GetILGenerator();
            var ret = methodIL.DefineLabel();
            var endIf = methodIL.DefineLabel();

            methodIL.Emit(OpCodes.Nop);
            methodIL.Emit(OpCodes.Ldarg_1);
            methodIL.Emit(OpCodes.Ldnull);
            methodIL.Emit(OpCodes.Ceq);
            methodIL.Emit(OpCodes.Brfalse, endIf);
            methodIL.Emit(OpCodes.Br, ret);
            methodIL.MarkLabel(endIf);

            memberBindings.Build(methodIL);
            EmitTransformations(methodIL, _forwardTransformations);

            methodIL.MarkLabel(ret);
            methodIL.Emit(OpCodes.Ret);
        }

        private void DeclareInjectToMethod(InjectToMemberBindingTree memberBindings)
        {
            var attr = MethodAttributes.Public
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig;
            var methodGetBuilder = _typeBuilder.DefineMethod(InjectToMethodName, attr, null, new[] { _mappingType });
            var methodIL = methodGetBuilder.GetILGenerator();
            methodIL.DeclareLocal(typeof(object));
            var ret = methodIL.DefineLabel();
            var endIf = methodIL.DefineLabel();

            methodIL.Emit(OpCodes.Nop);
            methodIL.Emit(OpCodes.Ldarg_1);
            methodIL.Emit(OpCodes.Ldnull);
            methodIL.Emit(OpCodes.Ceq);
            methodIL.Emit(OpCodes.Brfalse, endIf);
            methodIL.Emit(OpCodes.Br, ret);
            methodIL.MarkLabel(endIf);

            memberBindings.Build(methodIL);
            EmitTransformations(methodIL, _reverseTransformations);

            methodIL.MarkLabel(ret);
            methodIL.Emit(OpCodes.Ret);
        }

        private static void EmitTransformations(ILGenerator generator, FieldBuilder field)
        {
            if (field == null) return;
            generator.Emit(OpCodes.Ldsfld, field);
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldarg_1);
            generator.Emit(OpCodes.Callvirt, TypeBuilder.GetMethod(field.FieldType, typeof(Action<,>).GetMethod("Invoke")));
        }

        internal void InitializeTransformations()
        {
            if (_forwardTransformations != null) Initialize(_forwardTransformations, true);
            if (_reverseTransformations != null) Initialize(_reverseTransformations, false);

            void Initialize(FieldBuilder field, bool forward)
            {
                var dto = Expression.Parameter(_type, "dto");
                var entity = Expression.Parameter(_mappingType, "entity");
                var operations = new List<Expression>();
                using (var sourceTree = new SelectSourceMemberExpressionTree(forward ? entity : dto))
                {
                    foreach (var binding in _bindings.Where(b => b.IsTransformed
                        && (forward ? b.Mode != BindingMode.OneWayToEntity : b.Mode != BindingMode.OneWayToDto)))
                    {
                        var source = binding.IsComputed ? null : sourceTree.Property(forward ? binding.EntityProperty : binding.DtoProperty);
                        var value = BindingExpressions.Transform(binding, source?.MemberExpression ?? (Expression)entity, forward, false);
                        var targetPath = forward ? binding.DtoProperty : binding.EntityProperty;
                        Func<string, bool> preserve = forward
                            ? path => IsMappedDtoParent(path) : (Func<string, bool>)null;
                        var write = BindingExpressions.Write(forward ? dto : entity, targetPath, value, preserve);
                        operations.Add(source == null ? write : Expression.IfThen(source.Guard, write));
                    }
                }
                var callback = Expression.Lambda(typeof(Action<,>).MakeGenericType(_type, _mappingType),
                    Expression.Block(operations), dto, entity).Compile();
                _type.GetField(field.Name, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, callback);
            }
        }

        private bool IsMappedDtoParent(string path)
        {
            return _bindings.Any(b => b.Alternation?.PropertyType == SpecialPropertyType.Alternative
                && (path.Equals(b.DtoProperty, StringComparison.OrdinalIgnoreCase)
                    || (path.StartsWith(b.DtoProperty + ".", StringComparison.OrdinalIgnoreCase)
                        && b.Alternation.Cls.IsMappedDtoParent(path.Substring(b.DtoProperty.Length + 1)))));
        }

        private static PropertyBuilder DeclareAutoProperty(string name, Type propType, TypeBuilder typeBuilder, IList<CustomAttributeData> customAttributesData = null)
        {
            var fieldBuider = typeBuilder.DefineField($"{name}k__BackingField", propType, FieldAttributes.Private);
            var propertyBuilder = typeBuilder.DefineProperty(name, PropertyAttributes.HasDefault, propType, null);
            var getSetAttr = MethodAttributes.Public
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig;
            var methodGetBuilder = typeBuilder.DefineMethod($"get_{name}", getSetAttr, propType, Type.EmptyTypes);
            var methodGetIL = methodGetBuilder.GetILGenerator();
            methodGetIL.Emit(OpCodes.Ldarg_0);
            methodGetIL.Emit(OpCodes.Ldfld, fieldBuider);
            methodGetIL.Emit(OpCodes.Ret);
            var methodSetBuilder = typeBuilder.DefineMethod($"set_{name}", getSetAttr, null, new Type[] { propType });
            var methodSetIL = methodSetBuilder.GetILGenerator();
            methodSetIL.Emit(OpCodes.Ldarg_0);
            methodSetIL.Emit(OpCodes.Ldarg_1);
            methodSetIL.Emit(OpCodes.Stfld, fieldBuider);
            methodSetIL.Emit(OpCodes.Ret);
            propertyBuilder.SetGetMethod(methodGetBuilder);
            propertyBuilder.SetSetMethod(methodSetBuilder);

            if (customAttributesData == null) return propertyBuilder;

            foreach (var data in customAttributesData)
            {
                var namedProps = data.NamedArguments.Where(p => !p.IsField);
                var namedFields = data.NamedArguments.Where(p => p.IsField);
                var propInfos = namedProps.Select(p => (PropertyInfo)p.MemberInfo).ToArray();
                var propValues = namedProps.Select(p => ConvertAttributeArgument(p.TypedValue)).ToArray();
                var fieldInfos = namedFields.Select(p => (FieldInfo)p.MemberInfo).ToArray();
                var fieldValues = namedFields.Select(p => ConvertAttributeArgument(p.TypedValue)).ToArray();
                var attributeBuilder = new CustomAttributeBuilder(data.Constructor,
                    data.ConstructorArguments.Select(ConvertAttributeArgument).ToArray(),
                    propInfos,
                    propValues,
                    fieldInfos,
                    fieldValues);
                propertyBuilder.SetCustomAttribute(attributeBuilder);
            }

            return propertyBuilder;
        }

        private static object ConvertAttributeArgument(CustomAttributeTypedArgument argument)
        {
            if (argument.Value == null) return null;
            if (argument.Value is CustomAttributeTypedArgument boxed)
                return ConvertAttributeArgument(boxed);
            if (argument.ArgumentType.IsArray)
            {
                var elements = (IList<CustomAttributeTypedArgument>)argument.Value;
                var array = Array.CreateInstance(argument.ArgumentType.GetElementType(), elements.Count);
                for (var i = 0; i < elements.Count; i++)
                    array.SetValue(ConvertAttributeArgument(elements[i]), i);
                return array;
            }
            if (argument.ArgumentType.IsEnum)
                return Enum.ToObject(argument.ArgumentType, argument.Value);
            return argument.Value;
        }

        private ConstructorBuilder DeclareConstructor()
        {
            var ctorBuilder = _typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
            var ilGenerator = ctorBuilder.GetILGenerator();
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes));
            ilGenerator.Emit(OpCodes.Nop);
            ilGenerator.Emit(OpCodes.Ret);

            return ctorBuilder;
        }

        private void DeclareGetPrimaryKeysMethod()
        {
            var primaryKeyProperties = _bindings.Where(p => p.IsPrimaryKey).OrderBy(p => p.PrimaryKeyOrder).ToArray();
            var noOfKey = primaryKeyProperties.Length;
            var attr = MethodAttributes.Public
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig;
            var methodGetBuilder = _typeBuilder.DefineMethod(GetIdsMethodName, attr, typeof(object[]), Type.EmptyTypes);
            var methodIL = methodGetBuilder.GetILGenerator();
            methodIL.DeclareLocal(typeof(object[]));
            methodIL.Emit(OpCodes.Nop);
            var ldloc = methodIL.DefineLabel();
            if (noOfKey == 0)
            {
                methodIL.Emit(OpCodes.Ldc_I4_0);
                methodIL.Emit(OpCodes.Newarr, typeof(object));
            }
            else
            {
                methodIL.Emit(OpCodes.Ldc_I4, noOfKey);
                methodIL.Emit(OpCodes.Newarr, typeof(object));
                for (var i = 0; i < noOfKey; ++i)
                {
                    var binding = primaryKeyProperties[i];
                    if (binding.EntityProperty.IndexOf('.') != -1 || binding.DtoProperty.IndexOf('.') != -1)
                    {
                        throw new InvalidOperationException($"The binding \"{binding.EntityProperty}\" and \"{binding.DtoProperty} of {_mappingType.FullName} is not valid for primary key");
                    }
                    var property = _propertyBuilders[binding.DtoProperty];
                    methodIL.Emit(OpCodes.Dup);
                    methodIL.Emit(OpCodes.Ldc_I4, i);
                    methodIL.Emit(OpCodes.Ldarg_0);
                    methodIL.Emit(OpCodes.Call, property.GetMethod);
                    if (ShouldBox(property.PropertyType))
                    {
                        methodIL.Emit(OpCodes.Box, property.PropertyType);
                    }
                    methodIL.Emit(OpCodes.Stelem_Ref);
                }
            }
            methodIL.Emit(OpCodes.Stloc_0);
            methodIL.Emit(OpCodes.Br, ldloc);
            methodIL.MarkLabel(ldloc);
            methodIL.Emit(OpCodes.Ldloc_0);
            methodIL.Emit(OpCodes.Ret);
        }

        private static bool ShouldBox(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            return type.IsEnum || type.IsValueType;
        }

        void IDisposable.Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Clear();
            if (_bindings != null)
            {
                foreach (var binding in _bindings)
                {
                    if (binding.Alternation != null)
                    {
                        binding.Alternation.Cls = null;
                        binding.Alternation = null;
                    }
                }
            }
        }
    }
}
