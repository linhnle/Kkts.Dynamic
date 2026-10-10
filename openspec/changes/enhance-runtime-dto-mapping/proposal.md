# Proposal

## Why

Runtime-defined DTOs currently require string entity paths and repeated creation/mapping boilerplate, while richer shapes and safe partial updates need contracts the library does not expose. Strengthen this runtime-DTO workflow without turning the package into a general-purpose mapper or changing existing mapping semantics.

## What Changes

- Add a generic fluent declaration API with compiler-checked entity property/field expressions and string DTO paths; retain configuration-driven `PropertyBinding` declarations.
- Add `Class.CreateDto(entity)` to create and map the selected built DTO in one call.
- Add explicit DTO-value types, directional conversions, and forward-only computed properties. Support expressions and explicitly in-memory-only delegates; fail selector construction for active delegate-only transformations, never silently fall back to client evaluation.
- Add opt-in partial reverse mapping using a caller-supplied set of DTO paths. Omitted paths preserve entity values; supplied nulls clear nullable targets. Collections are supplied as whole values, without element-level PATCH.
- Make multiple named shapes for an entity deliberate and usable in nested objects/collections. **BREAKING for ambiguous nested registrations:** require explicit nested selection instead of choosing the first matching declaration. Unambiguous registrations retain existing behavior.
- Preserve nested overlay bindings through per-use derived DTO shapes, leaving registered base shapes unchanged rather than adding the parent's extra fields to a shared child shape.
- Add non-emitting validation reports aggregating configuration errors, with paths, directions, and actionable explanations. Preserve legacy declaration failures and failed-build lifecycle rules.
- Document all APIs, compatibility boundaries, null behavior, and query-provider limitations; stage implementation with typed bindings and one-call mapping first.

## Capabilities

### New Capabilities

- `typed-bindings`: Typed fluent declarations sharing existing string-binding semantics.
- `dto-creation`: One-call creation and entity-to-DTO mapping for a selected built class.
- `binding-transformations`: Explicit conversions and computed DTO values with projection contracts.
- `partial-updates`: Caller-supplied field presence for opt-in reverse mapping.
- `dto-shapes`: Multiple named DTO shapes and explicit nested shape resolution.
- `mapping-validation`: Aggregate, non-emitting configuration diagnostics.

### Modified Capabilities

None. The project currently has no main capability specifications.

## Impact

- Public surfaces: `DynamicAssembly`, `Class`, `PropertyBinding`, `Mapper`, and new typed binding/validation types. Keep `IDtoObject` unchanged and retain existing method signatures and generated mapping methods.
- Internals: binding normalization, member validation, alternative-type resolution, per-use shape composition, DTO property emission, both mapping trees, selector trees, and weak-key mapping/factory caches.
- Tests: extend the xUnit project targeting .NET 8 and .NET 10, retaining the original scenarios and regression coverage.
- Documentation: update the package README and examples during implementation.
- Keep `netstandard2.0`, runtime code generation, collectible assemblies, and the existing dependency footprint. No serializer, HTTP PATCH format, database-provider dependency, arbitrary handwritten DTO mapping, collection reconciliation, or Native AOT support is added.
