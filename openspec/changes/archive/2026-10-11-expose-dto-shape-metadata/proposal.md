# Proposal

## Why

External consumers can obtain a generated DTO type and a whole-shape selector, but the entity type, normalized bindings, selected nested shapes, and effective overlay layouts are internal. A future Kkts.Querying package would otherwise duplicate configuration or depend on implementation details to connect public DTO field names to Kkts.Expressions.

## What Changes

- Add an immutable public metadata snapshot for a successfully built, named `Class`: shape identity, entity/DTO types, finite member layouts, binding kinds and directions, value types, ordered keys, and selected versus effective nested shapes.
- Add generated-Type lookup for explicitly declared DTO shapes using weak type keys. Retained metadata and type-based lookup remain usable after builder disposal; synthetic grouping types and parent-context overlay types are explicitly not standalone shape registrations.
- Resolve public DTO paths with deterministic ordinal case-insensitive matching, including renamed, flattened, grouped, nested, overlaid, converted, computed, and recursive fields. Report source provenance separately from a direct forward entity-path alias and expose collection-element boundaries.
- Describe forward execution availability without exposing mutable bindings, builders, internal classes, transformation delegates, or provider-translation claims.
- Defer retrieval/composition of individual field expressions to a narrowly scoped follow-up. Existing whole-shape selectors remain available; this change immediately enables structural direct aliases, shape inspection, and integration validation.
- Document lifecycle, diagnostics, concurrency, recursion, and an illustrative future Kkts.Querying integration using only public APIs.

## Capabilities

### New Capabilities

- `dto-shape-metadata`: Public immutable built-shape metadata, contextual nested layouts, deterministic field resolution, alias/forward-execution classification, lifecycle, and weak-key generated-Type discovery.

### Modified Capabilities

None. The main specification inventory is empty. The completed but unarchived `enhance-runtime-dto-mapping` and `fix-virtual-accessors-and-attribute-copying` changes remain compatibility inputs; their deltas are neither duplicated nor modified.

## Impact

- Add public metadata descriptors and lookup/result types in the existing `Kkts.Dynamic` namespace; add a metadata entry point to `Class`.
- Integrate snapshot construction/publication into `DynamicAssembly.Build` before resolved emission information is cleared, reusing resolved alternatives and actual generated layouts.
- Reuse the weak-key ownership pattern already used by `Mapper`, without storing `Class` or `DynamicAssembly` in registry values.
- Add public-consumer, overlay, recursive, concurrent-read, disposal, failed-build, and collectible-assembly tests to the existing xUnit project targeting .NET 8 and .NET 10; validate the solution with warnings treated as errors.
- Retain the library's `netstandard2.0` target and existing dependencies. No Kkts.Expressions, EF Core, ASP.NET Core, or new package dependency is needed.
- Preserve mapping, selector, partial-update, key-ordering, attribute-copying, accessor-dispatch, and generated-layout contracts. Do not repeat the fixes in the other active change.

## Non-goals

No Kkts.Querying implementation, filter parsing, sorting, pagination, execution, SQL, provider capability detection, query authorization, request-controlled shape generation, projection caching, collection-element query/update feature, CRUD, or aggregation. No new per-field expression API or changes to existing selector semantics in this change.
