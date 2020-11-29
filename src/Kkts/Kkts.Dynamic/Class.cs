using Kkts.Dynamic.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;

namespace Kkts.Dynamic
{
    public sealed class Class : IDisposable
    {
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

        internal Class(TypeBuilder typeBuilder, string name, DynamicAssembly assembly, Type mappingType, IEnumerable<PropertyBinding> bindings)
        {
            _typeBuilder = typeBuilder;
            Name = name;
            _assembly = assembly;
            _mappingType = mappingType;
            _bindings = bindings;
            _isInternal = mappingType == null;
            _mappingTypeMember = new MemberTree(_mappingType);
            _propertyBuilders = new Dictionary<string, PropertyBuilder>();
            _subClasses = new Dictionary<string, Class>();
            _alternativeProperties = new Dictionary<string, Class>();
            _ctorBuilder = DeclareConstructor();
        }

        public string Name { get; private set; }

        public DynamicAssembly Assembly => _assembly;

        internal ConstructorBuilder Constructor => _ctorBuilder;

        internal Dictionary<string, Class> SubClasses => _subClasses;

        internal Dictionary<string, PropertyBuilder> PropertyBuilders => _propertyBuilders;

        internal TypeBuilder TypeBuilder => _typeBuilder;

        public Type GetBuiltType()
        {
            return BuildType();
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
            if (_type == null) throw new InvalidOperationException($"Should call BuildType before calling {nameof(BuildSelectorExpression)}");
            if (_selector != null) return _selector;
            var param = Expression.Parameter(_mappingType, "p");
            var bindingTree = new SelectMemberBindingExpressionTree(param, _type);
            bindingTree.Bind(_bindings);
            var body = bindingTree.Build();
            _selector = Expression.Lambda(body, param);

            return _selector;
        }

        internal void BuildProperties()
        {
            if (_isInternal || _isBuiltProperties) return;
            _isBuiltProperties = true;
            foreach (var binding in _bindings)
            {
                var memberInfo = _mappingTypeMember.PropertyOrField(binding.EntityProperty);
                var alternation = FindAlternativeType(memberInfo.PropertyOrFieldType);
                binding.Alternation = alternation;
                var propType = alternation.Type;
                if (alternation.PropertyType == SpecialPropertyType.Array)
                {
                    propType = alternation.Type.MakeArrayType();
                }
                else if (alternation.PropertyType == SpecialPropertyType.Collection)
                {
                    propType = typeof(IEnumerable<>).MakeGenericType(alternation.Type);
                }

                var attributesData = binding.Mode != BindingMode.OneWayToDto ? memberInfo.GetCustomAttributesData() : null;

                DeclareProperty(binding.DtoProperty, propType, attributesData, binding.Alternation.PropertyType == SpecialPropertyType.Alternative, alternation.Cls);
            }
        }

        internal void BuildMethods()
        {
            if (_isInternal || _isBuiltMethods) return;
            _isBuiltMethods = true;
            using (var injectFromBinding = new InjectFromMemberBindingTree(this, _mappingType, _mappingTypeMember))
            {
                injectFromBinding.Bind(_bindings);
                DeclareInjectFromMethod(injectFromBinding);
            }

            using (var injectToBinding = new InjectToMemberBindingTree(this, _mappingType, _mappingTypeMember))
            {
                injectToBinding.Bind(_bindings);
                DeclareInjectToMethod(injectToBinding);
            }

            DeclareGetPrimaryKeysMethod();
        }

        private AlternationInfo FindAlternativeType(Type propType)
        {
            var result = new AlternationInfo();
            Class cls;
            if (propType.IsArray)
            {
                var elementType = propType.GetElementType();
                cls = FindClass(elementType);
                if (cls != null)
                {
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
                var enumerableType = typeof(IEnumerable<>).MakeGenericType(elementType);
                var collectionType = typeof(ICollection<>).MakeGenericType(elementType);
                var listType = typeof(IList<>).MakeGenericType(elementType);
                var listType2 = typeof(List<>).MakeGenericType(elementType);
                if (propType == listType2
                    || propType == listType
                    || propType == collectionType
                    || propType == enumerableType)
                {
                    cls = FindClass(elementType);
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

            cls = FindClass(propType);

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

        private Class FindClass(Type type)
        {
            foreach (var cls in _assembly.Classes)
            {
                if (cls._mappingType == type)
                {
                    return cls;
                }
            }

            return null;
        }

        private void DeclareProperty(string name, Type propType, IList<CustomAttributeData> customAttributesData, bool isAlternativeType, Class alternativeCls)
        {
            if (_propertyBuilders.ContainsKey(name)) return;
            var segments = name.Split('.');

            if (segments.Length == 1)
            {
                if (!_propertyBuilders.ContainsKey(name))
                {
                    var prop = DeclareProperty(name, propType, _typeBuilder, customAttributesData);
                    _propertyBuilders.Add(name, prop);
                    if (isAlternativeType)
                    {
                        _alternativeProperties.Add(name, alternativeCls);
                    }
                }
            }
            else
            {
                var propName = string.Empty;
                var classSuffix = string.Empty;
                TypeBuilder currentType = _typeBuilder;
                var currentCls = this;
                var ticks = DateTime.Now.Ticks;
                for (var i = 0; i < segments.Length; ++i)
                {
                    var segment = segments[i];
                    classSuffix += segment;
                    propName = propName == string.Empty ? segment : $"{propName}.{segment}";
                    Class cls;
                    if (_subClasses.ContainsKey(propName))
                    {
                        cls = _subClasses[propName];
                        currentCls = cls;
                        currentType = cls._typeBuilder;
                    }
                    else
                    {
                        if (i == segments.Length - 1)
                        {
                            if (currentCls._propertyBuilders.ContainsKey(segment)) break;
                            var prop = currentCls.DeclareProperty(segment, propType, currentType, customAttributesData);
                            currentCls._propertyBuilders.Add(segment, prop);
                            currentCls._propertyBuilders.Add(name, prop);
                            if (isAlternativeType)
                            {
                                currentCls._alternativeProperties.Add(segment, alternativeCls);
                            }
                            break;
                        }
                        else
                        {
                            if (currentCls._alternativeProperties.ContainsKey(segment))
                            {
                                cls = currentCls._alternativeProperties[segment];
                                currentCls._subClasses.Add(segment, cls);
                                _subClasses.Add(propName, cls);
                                currentType = cls._typeBuilder;
                                currentCls = cls;
                            }
                            else
                            {
                                cls = _assembly.DeclareClass($"{Name}{classSuffix}{ticks}");
                                _subClasses.Add(propName, cls);
                                if (i > 0)
                                {
                                    currentCls._subClasses.Add(segment, cls);
                                }
                                var prop = currentCls.DeclareProperty(segment, cls._typeBuilder, currentType);
                                currentCls._propertyBuilders.Add(segment, prop);
                                currentType = cls._typeBuilder;
                                currentCls = cls;
                            }
                        }
                    }
                }
            }
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

            methodIL.MarkLabel(ret);
            methodIL.Emit(OpCodes.Ret);
        }

        private PropertyBuilder DeclareProperty(string name, Type propType, TypeBuilder typeBuilder, IList<CustomAttributeData> customAttributesData = null)
        {
            var fieldBuider = typeBuilder.DefineField($"{name.ToLower()}k__BackingField", propType, FieldAttributes.Private);
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
                var propValues = namedProps.Select(p => p.TypedValue.Value).ToArray();
                var fieldInfos = namedFields.Select(p => (FieldInfo)p.MemberInfo).ToArray();
                var fieldValues = namedFields.Select(p => p.TypedValue.Value).ToArray();
                var attributeBuilder = new CustomAttributeBuilder(data.Constructor,
                    data.ConstructorArguments.Select(p => p.Value).ToArray(),
                    propInfos,
                    propValues,
                    fieldInfos,
                    fieldValues);
                propertyBuilder.SetCustomAttribute(attributeBuilder);
            }

            return propertyBuilder;
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
            var resultVariable = methodIL.DeclareLocal(typeof(object[]));
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

        private bool ShouldBox(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            return type.IsEnum || type.IsValueType;
        }

        void IDisposable.Dispose()
        {
            Clear();
        }
    }
}
