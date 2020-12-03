using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Kkts.Dynamic.Internal
{
    internal class InjectToMemberBindingTree : IDisposable
    {
        private MemberTree _targetMemberTree;
        private MemberTree _sourceMemberTree;
        private Type _targetType;
        private Class _cls;
        private Class _rootCls;
        private readonly Dictionary<string, InjectToMemberBindingTree> _tree;
        private readonly List<(string Name, MemberTree Target, MemberTree Source, AlternationInfo Alternation)> _memberBindings;
        private readonly bool _isRoot;

        public InjectToMemberBindingTree(Class cls, Type targetType, MemberTree memberTree) : this()
        {
            _rootCls = cls;
            _targetType = targetType;
            _targetMemberTree = memberTree;
            _sourceMemberTree = new MemberTree(_rootCls);
            _isRoot = true;
        }

        private InjectToMemberBindingTree()
        {
            _tree = new Dictionary<string, InjectToMemberBindingTree>(StringComparer.OrdinalIgnoreCase);
            _memberBindings = new List<(string, MemberTree, MemberTree, AlternationInfo)>();
        }

        private InjectToMemberBindingTree(Class rootCls, Class cls) : this()
        {
            _cls = cls;
            _rootCls = rootCls;
            _sourceMemberTree = new MemberTree(_cls);
        }

        public void Bind(IEnumerable<PropertyBinding> bindings)
        {
            foreach (var binding in bindings)
            {
                if (binding.Mode == BindingMode.OneWayToDto) continue;
                Bind(binding.DtoProperty ?? binding.EntityProperty, binding.EntityProperty, binding.Alternation);
            }
        }

        private void Bind(string sourceProperty, string targetProperty, AlternationInfo alternation)
        {
            var sourceMember = _sourceMemberTree.PropertyOrFieldOfClass(sourceProperty);
            var errorMessage = $"The property {targetProperty} of type {_targetType.FullName} does not have set accessor";
            var targetMember = _targetMemberTree.PropertyOrField(targetProperty);
            if (targetProperty.IndexOf('.') == -1)
            {
                if (targetMember.IsProperty && !targetMember.Property.CanWrite) throw new InvalidOperationException(errorMessage);
                _memberBindings.Add((sourceProperty, targetMember, sourceMember, alternation));
                return;
            }

            var segments = targetProperty.Split('.');
            var current = this;
            string segment;
            for (var i = 0; i < segments.Length - 1; ++i)
            {
                segment = segments[i];
                if (current._tree.ContainsKey(segment))
                {
                    current = current._tree[segment];
                }
                else
                {
                    targetMember = current._targetMemberTree.PropertyOrField(segment);
                    var cls = sourceMember.Cls;
                    if (targetMember.IsProperty && !targetMember.Property.CanWrite) throw new InvalidOperationException(errorMessage);
                    var element = new InjectToMemberBindingTree(current._rootCls, cls);
                    element._targetType = targetMember.PropertyOrFieldType;
                    element._targetMemberTree = targetMember;
                    current._tree.Add(segment, element);
                    current = element;
                }
            }

            segment = segments[segments.Length - 1];
            if (!current._memberBindings.Any(p => p.Name.Equals(segment, StringComparison.OrdinalIgnoreCase)))
            {
                targetMember = current._targetMemberTree.PropertyOrField(segment);
                if (targetMember.IsProperty && !targetMember.Property.CanWrite) throw new InvalidOperationException(errorMessage);
                current._memberBindings.Add((segment, targetMember, sourceMember, alternation));
            }
        }

        private TreeEvaluationResult CheckHasTheSameParent()
        {
            var result = new TreeEvaluationResult();
            if (_memberBindings.Count == 0) return result;
            var memberBinding = _memberBindings[0];
            var parents = new List<MemberTree>();
            var parent = memberBinding.Source.Parent;
            while (parent != null && !parent.IsRoot)
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
                        BuildCollectionPropertyMap(generator, binding, Mapper.ToEntityArrayMethodInfo);
                    }
                    else if (binding.Alternation.PropertyType == SpecialPropertyType.Collection)
                    {
                        BuildCollectionPropertyMap(generator, binding, Mapper.ToEntityCollectionMethodInfo);
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
                        BuildCollectionNestedPropertyMap(generator, binding, isFirstBinding, Mapper.ToEntityArrayMethodInfo);
                    }
                    else if (binding.Alternation.PropertyType == SpecialPropertyType.Collection)
                    {
                        BuildCollectionNestedPropertyMap(generator, binding, isFirstBinding, Mapper.ToEntityCollectionMethodInfo);
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
                        generator.Emit(OpCodes.Ldarg_0);
                        for (var j = 0; j < i + 1; ++j)
                        {
                            var parent = hasTheSameParent.Parents[j];
                            generator.Emit(OpCodes.Callvirt, parent.Property.GetMethod);
                        }
                        generator.Emit(OpCodes.Brfalse, endIf);
                    }
                }

                var memberBinding = generator.DefineLabel();
                var end = generator.DefineLabel();
                var targetMember = item.Value._targetMemberTree;
                generator.Emit(OpCodes.Nop);
                if (_isRoot)
                {
                    generator.Emit(OpCodes.Ldarg_1);
                }
                else
                {
                    generator.Emit(OpCodes.Dup);
                }

                generator.Emit(OpCodes.Dup);

                if (targetMember.IsProperty)
                {
                    generator.Emit(OpCodes.Call, targetMember.Property.GetMethod);
                }
                else
                {
                    generator.Emit(OpCodes.Ldfld, targetMember.Field);
                }
                generator.Emit(OpCodes.Brtrue, memberBinding);
                generator.Emit(OpCodes.Pop);
                generator.Emit(OpCodes.Nop);
                if (_isRoot)
                {
                    generator.Emit(OpCodes.Ldarg_1);
                }
                else
                {
                    generator.Emit(OpCodes.Dup);
                }
                generator.Emit(OpCodes.Newobj, item.Value._targetType.GetConstructors()[0]);
                
                item.Value.Build(generator);

                generator.Emit(OpCodes.Nop);
                
                if (targetMember.IsProperty)
                {
                    var setMethod = targetMember.Property.SetMethod;
                    if (_isRoot)
                    {
                        generator.Emit(OpCodes.Call, setMethod);
                    }
                    else
                    {
                        generator.Emit(OpCodes.Callvirt, setMethod);
                    }
                }
                else
                {
                    generator.Emit(OpCodes.Stfld, targetMember.Field);
                }

                generator.Emit(OpCodes.Br, end);
                generator.MarkLabel(memberBinding);
                if (targetMember.IsProperty)
                {
                    generator.Emit(OpCodes.Call, targetMember.Property.GetMethod);
                }
                else
                {
                    generator.Emit(OpCodes.Ldfld, targetMember.Field);
                }
                item.Value.Build(generator);
                generator.Emit(OpCodes.Pop);
                if (hasTheSameParent.HasTheSameParent)
                {
                    generator.MarkLabel(endIf);
                }

                generator.MarkLabel(end);
            }
        }

        private void BuildCollectionPropertyMap(ILGenerator generator, (string Name, MemberTree Target, MemberTree Source, AlternationInfo Alternation) binding, MethodInfo mapper)
        {
            var afterSetPoint = generator.DefineLabel();
            var nothing = generator.DefineLabel();
            var memberBinding = generator.DefineLabel();
            var targetMember = binding.Target;
            generator.Emit(OpCodes.Nop);
            generator.Emit(OpCodes.Ldarg_0);
            var byPass = false;
            binding.Source.BuildGet(generator, ref nothing, ref afterSetPoint, true, ref byPass, false);
            generator.Emit(OpCodes.Pop);
            generator.Emit(OpCodes.Nop);
            generator.Emit(OpCodes.Ldarg_1);
            generator.Emit(OpCodes.Ldtoken, binding.Alternation.EntityElementType);
            generator.Emit(OpCodes.Call, Mapper.GetTypeFromHandleMethodInfo);
            generator.Emit(OpCodes.Ldarg_0);
            binding.Source.BuildGet(generator, ref nothing, ref afterSetPoint, true, ref byPass, true);
            generator.Emit(OpCodes.Call, mapper);
            if (targetMember.IsProperty)
            {
                var setMethod = targetMember.Property.SetMethod;
                generator.Emit(OpCodes.Callvirt, setMethod);
            }
            else
            {
                generator.Emit(OpCodes.Stfld, targetMember.Field);
            }
            generator.MarkLabel(afterSetPoint);
            generator.Emit(OpCodes.Nop);
        }

        private void BuildCollectionNestedPropertyMap(ILGenerator generator, (string Name, MemberTree Target, MemberTree Source, AlternationInfo Alternation) binding, bool isFirstBinding, MethodInfo mapper)
        {
            var afterSetPoint = generator.DefineLabel();
            var nothing = generator.DefineLabel();
            var targetMember = binding.Target;
            generator.Emit(OpCodes.Nop);
            generator.Emit(OpCodes.Dup);
            generator.Emit(OpCodes.Ldarg_0);
            var byPass = false;
            binding.Source.BuildGet(generator, ref nothing, ref afterSetPoint, true, ref byPass, false);
            generator.Emit(OpCodes.Pop);
            generator.Emit(OpCodes.Nop);
            generator.Emit(OpCodes.Ldtoken, binding.Alternation.EntityElementType);
            generator.Emit(OpCodes.Call, Mapper.GetTypeFromHandleMethodInfo);
            generator.Emit(OpCodes.Ldarg_0);
            binding.Source.BuildGet(generator, ref nothing, ref afterSetPoint, true, ref byPass, true);
            generator.Emit(OpCodes.Call, mapper);
            if (targetMember.IsProperty)
            {
                var setMethod = targetMember.Property.SetMethod;
                generator.Emit(OpCodes.Callvirt, setMethod);
            }
            else
            {
                generator.Emit(OpCodes.Stfld, targetMember.Field);
            }
            generator.MarkLabel(afterSetPoint);
            generator.Emit(OpCodes.Nop);
        }

        private void BuildAlternativePropertyMap(ILGenerator generator, (string Name, MemberTree Target, MemberTree Source, AlternationInfo Alternation) binding)
        {
            var afterSetPoint = generator.DefineLabel();
            var nothing = generator.DefineLabel();
            var memberBinding = generator.DefineLabel();
            var targetMember = binding.Target;
            generator.Emit(OpCodes.Nop);
            generator.Emit(OpCodes.Ldarg_0);
            var byPass = false;
            binding.Source.BuildGet(generator, ref nothing, ref afterSetPoint, true, ref byPass, false);
            generator.Emit(OpCodes.Pop);
            generator.Emit(OpCodes.Nop);
            generator.Emit(OpCodes.Ldarg_1);
            generator.Emit(OpCodes.Dup);

            if (binding.Target.IsProperty)
            {
                generator.Emit(OpCodes.Call, binding.Target.Property.GetMethod);
            }
            else
            {
                generator.Emit(OpCodes.Ldfld, binding.Target.Field);
            }
            generator.Emit(OpCodes.Brtrue, memberBinding);
            generator.Emit(OpCodes.Newobj, binding.Target.PropertyOrFieldType.GetConstructors()[0]);
            if (targetMember.IsProperty)
            {
                var setMethod = targetMember.Property.SetMethod;
                generator.Emit(OpCodes.Callvirt, setMethod);
            }
            else
            {
                generator.Emit(OpCodes.Stfld, targetMember.Field);
            }
            generator.Emit(OpCodes.Nop);
            generator.Emit(OpCodes.Ldarg_0);
            binding.Source.BuildGet(generator, ref nothing, ref afterSetPoint, true, ref byPass, true);
            generator.Emit(OpCodes.Ldarg_1);
            binding.Target.BuildGet(generator, ref nothing, ref afterSetPoint, true, ref byPass, true);
            generator.Emit(OpCodes.Call, Mapper.MapFromDtoToEntityMethodInfo);
            generator.Emit(OpCodes.Br, afterSetPoint);
            generator.MarkLabel(memberBinding);
            generator.Emit(OpCodes.Pop);
            generator.Emit(OpCodes.Nop);
            generator.Emit(OpCodes.Ldarg_0);
            binding.Source.BuildGet(generator, ref nothing, ref afterSetPoint, true, ref byPass, true);
            generator.Emit(OpCodes.Ldarg_1);
            binding.Target.BuildGet(generator, ref nothing, ref afterSetPoint, true, ref byPass, true);
            generator.Emit(OpCodes.Call, Mapper.MapFromDtoToEntityMethodInfo);
            generator.MarkLabel(afterSetPoint);
            generator.Emit(OpCodes.Nop);
        }

        private void BuildAlternativeNestedPropertyMap(ILGenerator generator, (string Name, MemberTree Target, MemberTree Source, AlternationInfo Alternation) binding, bool isFirstBinding)
        {
            var afterSetPoint = generator.DefineLabel();
            var nothing = generator.DefineLabel();
            var memberBinding = generator.DefineLabel();
            var targetMember = binding.Target;
            generator.Emit(OpCodes.Nop);
            generator.Emit(OpCodes.Dup);
            generator.Emit(OpCodes.Ldarg_0);
            var byPass = false;
            binding.Source.BuildGet(generator, ref nothing, ref afterSetPoint, true, ref byPass, false);
            generator.Emit(OpCodes.Pop);
            generator.Emit(OpCodes.Nop);
            generator.Emit(OpCodes.Dup);

            if (binding.Target.IsProperty)
            {
                generator.Emit(OpCodes.Call, binding.Target.Property.GetMethod);
            }
            else
            {
                generator.Emit(OpCodes.Ldfld, binding.Target.Field);
            }
            generator.Emit(OpCodes.Brtrue, memberBinding);
            generator.Emit(OpCodes.Pop);
            generator.Emit(OpCodes.Dup);
            generator.Emit(OpCodes.Newobj, binding.Target.PropertyOrFieldType.GetConstructors()[0]);
            if (targetMember.IsProperty)
            {
                var setMethod = targetMember.Property.SetMethod;
                generator.Emit(OpCodes.Callvirt, setMethod);
            }
            else
            {
                generator.Emit(OpCodes.Stfld, targetMember.Field);
            }
            generator.Emit(OpCodes.Nop);
            generator.Emit(OpCodes.Dup);
            if (binding.Target.IsProperty)
            {
                generator.Emit(OpCodes.Call, binding.Target.Property.GetMethod);
            }
            else
            {
                generator.Emit(OpCodes.Ldfld, binding.Target.Field);
            }
            generator.Emit(OpCodes.Stloc_0);
            generator.Emit(OpCodes.Ldarg_0);
            binding.Source.BuildGet(generator, ref nothing, ref afterSetPoint, true, ref byPass, true);
            generator.Emit(OpCodes.Ldloc_0);
            generator.Emit(OpCodes.Call, Mapper.MapFromDtoToEntityMethodInfo);
            generator.Emit(OpCodes.Br, afterSetPoint);
            generator.MarkLabel(memberBinding);
            generator.Emit(OpCodes.Pop);
            generator.Emit(OpCodes.Nop);
            generator.Emit(OpCodes.Dup);
            if (binding.Target.IsProperty)
            {
                generator.Emit(OpCodes.Call, binding.Target.Property.GetMethod);
            }
            else
            {
                generator.Emit(OpCodes.Ldfld, binding.Target.Field);
            }
            generator.Emit(OpCodes.Stloc_0);
            generator.Emit(OpCodes.Ldarg_0);
            binding.Source.BuildGet(generator, ref nothing, ref afterSetPoint, true, ref byPass, true);
            generator.Emit(OpCodes.Ldloc_0);
            generator.Emit(OpCodes.Call, Mapper.MapFromDtoToEntityMethodInfo);
            generator.MarkLabel(afterSetPoint);
            generator.Emit(OpCodes.Nop);
        }

        private void BuildPropertyMap(ILGenerator generator, (string Name, MemberTree Target, MemberTree Source, AlternationInfo Alternation) binding)
        {
            var afterSetPoint = generator.DefineLabel();
            var nothing = generator.DefineLabel();
            generator.Emit(OpCodes.Nop);
            generator.Emit(OpCodes.Ldarg_1);
            generator.Emit(OpCodes.Ldarg_0);
            var isParentNullable = false;
            binding.Source.BuildGet(generator, ref nothing, ref afterSetPoint, true, ref isParentNullable, false);
            if (binding.Target.IsProperty)
            {
                generator.Emit(OpCodes.Call, binding.Target.Property.SetMethod);
            }
            else
            {
                generator.Emit(OpCodes.Stfld, binding.Target.Field);
            }
            generator.MarkLabel(afterSetPoint);
            generator.Emit(OpCodes.Nop);
        }

        private void BuildNestedPropertyMap(ILGenerator generator, (string Name, MemberTree Target, MemberTree Source, AlternationInfo Alternation) binding, bool isFirstBinding)
        {
            var afterSetPoint = generator.DefineLabel();
            var nothing = generator.DefineLabel();
            if (!isFirstBinding)
            {
                generator.Emit(OpCodes.Nop);
            }
            generator.Emit(OpCodes.Dup);
            generator.Emit(OpCodes.Ldarg_0);
            var isParentNullable = false;
            binding.Source.BuildGet(generator, ref nothing, ref afterSetPoint, true, ref isParentNullable, false);
            if (binding.Target.IsProperty)
            {
                generator.Emit(OpCodes.Call, binding.Target.Property.SetMethod);
            }
            else
            {
                generator.Emit(OpCodes.Stfld, binding.Target.Field);
            }
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
            _rootCls = null;
            ((IDisposable)_sourceMemberTree).Dispose();
            ((IDisposable)_targetMemberTree).Dispose();
        }
    }
}
