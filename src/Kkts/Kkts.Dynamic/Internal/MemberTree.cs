using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Kkts.Dynamic.Internal
{
    internal class MemberTree : IDisposable
    {
        private readonly Dictionary<string, MemberTree> _tree = new Dictionary<string, MemberTree>();
        private readonly Type _declaredType;
        private MemberTree _parent;
        private Class _cls;
        private readonly bool _isRoot;

        public MemberTree(Type declaredType)
        {
            _declaredType = declaredType;
            _isRoot = true;
        }

        public MemberTree(Class cls)
        {
            _cls = cls;
            _isRoot = true;
        }

        private MemberTree(Type declaredType, MemberTree parent) 
        {
            _declaredType = declaredType;
            _parent = parent;
        }

        public string Name { get; set; }

        public FieldInfo Field { get; set; }

        public PropertyInfo Property { get; set; }

        public bool IsProperty { get; set; }

        public Type PropertyOrFieldType => _declaredType;

        public Class Cls => _cls;

        public MemberTree Parent => _parent;

        public bool IsRoot => _isRoot;

        public IList<CustomAttributeData> GetCustomAttributesData()
        {
            return IsProperty ? Property.GetCustomAttributesData() : Field.GetCustomAttributesData();
        }

        public MemberTree PropertyOrField(string name)
        {
            var segments = name.Split('.');
            var current = this;
            var currentType = _declaredType;
            foreach (var segment in segments)
            {
                if (current._tree.ContainsKey(segment))
                {
                    current = current._tree[segment];
                    currentType = current.PropertyOrFieldType;
                }
                else
                {
                    var property = currentType.GetProperty(segment);
                    var declaredType = property?.PropertyType;
                    FieldInfo field = null;
                    var isProp = true;
                    if (property == null)
                    {
                        isProp = false;
                        field = currentType.GetField(segment) ?? throw new InvalidOperationException($"The property or field '{name}' does not exist in '{_declaredType.FullName}'");
                        declaredType = field.FieldType;
                    }

                    var element = new MemberTree(declaredType, current)
                    {
                        Field = field,
                        Property = property,
                        IsProperty = isProp,
                        Name = segment
                    };
                    
                    current._tree.Add(segment, element);
                    current = element;
                    currentType = declaredType;
                }
            }

            return current;
        }

        public MemberTree PropertyOrFieldOfClass(string name)
        {
            var segments = name.Split('.');
            var current = this;
            var currentCls = _cls;
            var propName = string.Empty;
            for (var i = 0; i < segments.Length; ++i)
            {
                var segment = segments[i];
                if (current._tree.ContainsKey(segment))
                {
                    current = current._tree[segment];
                    currentCls = current._cls;
                }
                else
                {
                    MemberTree element;
                    PropertyBuilder property;
                    Class cls;
                    if (i == segments.Length - 1)
                    {
                        if (!currentCls.PropertyBuilders.ContainsKey(segment)) throw new InvalidOperationException($"The property or field '{name}' does not exist in '{_cls.Name}'");
                        property = currentCls.PropertyBuilders[segment];
                        cls = current._cls.SubClasses.ContainsKey(segment) ? current._cls.SubClasses[segment] : current._cls;
                        var type = property.PropertyType;
                        element = new MemberTree(type, current)
                        {
                            _cls = cls,
                            Property = property,
                            IsProperty = true,
                            Name = segment
                        };
                        current._tree.Add(segment, element);
                        current = element;

                        break;
                    }

                    propName = propName == string.Empty ? segment : $"{propName}.{segment}";
                    if (!currentCls.PropertyBuilders.ContainsKey(segment)) throw new InvalidOperationException($"The property or field '{name}' does not exist in '{_cls.Name}'");
                    cls = current._cls.SubClasses[segment];
                    property = currentCls.PropertyBuilders[segment];
                    var declaredType = property.PropertyType;
                    element = new MemberTree(declaredType, current)
                    {
                        _cls = cls,
                        Property = property,
                        IsProperty = true,
                        Name = segment
                    };

                    current._tree.Add(segment, element);
                    currentCls = cls;
                    current = element;
                }
            }

            return current;
        }

        internal void BuildGet(ILGenerator generator, ref Label jumpPoint, ref Label afterSetPoint, bool topmost, ref bool isParentNullable, bool ignoreNullableCheck)
        {
            if (_isRoot) return;
            var notNullJumpPoint = generator.DefineLabel();
            var isNullable = false;
            _parent.BuildGet(generator, ref notNullJumpPoint, ref afterSetPoint, false, ref isNullable, ignoreNullableCheck);

            generator.MarkLabel(notNullJumpPoint);
            if (IsProperty)
            {
                if (isNullable && !ignoreNullableCheck)
                {
                    generator.Emit(OpCodes.Call, Property.GetMethod);
                }
                else
                {
                    generator.Emit(OpCodes.Callvirt, Property.GetMethod);
                }
            }
            else
            {
                generator.Emit(OpCodes.Ldfld, Field);
            }

            if (!topmost && IsNullable() && !ignoreNullableCheck)
            {
                isParentNullable = true;
                generator.Emit(OpCodes.Dup);
                generator.Emit(OpCodes.Brtrue, jumpPoint);
                generator.Emit(OpCodes.Pop);
                generator.Emit(OpCodes.Pop);
                generator.Emit(OpCodes.Br, afterSetPoint);
            }
        }

        private bool IsNullable()
        {
            return Nullable.GetUnderlyingType(_declaredType) != null || _declaredType.IsClass;
        }

        void IDisposable.Dispose()
        {
            foreach (IDisposable element in _tree.Values.ToArray())
            {
                element.Dispose();
            }

            _tree.Clear();
            _parent = null;
            _cls = null;
        }
    }
}
