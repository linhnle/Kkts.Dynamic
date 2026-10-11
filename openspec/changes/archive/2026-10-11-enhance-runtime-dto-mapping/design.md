# Design

## Context

See [proposal.md](./proposal.md) for motivation and the six capability deltas for behavioral requirements.

Observed implementation:

- `DynamicAssembly.DeclareClass` normalizes and copies mutable `PropertyBinding` objects before immediately defining a type shell. Syntax, duplicate paths/writes, and mode errors currently throw argument exceptions at declaration.
- `Build` validates registered classes before properties/methods are emitted. Any build failure permanently faults the assembly. Successful build clears emission dictionaries while retaining binding information for selectors.
- `Class.BuildProperties` infers DTO types from entity paths and registered alternatives. `FindClass` currently selects the first class with the matching entity type; multiple root declarations already exist, but nested choice is order-dependent.
- Nested overlays currently reuse the registered alternative class's property builders. Adding a parent-specific field beneath a mapped alternative can therefore mutate the shared child DTO layout, as exercised by the existing `NestedAlternativeSelectorsAndFlattenedBindingsShareTheSameDtoNode` regression scenario.
- Forward/reverse mapping uses separate IL trees. Selector construction uses a separate expression tree with null guards and recursion limits.
- `Mapper` dispatches using the generated DTO type and caches mapping delegates and factories with weak type keys. Several `MethodInfo` lookups use name-only reflection, making mapper overload additions potentially ambiguous.
- Full reverse mapping skips null DTO subtrees and null mapped alternative objects, but assigns direct null scalars/collections. Mapped arrays/collections are replaced, not reconciled.
- The README and xUnit regression tests codify these behaviors, disposal/fault semantics, direct exception propagation, and collectible caches. Tests target .NET 8 and .NET 10; the library targets .NET Standard 2.0. No database provider is currently tested.

## Goals / Non-Goals

**Goals:**

- Share normalized binding metadata across declarations, validation, emission, selectors, and partial updates so these surfaces cannot independently choose types or directions.
- Preserve the simple existing member-binding path and emitted mapping methods; add rich metadata only for opted-in features.
- Keep metadata/delegates tied to generated-type lifetime, including after builder disposal, without static strong-key type registries.
- Provide exact, testable behavior for field presence and projection support before implementation.

**Non-Goals:**

- Handwritten destination types, convention-based mapping, automatic inverse computations, or automatic conversion discovery.
- Serializer integration, JSON Patch operations, assignment tracking, individual collection-element PATCH, key-based merging, graph cycles, or transactional rollback.
- Database-provider translation guarantees, provider-specific rewrites, or implicit client evaluation.
- New runtime targets, external packages, a public replacement for `IDtoObject`, or unrelated refactors/performance targets.

## Decisions

### 1. Add a fluent facade over the same binding model

Proposed public surface:

```csharp
Class DeclareClass<TEntity>(
    string name,
    Action<BindingBuilder<TEntity>> configure);

BindingBuilder<TEntity> Map<TMember>(
    Expression<Func<TEntity, TMember>> entityPath,
    string dtoPath = null,
    BindingMode mode = BindingMode.TwoWay,
    bool isPrimaryKey = false,
    int primaryKeyOrder = 0,
    string nestedShape = null);
```

`TEntity` has a reference-class constraint and the existing visibility/closed-type checks. The builder also provides a string-member overload accepting `PropertyBinding` so configuration and typed mappings share one declaration. Add `NestedShape` to `PropertyBinding`.

A small path extractor walks property/field member expressions back to the exact entity parameter. Remove only a terminal boxing conversion; reject computations, method calls, indexers, static access, captured objects, and meaning-changing casts. Do not reinterpret member paths as general computations. Getter-only/read-only members remain legal only in forward directions; setter-only members remain available through strings because a C# read expression cannot represent them.

Keep existing normalized ordering and case-insensitive DTO paths. Build a complete local binding collection and validate before registration; callback failures leave the class registry unchanged. Snapshot all public metadata and retain original binding indices for diagnostics before sorting. Equivalent plain typed/string declarations share existing redeclaration behavior.

Alternative: introduce an unrelated typed mapper or typed DTO proxy. Rejected because destination types exist only at runtime and a second mapping pipeline would diverge.

### 2. One-call creation reuses mapper factories

Add `object Class.CreateDto(object entity)`. Require a usable, built declaration and a non-null compatible entity; do not implicitly call `Build`. Expose an internal `Mapper` helper using the existing weak-key factory and mapping dispatch to validate, create, and map. Return `object` because the type is runtime-defined.

Do not add a public overload to an existing mapper name: current name-only `GetMethod` lookups would otherwise become ambiguous. Existing two-argument mapping null no-ops stay unchanged; the new creation API rejects null to avoid returning a success-shaped empty DTO.

Alternative: use `Activator.CreateInstance` on each call or return `dynamic`. Rejected in favor of the existing cached factory and a clear runtime-type contract.

### 3. Transformations declare value type, direction, and execution kind

Extend binding metadata with `DtoValueType`, directional `LambdaExpression` transformations, explicitly named in-memory delegate alternatives, and a forward computed expression/delegate. A binding is exactly one of ordinary member, converted member, or computed value. Validate conflicting options instead of selecting one silently.

The fluent builder exposes `MapConverted<TMember, TDto>` with typed forward/reverse expressions, `MapConvertedInMemory<TMember, TDto>` with typed delegates, `Compute<TDto>` with `Expression<Func<TEntity, TDto>>`, and `ComputeInMemory<TDto>` with `Func<TEntity, TDto>`. Raw bindings can combine expression-backed forward conversion with a reverse-only in-memory delegate. Each direction accepts exactly one transformation representation.

- Converted member inputs/outputs must match the resolved entity member and DTO value types exactly; no implicit coercion or inferred inverse. Require transformations for enabled directions whose types differ.
- Computed bindings have no `EntityProperty`, are always forward-only, and cannot be keys. Infer the DTO property type from their declared result.
- Transformations produce their declared final DTO/entity values; they do not also perform registered nested-shape substitution. Require a separate ordinary binding for nested shape mapping.
- A converted key remains a top-level member binding and `GetIds` reads its DTO-side value.
- Do not copy entity validation attributes to a converted property with a different CLR type or to a computed property; retain existing attribute copying for unchanged member types.

Compile expression transformations once for runtime mapping. Store typed runtime delegates in generated-type-owned static fields initialized before successful build returns; emit strongly typed delegate invocations in the mapping methods, preserving direct exceptions. No global strong-key delegate store. For projection, substitute the conversion input/entity parameter into the original expression body rather than adding `Expression.Invoke` or a compiled-delegate constant. Keep ordinary path guards; computed values own null-navigation/culture/formatting choices. Runtime mapping and compiled selectors must agree for pure expression transformations and the same input.

Validate selector availability recursively through selected shapes. Delegate-only active forward bindings are valid for in-memory build but produce warnings; `BuildSelectorExpression` throws a descriptive `InvalidOperationException` before returning/caching a partial selector. Reverse-only delegates do not block selectors. Providers remain responsible for unsupported database expressions.

Alternative: compile everything and invoke delegates in projections, or restrict all users to expressions. Rejected because the former conceals translation limits and the latter excludes deliberate in-memory transformations chosen by the user.

### 4. Resolve nested shapes once, by explicit name or unambiguous type

Keep assembly-wide class names as shape identities, not an additional entity-to-default-shape registry. Resolve `NestedShape` before emission against the exact object/array/collection element entity type. Allow forward references within the same assembly; missing/mismatched references are validation errors. Zero matches retain direct assignment, one match retains implicit substitution, and multiple matches require a name.

Share resolved alternatives with property emission, both IL trees, selector trees, and PATCH metadata. Include selected shape names, result types, binding kinds, and transformation identities in `Class.Matches`. Compare transformation expression/delegate instances conservatively by reference identity, not arbitrary structural/executable equivalence. Different behavior must never be treated as an equivalent redeclaration.

#### Per-use derived layouts for nested overlays

Preserve overlays such as mapping `Child` to `Nested.Child` and `Child.Id` to `Nested.Child.ExtraId`, but stop adding `ExtraId` to the registered child class's shared property builders.

Before emission, construct a logical per-use DTO layout from the selected shape's immutable definition plus the parent's relative overlay DTO paths. "Derived" means composition of the binding/layout definition, not a requirement for CLR inheritance. A nested use with additions gets an internal generated layout; a use without additions retains the exact selected base type. Internal layouts are not user-declared shapes and must not participate in name selection or entity-type ambiguity counts.

Base binding behavior remains relative to the child entity. Overlay behavior remains relative to the owning parent entity, even when its source is outside the mapped child. Do not naively rebase every entity expression onto the child or merge parent-specific computations into a reusable child mapper. The internal child layout provides base mapping methods and key extraction; the parent's mapping/selector metadata owns its overlay writes and computations.

Resolve property types and validate base/overlay path conflicts logically before touching property builders. Compatible repeated leaf definitions can share a leaf; incompatible types, directions, entity destinations, transformation identities, or scalar/grouping conflicts produce actionable diagnostics rather than silent first-write wins. Build independent property-builder dictionaries for each layout instead of sharing mutable dictionaries with the registered shape. Treat overlapping reverse entity writes from base and overlays using the existing conflict policy after resolving their full entity paths.

Parent selectors compose base bindings and overlays into one effective child node with the existing null guards. Parent runtime mapping uses the same effective child type and applies each parent overlay once. PATCH descendant resolution includes effective overlay leaves; whole-object selection runs the base child reverse mapping and the applicable parent-scoped overlays once, while retaining the overlap rejection for simultaneously supplied whole-object/descendant paths. Forward-only overlays never become editable through whole-object selection.

Keep effective layout and update metadata owned by the generated types/weak-key caches so disposal and collectibility requirements still hold. Test two parents with different overlays, a root base DTO, declaration-order reversal, and overlays sourced from non-child parent members. Existing overlay value behavior stays supported; the intentional change is isolation from unrelated uses.

This intentionally tightens existing order-dependent nested behavior. Root multiple-shape declarations already work and remain supported; only ambiguous nested uses need migration. Keep recursion limits and collection restrictions unchanged.

Alternative: first-registration wins or add configured defaults. Rejected per the user's choice because explicit binding selection is deterministic and local.

### 5. Shared non-emitting validation, with a raw-candidate entry point

Add `MappingValidationReport` and immutable diagnostic entries with code, severity, class name, original binding index, available paths, direction/scope, message, and suggested fix. `IsValid` means no errors, not no warnings. Sort deterministically by class name, original index, scope, and diagnostic code.

Expose `DynamicAssembly.Validate()` for registered declarations and `ValidateClass(string name, Type mappingType, IEnumerable<PropertyBinding> bindings)` for a raw candidate considered alongside existing declarations without registration. The raw entry point is necessary because legacy declaration syntax errors must still throw before a class is registered. Validate candidate name conflicts as well as binding content.

Extract independently evaluable rules from `NormalizeBindings`, `Class.ValidateBindings`, and alternative lookup. Validation resolves CLR member information and logical DTO path/type trees, including composed base/overlay layouts, without defining new properties, methods, or completed types. Existing `TypeBuilder` shells from valid declarations are not finalized. Skip checks dependent on an unresolved member, but continue other bindings/classes; catch only expected configuration-resolution failures when converting them into diagnostics. Unexpected exceptions propagate.

`Build` invokes the same validation after entering its existing build state, before property emission. Throw `MappingValidationException : InvalidOperationException` carrying the report when errors exist, preserving callers' existing `InvalidOperationException` catch behavior. Update xUnit exact-type assertions specifically for configuration build failures to expect the new subtype and inspect its report; lifecycle errors keep their existing exact types. The enclosing build fault logic remains intact. Existing declaration-time rules use the same helpers but retain fail-fast `ArgumentException` behavior. New `Validate` calls do not fault the assembly or mutate candidate inputs.

Alternative: delay all declaration validation until build, or collect errors only from registered classes. Rejected because the former breaks callers/tests and the latter cannot report malformed raw configurations.

### 6. PATCH uses presence-selected bindings, never full mapping followed by restoration

Add a distinctly named API:

```csharp
void Mapper.MapPartialFromDtoToEntity(
    object dto, object entity, IEnumerable<string> suppliedDtoPaths);
```

Keep the marker interface and generated full-mapping signatures unchanged. Attach immutable resolved partial-update metadata to the generated DTO type through a weak-key cache at successful build. Extend the existing mapping cache only as needed to expose the plan; DTO use must survive builder disposal. Compile/cache per-binding read/convert/write operations once, not per path-set combination. Do not keep unbounded caches of arbitrary supplied sets.

Request processing:

1. Validate non-null inputs and exact generated DTO/entity compatibility, then enumerate supplied paths once into a case-insensitive set.
2. Resolve the entire set to reverse-enabled mapped bindings, including named nested-shape and effective per-use overlay descendant leaves. Reject unknown, synthetic grouping, forward-only/computed, indexed/element, and overlapping whole-object/descendant paths before any reads or writes.
3. Read only selected values. Reject inaccessible selected descendants under null DTO parents. Prepare reverse conversions and check assignability/null-to-non-nullable errors before mutation. No entity-parent construction occurs during this phase.
4. Apply selected writes in normalized binding order, creating only required entity parents. Directly supplied null mapped objects explicitly clear the target; do not use the legacy alternative-object null-preservation branch.
5. Non-null whole mapped objects use their selected base shape's full reverse mapping plus any reverse-enabled parent-scoped overlays from their effective per-use layout, each once. Whole collections use existing replacement helpers and preserve null elements. A presence path never addresses an individual collection element.

Selecting `Nested.Name` requires an accessible DTO value. Selecting a directly mapped `Child` with null clears it; a synthetic DTO grouping path such as `Fields` is not an atomic update target. Empty presence is a validated no-op. Null direct inputs are errors only for the new PATCH API.

Configuration/path/value validation is pre-mutation; arbitrary constructors/setters, nested full mapping, or user code can still throw while applying. No rollback guarantee. Omitted bindings cannot execute getters/converters or instantiate parents. Full reverse mapping retains all existing null semantics.

Alternative: ignore nulls, setter-based presence tracking, or full mapping then restoring omitted values. Rejected because none provide the chosen explicit presence contract without unwanted writes or serializer coupling.

## Risks / Trade-offs

- [Cross-surface divergence] -> Reuse normalized/resolved metadata and require parity tests across normal mapping, fresh creation, selectors, shapes, and PATCH conversions.
- [Breaking ambiguous nested lookup] -> Document migration to `NestedShape`; test declaration-order independence and preserve zero/single-match behavior.
- [Shared layout mutation or wrongly rebased overlays] -> Compose immutable per-use layouts before emission, keep overlay behavior parent-scoped, and verify base/root isolation plus non-child-source overlays across mapping, selectors, and PATCH.
- [Projection translation] -> Inline expression bodies, reject delegate-only projections, and clearly document that passing compiled/LINQ-to-Objects tests is not proof of SQL translation.
- [Collectible-type retention] -> Avoid static strong-key registries, keep transformation delegates type-owned, and extend existing collectible-cache and post-disposal mapping regression coverage.
- [PATCH writes before user exception] -> Preflight configuration and selected values; document nontransactional assignment, propagate original exceptions, and never imply rollback.
- [Validation duplication or cascading noise] -> Extract shared checks, carry original indices, and suppress only dependent checks after an upstream resolution failure.
- [Attribute/type mismatch] -> Preserve attributes only on same-type member properties; document why converted/computed properties do not inherit incompatible metadata.
- [Large scope] -> Implement in independently testable stages, starting with the recommended typed-bindings/creation pair, then validation, shapes, transformations, and PATCH.

## Migration Plan

1. Land additive typed declarations and creation APIs with existing regression tests unchanged.
2. Add shared validation and named nested selections. Update ambiguous bindings to name their intended shape before adopting the new resolution rule.
3. Add transformations and explicit projection warnings/failures; callers choose expressions when selectors are required.
4. Add PATCH as a separate opt-in API. Callers derive presence from their request format; migrate no legacy reverse-mapping callers automatically.
5. Update README examples and compatibility notes and run tests on both existing target frameworks plus warnings-as-errors build.

The ambiguity tightening requires an appropriately versioned release and release-note callout. No release publishing or package-version edit is part of this change. Rollback is package-version rollback; newly used APIs/configuration must be removed by the caller. Existing DTOs are runtime artifacts, so rebuild assemblies from declarations when switching versions.
