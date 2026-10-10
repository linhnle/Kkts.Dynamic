using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Kkts.Dynamic.Internal
{
    internal sealed class MemberTree : IDisposable
    {
        private readonly Dictionary<string, MemberTree> _tree = new Dictionary<string, MemberTree>(StringComparer.OrdinalIgnoreCase);
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
                    var property = currentType.GetProperty(segment, TypeExtensions.BindingFlags);
                    var declaredType = property?.PropertyType;
                    FieldInfo field = null;
                    var isProp = true;
                    if (property == null)
                    {
                        isProp = false;
                        field = currentType.GetField(segment, TypeExtensions.BindingFlags) ?? throw new InvalidOperationException($"The property or field '{name}' does not exist in '{_declaredType.FullName}'");
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
                    var element = CreateClassMember(current, currentCls, segment, name, i == segments.Length - 1);
                    current._tree.Add(segment, element);
                    current = element;
                    currentCls = element._cls;
                }
            }

            return current;
        }

        private MemberTree CreateClassMember(MemberTree parent, Class cls, string segment, string path, bool isLeaf)
        {
            if (!cls.PropertyBuilders.TryGetValue(segment, out var property))
                throw new InvalidOperationException($"The property or field '{path}' does not exist in '{_cls.Name}'");
            var memberClass = isLeaf && !parent._cls.SubClasses.ContainsKey(segment)
                ? parent._cls : parent._cls.SubClasses[segment];
            return new MemberTree(property.PropertyType, parent)
            {
                _cls = memberClass,
                Property = property,
                IsProperty = true,
                Name = segment
            };
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
            return Nullable.GetUnderlyingType(_declaredType) != null || !_declaredType.IsValueType;
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
