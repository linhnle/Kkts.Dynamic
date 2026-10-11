using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Kkts.Dynamic.Internal
{
    internal sealed class InjectToMemberBindingTree : IDisposable
    {
        private MemberTree _targetMemberTree;
        private readonly MemberTree _sourceMemberTree;
        private Type _targetType;
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
            _rootCls = rootCls;
            _sourceMemberTree = new MemberTree(cls);
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
                current = current.GetOrAddChild(segment, sourceMember.Cls, errorMessage);
            }

            segment = segments[segments.Length - 1];
            if (!current._memberBindings.Any(p => p.Name.Equals(segment, StringComparison.OrdinalIgnoreCase)))
            {
                targetMember = current._targetMemberTree.PropertyOrField(segment);
                if (targetMember.IsProperty && !targetMember.Property.CanWrite) throw new InvalidOperationException(errorMessage);
                current._memberBindings.Add((segment, targetMember, sourceMember, alternation));
            }
        }

        private InjectToMemberBindingTree GetOrAddChild(string segment, Class cls, string errorMessage)
        {
            if (_tree.TryGetValue(segment, out var existing)) return existing;
            var target = _targetMemberTree.PropertyOrField(segment);
            if (target.IsProperty && !target.Property.CanWrite) throw new InvalidOperationException(errorMessage);
            var element = new InjectToMemberBindingTree(_rootCls, cls)
            {
                _targetType = target.PropertyOrFieldType,
                _targetMemberTree = target
            };
            _tree.Add(segment, element);
            return element;
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

                if (parent == null || !parent.IsRoot) return result;
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
                    BuildRootBinding(generator, binding);
                }
                else
                {
                    BuildNestedBinding(generator, binding, isFirstBinding);
                    isFirstBinding = false;
                }
            }

            foreach (var child in _tree.Values)
            {
                BuildChild(generator, child);
            }
        }

        private static void BuildRootBinding(ILGenerator generator, (string Name, MemberTree Target, MemberTree Source, AlternationInfo Alternation) binding)
        {
            switch (binding.Alternation.PropertyType)
            {
                case SpecialPropertyType.Alternative:
                    BuildAlternativePropertyMap(generator, binding);
                    break;
                case SpecialPropertyType.Array:
                    BuildCollectionPropertyMap(generator, binding, Mapper.ToEntityArrayMethodInfo);
                    break;
                case SpecialPropertyType.Collection:
                    BuildCollectionPropertyMap(generator, binding, Mapper.ToEntityCollectionMethodInfo);
                    break;
                default:
                    BuildPropertyMap(generator, binding);
                    break;
            }
        }

        private static void BuildNestedBinding(ILGenerator generator, (string Name, MemberTree Target, MemberTree Source, AlternationInfo Alternation) binding, bool isFirstBinding)
        {
            switch (binding.Alternation.PropertyType)
            {
                case SpecialPropertyType.Alternative:
                    BuildAlternativeNestedPropertyMap(generator, binding);
                    break;
                case SpecialPropertyType.Array:
                    BuildCollectionNestedPropertyMap(generator, binding, Mapper.ToEntityArrayMethodInfo);
                    break;
                case SpecialPropertyType.Collection:
                    BuildCollectionNestedPropertyMap(generator, binding, Mapper.ToEntityCollectionMethodInfo);
                    break;
                default:
                    BuildNestedPropertyMap(generator, binding, isFirstBinding);
                    break;
            }
        }

        private void BuildChild(ILGenerator generator, InjectToMemberBindingTree child)
        {
            var endIf = generator.DefineLabel();
            var hasTheSameParent = child.CheckHasTheSameParent();
            hasTheSameParent.BuildParentGuard(generator, OpCodes.Ldarg_0, endIf);

            var memberBinding = generator.DefineLabel();
            var end = generator.DefineLabel();
            var targetMember = child._targetMemberTree;
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
                generator.Emit(OpCodes.Callvirt, targetMember.Property.GetMethod);
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
            generator.Emit(OpCodes.Newobj, child._targetType.GetRequiredConstructor());

            child.Build(generator);

            generator.Emit(OpCodes.Nop);

            BuildChildSet(generator, targetMember);

            generator.Emit(OpCodes.Br, end);
            generator.MarkLabel(memberBinding);
            if (targetMember.IsProperty)
            {
                generator.Emit(OpCodes.Callvirt, targetMember.Property.GetMethod);
            }
            else
            {
                generator.Emit(OpCodes.Ldfld, targetMember.Field);
            }
            child.Build(generator);
            generator.Emit(OpCodes.Pop);
            if (hasTheSameParent.HasTheSameParent)
            {
                generator.MarkLabel(endIf);
            }

            generator.MarkLabel(end);
        }

        private void BuildChildSet(ILGenerator generator, MemberTree target)
        {
            if (target.IsProperty)
                generator.Emit(OpCodes.Callvirt, target.Property.SetMethod);
            else
                generator.Emit(OpCodes.Stfld, target.Field);
        }

        private static void BuildCollectionPropertyMap(ILGenerator generator, (string Name, MemberTree Target, MemberTree Source, AlternationInfo Alternation) binding, MethodInfo mapper)
        {
            var afterSetPoint = generator.DefineLabel();
            var nothing = generator.DefineLabel();
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

        private static void BuildCollectionNestedPropertyMap(ILGenerator generator, (string Name, MemberTree Target, MemberTree Source, AlternationInfo Alternation) binding, MethodInfo mapper)
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

        private static void BuildAlternativePropertyMap(ILGenerator generator, (string Name, MemberTree Target, MemberTree Source, AlternationInfo Alternation) binding)
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
                generator.Emit(OpCodes.Callvirt, binding.Target.Property.GetMethod);
            }
            else
            {
                generator.Emit(OpCodes.Ldfld, binding.Target.Field);
            }
            generator.Emit(OpCodes.Brtrue, memberBinding);
            generator.Emit(OpCodes.Newobj, binding.Target.PropertyOrFieldType.GetRequiredConstructor());
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

        private static void BuildAlternativeNestedPropertyMap(ILGenerator generator, (string Name, MemberTree Target, MemberTree Source, AlternationInfo Alternation) binding)
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
                generator.Emit(OpCodes.Callvirt, binding.Target.Property.GetMethod);
            }
            else
            {
                generator.Emit(OpCodes.Ldfld, binding.Target.Field);
            }
            generator.Emit(OpCodes.Brtrue, memberBinding);
            generator.Emit(OpCodes.Pop);
            generator.Emit(OpCodes.Dup);
            generator.Emit(OpCodes.Newobj, binding.Target.PropertyOrFieldType.GetRequiredConstructor());
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
                generator.Emit(OpCodes.Callvirt, binding.Target.Property.GetMethod);
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
                generator.Emit(OpCodes.Callvirt, binding.Target.Property.GetMethod);
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

        private static void BuildPropertyMap(ILGenerator generator, (string Name, MemberTree Target, MemberTree Source, AlternationInfo Alternation) binding)
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
                generator.Emit(OpCodes.Callvirt, binding.Target.Property.SetMethod);
            }
            else
            {
                generator.Emit(OpCodes.Stfld, binding.Target.Field);
            }
            generator.MarkLabel(afterSetPoint);
            generator.Emit(OpCodes.Nop);
        }

        private static void BuildNestedPropertyMap(ILGenerator generator, (string Name, MemberTree Target, MemberTree Source, AlternationInfo Alternation) binding, bool isFirstBinding)
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
                generator.Emit(OpCodes.Callvirt, binding.Target.Property.SetMethod);
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
            _rootCls = null;
            ((IDisposable)_sourceMemberTree).Dispose();
            ((IDisposable)_targetMemberTree).Dispose();
        }
    }
}
