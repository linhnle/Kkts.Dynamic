using System;
using System.Collections.Generic;
using System.Linq;

namespace Kkts.Dynamic.Internal
{
    internal static class BindingValidation
    {
        internal static bool IsPath(string path) =>
            !string.IsNullOrWhiteSpace(path) && !path.Split('.').Any(string.IsNullOrWhiteSpace);

        internal static PropertyBinding[] Snapshot(IEnumerable<PropertyBinding> bindings)
        {
            return bindings.Select((binding, index) => binding == null ? null : new PropertyBinding
            {
                EntityProperty = binding.EntityProperty,
                DtoProperty = binding.DtoProperty ?? binding.EntityProperty,
                Mode = binding.Mode,
                IsPrimaryKey = binding.IsPrimaryKey,
                PrimaryKeyOrder = binding.PrimaryKeyOrder,
                NestedShape = binding.NestedShape,
                DtoValueType = binding.DtoValueType ?? (binding.IsComputed
                    ? binding.ComputedExpression?.ReturnType ?? binding.ComputedInMemory?.GetType().GetMethod("Invoke")?.ReturnType : null),
                ToDtoExpression = binding.ToDtoExpression,
                ToEntityExpression = binding.ToEntityExpression,
                ToDtoInMemory = binding.ToDtoInMemory,
                ToEntityInMemory = binding.ToEntityInMemory,
                ComputedExpression = binding.ComputedExpression,
                ComputedInMemory = binding.ComputedInMemory,
                OriginalIndex = index
            }).ToArray();
        }

        internal static IEnumerable<MappingDiagnostic> Syntax(string name, PropertyBinding[] bindings)
        {
            var dtoPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entityWrites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < bindings.Length; index++)
            {
                var binding = bindings[index];
                if (binding == null)
                {
                    yield return new MappingDiagnostic("NullBinding", name, null, "Configuration",
                        "Bindings cannot contain null entries.", "Remove the null entry.", bindingIndex: index);
                    continue;
                }
                if (!binding.IsComputed && !IsPath(binding.EntityProperty))
                    yield return Error("EntityPath", name, binding, "Entity paths must contain non-empty segments.", "Provide an entity member path.");
                if (!IsPath(binding.DtoProperty))
                    yield return Error("DtoPath", name, binding, "DTO paths must contain non-empty segments.", "Provide a DTO path or omit it to use the entity path.");
                if (!Enum.IsDefined(typeof(BindingMode), binding.Mode))
                    yield return Error("BindingMode", name, binding, $"Invalid binding mode: {binding.Mode}.", "Choose a defined binding mode.");
                if (IsPath(binding.DtoProperty) && !dtoPaths.Add(binding.DtoProperty))
                    yield return Error("DuplicateDtoPath", name, binding, $"Duplicate DTO path '{binding.DtoProperty}'.", "Use a unique DTO path.");
                if (IsPath(binding.EntityProperty) && binding.Mode != BindingMode.OneWayToDto && !entityWrites.Add(binding.EntityProperty))
                    yield return Error("DuplicateEntityWrite", name, binding, $"Multiple bindings write entity path '{binding.EntityProperty}'.", "Keep only one reverse-enabled binding for this entity path.");
                if (binding.IsPrimaryKey && IsPath(binding.EntityProperty) && IsPath(binding.DtoProperty)
                    && (binding.EntityProperty.IndexOf('.') >= 0 || binding.DtoProperty.IndexOf('.') >= 0))
                    yield return Error("PrimaryKeyPath", name, binding, "Primary keys must use top-level entity and DTO properties.", "Choose top-level key paths.");
            }
        }

        private static MappingDiagnostic Error(string code, string name, PropertyBinding binding, string message, string fix) =>
            new MappingDiagnostic(code, name, binding, "Configuration", message, fix);

        internal static void Members(string name, Type mappingType, PropertyBinding[] bindings,
            Func<Type, PropertyBinding, AlternationInfo> resolveAlternative, List<MappingDiagnostic> diagnostics)
        {
            foreach (var computed in bindings.Where(b => b != null && b.IsComputed))
            {
                Transformations(mappingType, computed);
                if (IsPath(computed.DtoProperty) && bindings.Any(b => b != null && b.DtoProperty != null
                    && b.DtoProperty.StartsWith(computed.DtoProperty + ".", StringComparison.OrdinalIgnoreCase)))
                    diagnostics.Add(Error("DtoLeafParent", name, computed, $"DTO path '{computed.DtoProperty}' cannot also be a parent.", "Rename the computed value or grouping path."));
            }
            using (var tree = new MemberTree(mappingType))
            {
                foreach (var binding in bindings.Where(b => b != null && !b.IsComputed && IsPath(b.EntityProperty) && IsPath(b.DtoProperty)
                    && Enum.IsDefined(typeof(BindingMode), b.Mode)))
                {
                    MemberTree member = null;
                    Check("MemberPath", binding, "Configuration", "Correct the entity path to a public instance property or field.",
                        () => member = tree.PropertyOrField(binding.EntityProperty));
                    if (member == null) continue;
                    if (binding.IsTransformed) Transformations(member.PropertyOrFieldType, binding);
                    var writes = binding.Mode != BindingMode.OneWayToDto;
                    var reads = binding.Mode != BindingMode.OneWayToEntity;
                    CheckMember(member, binding, writes, reads);
                    for (var parent = member.Parent; !parent.IsRoot; parent = parent.Parent)
                    {
                        var current = parent;
                        if (current.PropertyOrFieldType.IsValueType)
                            diagnostics.Add(new MappingDiagnostic("NestedValueType", name, binding, "Configuration",
                                $"Nested value-type path '{binding.EntityProperty}' is not supported.", "Flatten a reference-type path instead."));
                        CheckMember(current, binding, writes, true);
                        if (writes)
                            Check("Constructor", binding, "DtoToEntity", "Use a visible reference class with a public parameterless constructor.",
                                () => current.PropertyOrFieldType.GetRequiredConstructor());
                    }
                    AlternationInfo alternative = null;
                    Check("AlternativeType", binding, "Configuration", "Use a supported mapped object, array, or collection.",
                        () => alternative = resolveAlternative(member.PropertyOrFieldType, binding));
                    if (alternative == null) continue;
                    if (writes && alternative.PropertyType == SpecialPropertyType.Alternative)
                        CheckMember(member, binding, true, true);
                    if (writes && alternative.PropertyType != SpecialPropertyType.None)
                        Check("Constructor", binding, "DtoToEntity", "Use a visible reference class with a public parameterless constructor.",
                            () => (alternative.EntityElementType ?? member.PropertyOrFieldType).GetRequiredConstructor());
                    if (alternative.PropertyType != SpecialPropertyType.Alternative
                        && bindings.Any(b => b != null && b.DtoProperty != null
                            && b.DtoProperty.StartsWith(binding.DtoProperty + ".", StringComparison.OrdinalIgnoreCase)))
                        diagnostics.Add(new MappingDiagnostic("DtoLeafParent", name, binding, "Configuration",
                            $"DTO path '{binding.DtoProperty}' cannot also be a parent of another path.", "Rename the leaf or the grouping path."));
                    if (alternative.PropertyType == SpecialPropertyType.Alternative && alternative.Cls != null)
                    {
                        foreach (var overlay in bindings.Where(b => b != null && (b.IsComputed || IsPath(b.EntityProperty))
                            && b.DtoProperty != null && b.DtoProperty.StartsWith(binding.DtoProperty + ".", StringComparison.OrdinalIgnoreCase)))
                        {
                            var relativePath = overlay.DtoProperty.Substring(binding.DtoProperty.Length + 1);
                            CheckOverlay(alternative.Cls, overlay, relativePath, binding.EntityProperty);
                        }
                    }
                }
            }

            void CheckOverlay(Class baseShape, PropertyBinding overlay, string relativePath, string entityPrefix)
            {
                foreach (var baseBinding in baseShape.Bindings)
                {
                    var sameLeaf = relativePath.Equals(baseBinding.DtoProperty, StringComparison.OrdinalIgnoreCase);
                    var extendsLeaf = relativePath.StartsWith(baseBinding.DtoProperty + ".", StringComparison.OrdinalIgnoreCase);
                    var replacesParent = baseBinding.DtoProperty.StartsWith(relativePath + ".", StringComparison.OrdinalIgnoreCase);
                    var conflict = (sameLeaf && (overlay.Mode != baseBinding.Mode
                        || !string.Equals(overlay.EntityProperty, entityPrefix + "." + baseBinding.EntityProperty, StringComparison.OrdinalIgnoreCase)
                        || overlay.NestedShape != baseBinding.NestedShape
                        || overlay.DtoValueType != baseBinding.DtoValueType
                        || overlay.ToDtoExpression != baseBinding.ToDtoExpression
                        || overlay.ToEntityExpression != baseBinding.ToEntityExpression
                        || !ReferenceEquals(overlay.ToDtoInMemory, baseBinding.ToDtoInMemory)
                        || !ReferenceEquals(overlay.ToEntityInMemory, baseBinding.ToEntityInMemory)
                        || overlay.IsComputed || baseBinding.IsComputed)) || replacesParent;
                    if (extendsLeaf)
                    {
                        AlternationInfo nested = null;
                        Check("OverlayConflict", overlay, "Configuration", "Use a valid mapped nested shape for overlays.", () =>
                        {
                            using (var baseTree = new MemberTree(baseShape.MappingType))
                                nested = resolveAlternative(baseTree.PropertyOrField(baseBinding.EntityProperty).PropertyOrFieldType, baseBinding);
                        });
                        if (nested?.PropertyType == SpecialPropertyType.Alternative && nested.Cls != null)
                            CheckOverlay(nested.Cls, overlay, relativePath.Substring(baseBinding.DtoProperty.Length + 1),
                                entityPrefix + "." + baseBinding.EntityProperty);
                        else conflict = true;
                    }
                    if (conflict)
                        diagnostics.Add(new MappingDiagnostic("OverlayConflict", name, overlay, "Configuration",
                            $"Overlay '{relativePath}' conflicts with leaf '{baseBinding.DtoProperty}' in selected base shape '{baseShape.Name}'.",
                            "Use an independent overlay path with compatible type and binding behavior."));
                    if (!sameLeaf && overlay.Mode != BindingMode.OneWayToDto && baseBinding.Mode != BindingMode.OneWayToDto
                        && string.Equals(overlay.EntityProperty, entityPrefix + "." + baseBinding.EntityProperty, StringComparison.OrdinalIgnoreCase))
                        diagnostics.Add(new MappingDiagnostic("OverlayEntityWrite", name, overlay, "DtoToEntity",
                            $"Overlay and selected base shape '{baseShape.Name}' both write '{overlay.EntityProperty}'.",
                            "Keep only one reverse-enabled write to that entity member."));
                }
            }

            void Transformations(Type inputType, PropertyBinding binding)
            {
                var dtoType = binding.DtoValueType ?? inputType;
                if (dtoType == typeof(void) || dtoType.IsByRef || dtoType.IsPointer || dtoType.ContainsGenericParameters || !dtoType.IsVisible)
                    diagnostics.Add(Error("DtoValueType", name, binding, "The DTO value type must be visible, closed, and assignable.", "Use a supported CLR value type."));
                if (binding.NestedShape != null)
                    diagnostics.Add(Error("BindingKind", name, binding, "A transformation cannot also select a nested shape.", "Use separate transformation and nested-object bindings."));
                if (binding.IsComputed)
                {
                    if (binding.EntityProperty != null || binding.Mode != BindingMode.OneWayToDto || binding.IsPrimaryKey
                        || binding.ToDtoExpression != null || binding.ToEntityExpression != null || binding.ToDtoInMemory != null || binding.ToEntityInMemory != null)
                        diagnostics.Add(Error("ComputedBinding", name, binding, "Computed values must be forward-only, without an entity path, primary key, or member conversions.", "Use a forward-only computed binding."));
                    Signature(binding.ComputedExpression, binding.ComputedInMemory, inputType, dtoType, "EntityToDto");
                }
                else
                {
                    if (binding.Mode != BindingMode.OneWayToEntity)
                        Signature(binding.ToDtoExpression, binding.ToDtoInMemory, inputType, dtoType, "EntityToDto");
                    if (binding.Mode != BindingMode.OneWayToDto)
                        Signature(binding.ToEntityExpression, binding.ToEntityInMemory, dtoType, inputType, "DtoToEntity");
                }

                void Signature(System.Linq.Expressions.LambdaExpression expression, Delegate callback, Type input, Type output, string direction)
                {
                    if (expression != null && callback != null)
                        diagnostics.Add(new MappingDiagnostic("TransformationConflict", name, binding, direction,
                            "A direction has both an expression and an in-memory delegate.", "Choose exactly one transformation representation."));
                    if (expression == null && callback == null)
                    {
                        if (input != output || binding.IsComputed)
                            diagnostics.Add(new MappingDiagnostic("MissingTransformation", name, binding, direction,
                                $"A transformation from '{input.FullName}' to '{output.FullName}' is required.", "Supply the enabled direction's conversion explicitly."));
                        return;
                    }
                    var invoke = callback?.GetType().GetMethod("Invoke");
                    var validExpression = expression == null || (expression.Parameters.Count == 1
                        && expression.Parameters[0].Type == input && !expression.Parameters[0].IsByRef && expression.ReturnType == output);
                    var validDelegate = callback == null || (callback.GetType().IsVisible && invoke != null && invoke.GetParameters().Length == 1
                        && invoke.GetParameters()[0].ParameterType == input && invoke.ReturnType == output);
                    if (!validExpression || !validDelegate)
                        diagnostics.Add(new MappingDiagnostic("TransformationSignature", name, binding, direction,
                            $"The transformation must accept '{input.FullName}' and return '{output.FullName}'.", "Use an exactly matching expression or delegate signature."));
                    if (direction == "EntityToDto" && expression == null && callback != null)
                        diagnostics.Add(new MappingDiagnostic("ProjectionUnavailable", name, binding, direction,
                            "This forward transformation is in-memory-only.", "Supply an expression to enable selector construction.", MappingDiagnosticSeverity.Warning));
                }
            }

            void CheckMember(MemberTree member, PropertyBinding binding, bool write, bool read)
            {
                if (member.IsProperty)
                {
                    if (member.Property.GetIndexParameters().Length != 0)
                        diagnostics.Add(new MappingDiagnostic("Indexer", name, binding, "Configuration",
                            $"Indexed property '{member.Name}' is not supported.", "Use a non-indexed property or field."));
                    if (read && member.Property.GetGetMethod() == null)
                        diagnostics.Add(new MappingDiagnostic("Getter", name, binding, "EntityToDto",
                            $"Property '{member.Name}' must have a public get accessor.", "Expose a public getter or use a reverse-only binding."));
                    if (write && member.Property.GetSetMethod() == null)
                        diagnostics.Add(new MappingDiagnostic("Setter", name, binding, "DtoToEntity",
                            $"Property '{member.Name}' must have a public set accessor.", "Expose a public setter or use a forward-only binding."));
                }
                else if (write && member.Field.IsInitOnly)
                    diagnostics.Add(new MappingDiagnostic("ReadOnlyField", name, binding, "DtoToEntity",
                        $"Field '{member.Name}' is read-only.", "Use a writable field or a forward-only binding."));
            }

            void Check(string code, PropertyBinding binding, string direction, string fix, Action action)
            {
                try { action(); }
                catch (InvalidOperationException error)
                {
                    diagnostics.Add(new MappingDiagnostic(code, name, binding, direction, error.Message, fix));
                }
                catch (System.Reflection.AmbiguousMatchException error)
                {
                    diagnostics.Add(new MappingDiagnostic(code, name, binding, direction, error.Message, fix));
                }
            }
        }
    }
}
