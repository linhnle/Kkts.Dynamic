using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Kkts.Dynamic.Internal
{
    internal class InjectFromMemberBindingTree : IDisposable
    {
        private MemberTree _sourceMemberTree;
        private Type _sourceType;
        private Class _cls;
        private Class _parent;
        private Class _rootCls;
        private readonly Dictionary<string, InjectFromMemberBindingTree> _tree;
        private readonly List<(string Name, PropertyInfo PI, MemberTree Source, AlternationInfo Alternation)> _memberBindings;
        private readonly bool _isRoot;

        public InjectFromMemberBindingTree(Class cls, Type sourceType, MemberTree memberTree) : this()
        {
            _rootCls = cls;
            _cls = cls;
            _sourceType = sourceType;
            _sourceMemberTree = memberTree;
            _isRoot = true;
        }

        private InjectFromMemberBindingTree()
        {
            _tree = new Dictionary<string, InjectFromMemberBindingTree>(StringComparer.OrdinalIgnoreCase);
            _memberBindings = new List<(string Name, PropertyInfo PI, MemberTree Source, AlternationInfo Alternation)>();
        }

        private InjectFromMemberBindingTree(Class rootCls, Class cls, Class parentCls) : this()
        {
            _cls = cls;
            _rootCls = rootCls;
            _parent = parentCls;
        }

        public void Bind(IEnumerable<PropertyBinding> bindings)
        {
            foreach (var binding in bindings)
            {
                if (binding.Mode == BindingMode.OneWayToEntity) continue;
                Bind(binding.EntityProperty, binding.DtoProperty ?? binding.EntityProperty, binding.Alternation);
            }
        }

        private void Bind(string sourceProperty, string targetProperty, AlternationInfo alternation)
        {
            var sourceMember = _sourceMemberTree.PropertyOrField(sourceProperty);
            if (sourceMember.IsProperty && !sourceMember.Property.CanWrite) throw new InvalidOperationException($"The property {sourceProperty} of type {_sourceType.FullName} does not have get accessor");
            var errorMessage = $"The property {targetProperty} does not exist, it is related to entity {_sourceType.FullName}";
            if (targetProperty.IndexOf('.') == -1)
            {
                if (!_cls.PropertyBuilders.ContainsKey(targetProperty)) throw new InvalidOperationException(errorMessage);
                _memberBindings.Add((targetProperty, _cls.PropertyBuilders[targetProperty], sourceMember, alternation));
                return;
            }

            var segments = targetProperty.Split('.');
            var current = this;
            var propName = string.Empty;
            string segment;
            for (var i = 0; i < segments.Length - 1; ++i)
            {
                segment = segments[i];
                propName = propName == string.Empty ? segment : $"{propName}.{segment}";
                if (current._tree.ContainsKey(segment))
                {
                    current = current._tree[segment];
                }
                else
                {
                    if (!current._cls.PropertyBuilders.ContainsKey(segment)) throw new InvalidOperationException(errorMessage);
                    var cls = current._rootCls.SubClasses[propName];
                    var element = new InjectFromMemberBindingTree(current._rootCls, cls, current._cls);
                    element._sourceType = current._sourceType;
                    element._sourceMemberTree = sourceMember;
                    current._tree.Add(segment, element);
                    current = element;
                }
            }

            segment = segments[segments.Length - 1];
            if (!current._memberBindings.Any(p => p.Name.Equals(segment, StringComparison.OrdinalIgnoreCase)))
            {
                if (!current._cls.PropertyBuilders.ContainsKey(segment)) throw new InvalidOperationException(errorMessage);
                current._memberBindings.Add((segment, current._cls.PropertyBuilders[segment], sourceMember, alternation));
            }
        }

        private TreeEvaluationResult CheckHasTheSameParent()
        {
            var result = new TreeEvaluationResult();
            if (_memberBindings.Count == 0) return result;
            var memberBinding = _memberBindings[0];
            var parents = new List<MemberTree>();
            var parent = memberBinding.Source.Parent;
            while(parent != null && !parent.IsRoot)
            {
                parents.Add(parent);
                parent = parent.Parent;
            }

            for (var i = 1; i < _memberBindings.Count; ++i)
            {
                memberBinding = _memberBindings[i];
                parent = memberBinding.Source.Parent;
                for (var j = 0; j < parents.Count; ++j)
                {
                    if (parent is null) return result;
                    if (parent.Name != parents[j].Name) return result;
                    parent = parent.Parent;
                }

                if (!parent.IsRoot == true) return result;
            }

            parents.Reverse();
            result.HasTheSameParent = true;
            result.Parents = parents;

            return result;
        }

        internal void Build(ILGenerator generator)
        {
            var isFirstBinding = true;
            foreach (var binding in _memberBindings)
            {
                if (_isRoot)
                {
                    if (binding.Alternation.PropertyType == SpecialPropertyType.Alternative)
                    {
                        BuildAlternativePropertyMap(generator, binding);
                    }
                    else if (binding.Alternation.PropertyType == SpecialPropertyType.Array)
                    {
                        BuildCollectionPropertyMap(generator, binding, Mapper.ToDtoArrayMethodInfo);
                    }
                    else if (binding.Alternation.PropertyType == SpecialPropertyType.Collection)
                    {
                        BuildCollectionPropertyMap(generator, binding, Mapper.ToDtoCollectionMethodInfo);
                    }
                    else
                    {
                        BuildPropertyMap(generator, binding);
                    }
                }
                else
                {
                    if (binding.Alternation.PropertyType == SpecialPropertyType.Alternative)
                    {
                        BuildAlternativeNestedPropertyMap(generator, binding, isFirstBinding);
                    }
                    else if (binding.Alternation.PropertyType == SpecialPropertyType.Array)
                    {
                        BuildCollectionNestedPropertyMap(generator, binding, isFirstBinding, Mapper.ToDtoArrayMethodInfo);
                    }
                    else if (binding.Alternation.PropertyType == SpecialPropertyType.Collection)
                    {
                        BuildCollectionNestedPropertyMap(generator, binding, isFirstBinding, Mapper.ToDtoCollectionMethodInfo);
                    }
                    else
                    {
                        BuildNestedPropertyMap(generator, binding, isFirstBinding);
                    }

                    isFirstBinding = false;
                }
            }

            foreach (var item in _tree)
            {
                var endIf = generator.DefineLabel();
                var hasTheSameParent = item.Value.CheckHasTheSameParent();
                if (hasTheSameParent.HasTheSameParent)
                {
                    generator.Emit(OpCodes.Nop);
                    for (var i = 0; i < hasTheSameParent.Parents.Count; ++i)
                    {
                        generator.Emit(OpCodes.Ldarg_1);
                        for (var j = 0; j < i + 1; ++j)
                        {
                            var parent = hasTheSameParent.Parents[j];
                            if (parent.IsProperty)
                            {
                                generator.Emit(OpCodes.Callvirt, parent.Property.GetMethod);
                            }
                            else
                            {
                                generator.Emit(OpCodes.Ldfld, parent.Field);
                            }
                        }
                        generator.Emit(OpCodes.Brfalse, endIf);
                    }
                }
                generator.Emit(OpCodes.Nop);
                if (_isRoot)
                {
                    generator.Emit(OpCodes.Ldarg_0);
                }
                else
                {
                    generator.Emit(OpCodes.Dup);
                }

                generator.Emit(OpCodes.Newobj, item.Value._cls.Constructor);

                item.Value.Build(generator);

                generator.Emit(OpCodes.Nop);
                var setMethod = item.Value._parent.PropertyBuilders[item.Key].SetMethod;
                if (_isRoot)
                {
                    generator.Emit(OpCodes.Call, setMethod);
                }
                else
                {
                    generator.Emit(OpCodes.Callvirt, setMethod);
                }

                if (hasTheSameParent.HasTheSameParent)
                {
                    generator.MarkLabel(endIf);
                }
            }
        }

        private void BuildCollectionPropertyMap(ILGenerator generator, (string Name, PropertyInfo PI, MemberTree Source, AlternationInfo Alternation) binding, MethodInfo mapper)
        {
            var afterSetPoint = generator.DefineLabel();
            var nothing = generator.DefineLabel();
            generator.Emit(OpCodes.Nop);
            generator.Emit(OpCodes.Ldarg_1);
            var byPass = false;
            binding.Source.BuildGet(generator, ref nothing, ref afterSetPoint, true, ref byPass, false);
            generator.Emit(OpCodes.Pop);
            generator.Emit(OpCodes.Nop);
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldtoken, binding.Alternation.Type);
            generator.Emit(OpCodes.Call, Mapper.GetTypeFromHandleMethodInfo);
            generator.Emit(OpCodes.Ldarg_1);
            binding.Source.BuildGet(generator, ref nothing, ref afterSetPoint, true, ref byPass, true);
            generator.Emit(OpCodes.Call, mapper);
            generator.Emit(OpCodes.Call, binding.PI.SetMethod);
            generator.MarkLabel(afterSetPoint);
            generator.Emit(OpCodes.Nop);
        }

        private void BuildCollectionNestedPropertyMap(ILGenerator generator, (string Name, PropertyInfo PI, MemberTree Source, AlternationInfo Alternation) binding, bool isFirstBinding, MethodInfo mapper)
        {
            var afterSetPoint = generator.DefineLabel();
            var nothing = generator.DefineLabel();
            if (!isFirstBinding)
            {
                generator.Emit(OpCodes.Nop);
            }
            generator.Emit(OpCodes.Dup);
            generator.Emit(OpCodes.Ldarg_1);
            var byPass = false;
            binding.Source.BuildGet(generator, ref nothing, ref afterSetPoint, true, ref byPass, false);
            generator.Emit(OpCodes.Pop);
            generator.Emit(OpCodes.Nop);
            generator.Emit(OpCodes.Ldtoken, binding.Alternation.Type);
            generator.Emit(OpCodes.Call, Mapper.GetTypeFromHandleMethodInfo);
            generator.Emit(OpCodes.Ldarg_1);
            binding.Source.BuildGet(generator, ref nothing, ref afterSetPoint, true, ref byPass, true);
            generator.Emit(OpCodes.Call, mapper);
            generator.Emit(OpCodes.Call, binding.PI.SetMethod);
            generator.MarkLabel(afterSetPoint);
        }

        private void BuildAlternativePropertyMap(ILGenerator generator, (string Name, PropertyInfo PI, MemberTree Source, AlternationInfo Alternation) binding)
        {
            var afterSetPoint = generator.DefineLabel();
            var nothing = generator.DefineLabel();
            generator.Emit(OpCodes.Nop);
            generator.Emit(OpCodes.Ldarg_1);
            var byPass = false;
            binding.Source.BuildGet(generator, ref nothing, ref afterSetPoint, true, ref byPass, false);
            generator.Emit(OpCodes.Brfalse, afterSetPoint);
            generator.Emit(OpCodes.Nop);
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Newobj, binding.Alternation.AlternativeCtor);
            generator.Emit(OpCodes.Call, binding.PI.SetMethod);
            generator.Emit(OpCodes.Nop);
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Call, binding.PI.GetMethod);
            generator.Emit(OpCodes.Ldarg_1);
            binding.Source.BuildGet(generator, ref nothing, ref afterSetPoint, true, ref byPass, true);
            generator.Emit(OpCodes.Call, Mapper.MapFromEntityToDtoMethodInfo);
            generator.MarkLabel(afterSetPoint);
            generator.Emit(OpCodes.Nop);
        }

        private void BuildAlternativeNestedPropertyMap(ILGenerator generator, (string Name, PropertyInfo PI, MemberTree Source, AlternationInfo Alternation) binding, bool isFirstBinding)
        {
            var afterSetPoint = generator.DefineLabel();
            var nothing = generator.DefineLabel();
            var endIf = generator.DefineLabel();
            if (!isFirstBinding)
            {
                generator.Emit(OpCodes.Nop);
            }
            generator.Emit(OpCodes.Dup);
            generator.Emit(OpCodes.Ldarg_1);
            var byPass = false;
            binding.Source.BuildGet(generator, ref nothing, ref afterSetPoint, true, ref byPass, false);
            generator.Emit(OpCodes.Brfalse, endIf);
            generator.Emit(OpCodes.Nop);
            generator.Emit(OpCodes.Dup);
            generator.Emit(OpCodes.Newobj, binding.Alternation.AlternativeCtor);
            generator.Emit(OpCodes.Call, binding.PI.SetMethod);
            generator.Emit(OpCodes.Nop);
            generator.Emit(OpCodes.Call, binding.PI.GetMethod);
            generator.Emit(OpCodes.Ldarg_1);
            binding.Source.BuildGet(generator, ref nothing, ref afterSetPoint, true, ref byPass, true);
            generator.Emit(OpCodes.Call, Mapper.MapFromEntityToDtoMethodInfo);
            generator.Emit(OpCodes.Br, afterSetPoint);
            generator.MarkLabel(endIf);
            generator.Emit(OpCodes.Pop);
            generator.MarkLabel(afterSetPoint);
        }

        private void BuildPropertyMap(ILGenerator generator, (string Name, PropertyInfo PI, MemberTree Source, AlternationInfo Alternation) binding)
        {
            var afterSetPoint = generator.DefineLabel();
            var nothing = generator.DefineLabel();
            generator.Emit(OpCodes.Nop);
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldarg_1);
            var byPass = false;
            binding.Source.BuildGet(generator, ref nothing, ref afterSetPoint, true, ref byPass, false);
            generator.Emit(OpCodes.Call, binding.PI.SetMethod);
            generator.MarkLabel(afterSetPoint);
            generator.Emit(OpCodes.Nop);
        }

        private void BuildNestedPropertyMap(ILGenerator generator, (string Name, PropertyInfo PI, MemberTree Source, AlternationInfo Alternation) binding, bool isFirstBinding)
        {
            var afterSetPoint = generator.DefineLabel();
            var nothing = generator.DefineLabel();
            if (!isFirstBinding)
            {
                generator.Emit(OpCodes.Nop);
            }
            generator.Emit(OpCodes.Dup);
            generator.Emit(OpCodes.Ldarg_1);
            var isParentNullable = false;
            binding.Source.BuildGet(generator, ref nothing, ref afterSetPoint, true, ref isParentNullable, false);
            generator.Emit(OpCodes.Callvirt, binding.PI.SetMethod);
            generator.MarkLabel(afterSetPoint);
        }

        void IDisposable.Dispose()
        {
            foreach (IDisposable element in _tree.Values.ToArray())
            {
                element.Dispose();
            }

            _tree.Clear();
            _cls = null;
            _parent = null;
            _rootCls = null;
            ((IDisposable)_sourceMemberTree).Dispose();
        }
    }
}
