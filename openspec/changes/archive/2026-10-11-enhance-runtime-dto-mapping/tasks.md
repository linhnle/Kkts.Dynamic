# Tasks

## 1. Typed fluent declarations

- [x] 1.1 Add the parameter-rooted member-path expression extractor with terminal-boxing support; verify focused tests accept nested properties/public fields and reject calculations, calls, indexers, static/captured members, and meaning-changing casts with actionable argument errors.
- [x] 1.2 Add `BindingBuilder<TEntity>` and generic `DynamicAssembly.DeclareClass<TEntity>` with chainable member bindings, string binding inclusion, directions, and primary-key options; verify typed/string parity for generated types, both mapping directions, selectors, and key ordering using existing original scenarios.
- [x] 1.3 Snapshot fluent configuration through shared normalization and preserve default DTO paths and equivalent redeclarations; verify callback/expression failures register no class, later builder mutation has no effect, and conflicting redeclarations still fail.
- [x] 1.4 Add typed quick-start and member-expression restrictions to the README; verify the documented example through a matching test and run the typed-binding test class on both existing test frameworks.

## 2. One-call DTO creation

- [x] 2.1 Add `Class.CreateDto(object entity)` using the existing weak-key mapper factory and dispatch, without implicitly building or overloading existing mapper names; verify exact selected type, fresh instances, forward direction behavior, nested values, and identifiers against explicit activation/mapping.
- [x] 2.2 Add creation null/type/lifecycle/direct-exception tests; verify null and incompatible entities fail explicitly, unbuilt/faulted/disposed declarations retain lifecycle errors, getter exceptions propagate, and legacy mapper null no-ops remain intact.
- [x] 2.3 Update the README quick-start with one-call creation and lifecycle/null requirements; verify the documented example matches an executable test on .NET 8 and .NET 10.

## 3. Shared aggregate validation

- [x] 3.1 Extract shared binding/member/constructor/path-conflict checks from normalization and `Class` validation while retaining original binding indices; verify legacy declaration argument failures, caller input immutability, direction-specific accessor rules, and valid original/regression mappings remain unchanged.
- [x] 3.2 Add immutable diagnostic/report types plus registered and raw-candidate validation entry points; verify one report contains malformed-path, duplicate-path, missing-member, accessor, constructor, and primary-key errors with stable codes, paths, directions, indices, corrective guidance, and deterministic ordering; verify the diagnostic model also identifies selected-base/overlay conflicts when composition is introduced in group 4.
- [x] 3.3 Ensure validation uses logical member/DTO metadata without type/property/method completion or registration; verify invalid raw validation leaves registry and caller bindings unchanged and a valid subsequent declaration/build succeeds.
- [x] 3.4 Make build run shared validation before emission and throw the report-carrying invalid-operation subtype; update only configuration-build exact-type xUnit assertions and verify faulted retries retain the original failure as their inner exception, no incomplete types escape, successful build remains idempotent, and disposal behavior is unchanged.
- [x] 3.5 Document both validation APIs, report fields, fail-fast declaration compatibility, and the need for a new assembly after failed build; verify README examples against validation tests on both frameworks.

## 4. Multiple and nested DTO shapes

- [x] 4.1 Add and snapshot `NestedShape` metadata in string/fluent bindings and declaration equivalence; verify root summary/detail/edit shapes remain independent and redeclaration with a different selected shape is rejected.
- [x] 4.2 Resolve named object/array/supported-collection shapes once and share alternatives across validation, property emission, both IL trees, and selectors; verify explicit selection, forward references, exact element-type checks, missing/wrong-type diagnostics, and declaration-order independence.
- [x] 4.3 Replace first-match implicit nested resolution with zero-match direct assignment, single-match automatic substitution, and multiple-match validation failure; verify existing unambiguous mappings and null/collection behavior, ambiguity guidance, primary-key output, and selector recursion errors.
- [x] 4.4 Document summary/detail/edit examples and the intentional ambiguous-nested-registration migration to explicit names; verify object and collection examples through shape tests on both frameworks and retain original/regression coverage.
- [x] 4.5 Compose immutable per-use derived layouts for nested overlays before emission, retaining parent-scoped overlay sources and child-scoped base bindings; verify internal layouts do not enter public shape resolution, incompatible base/overlay definitions fail validation without mutating base declarations, and overlays sourced from other parent members map correctly.
- [x] 4.6 Add isolation/parity tests and README documentation for composed nested overlays; verify two parents with different overlays, root base-shape creation, declaration-order reversal, forward/reverse mapping, selectors, and key extraction agree without leaking properties or executing overlays twice, while the existing nested-overlay regression behavior stays supported.

## 5. Conversions and computed properties

- [x] 5.1 Extend normalized binding metadata and the fluent API for explicit DTO value types, directional expression/in-memory transformations, and forward-only computed values; verify snapshots/equivalence, enum-to-string typing, computed result inference, missing reverse conversions, signature mismatch, invalid computed keys/directions, and incompatible transformation/shape combinations through shared validation.
- [x] 5.2 Extend DTO property and IL mapping emission to invoke strongly typed compiled transformations owned by generated types; verify enum/string two-way conversions, formatted values, multi-member computed values, converted primary keys, intermediate/direct-null behavior, and original converter exceptions without reflection wrapping.
- [x] 5.3 Preserve same-type member attribute copying and suppress incompatible copied attributes for changed-type/computed values; verify emitted property metadata with tests for ordinary, same-type converted, changed-type converted, and computed bindings.
- [x] 5.4 Compose expression transformations into selectors via parameter substitution and add recursive projection-availability checks; verify fresh-runtime/compiled-selector/LINQ-to-Objects parity, no compiled-delegate invocation nodes, explicit root/nested delegate-only failures, reverse-only delegate acceptance, and no partial selector caching after a failure.
- [x] 5.5 Add projection-unavailable warnings to validation and extend generated-type lifetime tests for transformation delegates; verify warnings do not invalidate in-memory build, mapping survives builder disposal, concurrent distinct-object mapping remains supported, and transformed DTO types can be collected after references are released.
- [x] 5.6 Document conversion/computed examples, explicit inverse and null handling, attribute behavior, in-memory-only APIs, and query-provider limitations; verify examples through focused transformation tests on both frameworks without adding a database-provider package.

## 6. Explicit partial updates

- [x] 6.1 Add the distinctly named partial reverse-mapping API and immutable weak-type-owned update plans from resolved bindings, including selected nested-shape and effective per-use overlay descendants; verify generated DTO/entity compatibility, null argument errors, single enumeration, case-insensitive deduplication, empty-set behavior, and post-disposal plan usability.
- [x] 6.2 Validate the complete presence set before reads/writes; verify unknown, synthetic grouping, forward-only/computed, indexed/element, and whole-object/descendant overlap paths fail with actionable errors before any mutation.
- [x] 6.3 Prepare only selected DTO values and conversions before assignments; verify null DTO-parent errors and null-to-non-nullable converted results fail before mutation, and omitted getters/converters are never executed.
- [x] 6.4 Apply selected scalar/nested writes with only necessary parent construction and reverse conversions; verify omitted/null/value outcomes, flattened paths, fields, multiple DTO groups targeting one entity child, preserved sibling values, and configured converter/constructor/setter exception behavior.
- [x] 6.5 Add explicit null clearing for supplied mapped objects and existing-policy whole-object/array/collection replacement; verify selected shapes, effective per-use overlays applied once for whole-object selection, overlay-descendant path acceptance only in the declaring parent, forward-only overlay exclusion, preserved null elements, omitted parent/collection preservation, no element-level reconciliation, and legacy full-mapping null semantics remain unchanged.
- [x] 6.6 Extend cache/lifetime and concurrency tests for PATCH without caching arbitrary presence combinations; verify distinct-object concurrent updates, generated-type collectibility, and no dependency on disposed emission dictionaries.
- [x] 6.7 Document caller-supplied presence, atomic mapped-object versus synthetic grouping paths, whole-collection policy, clearing semantics, and nontransactional user-code failures; verify the three-state PATCH examples with tests on both frameworks.

## 7. Cross-feature integration validation

- [x] 7.1 Add an end-to-end typed summary/detail/edit scenario combining explicit nested shapes, isolated per-use overlays, keys, conversions, computed values, one-call creation, aggregate diagnostics, projection, and supplied-path updates; verify selected-shape/type/value consistency, base-shape isolation, and omitted/cleared/updated outcomes on .NET 8 and .NET 10.
- [x] 7.2 Run `dotnet test src/Kkts/Kkts.Dynamic.UnitTest/Kkts.Dynamic.UnitTest.csproj` and `dotnet build src/Kkts/Kkts.sln -warnaserror`; verify both target-framework suites and warnings-as-errors build pass with unchanged `netstandard2.0` and no new dependency footprint.
- [x] 7.3 Run the existing Release performance smoke benchmark without inventing new thresholds; verify it completes using the existing mapping path, retain the output for comparison, and investigate only regressions attributable to this change.
