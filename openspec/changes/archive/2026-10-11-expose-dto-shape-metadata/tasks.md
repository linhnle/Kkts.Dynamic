# Tasks

These are implementation tasks for a later apply request, not authorization to edit source during proposal creation. See [design.md](./design.md) and [the metadata delta](./specs/dto-shape-metadata/spec.md). Groups are ordered by prerequisites; each group includes its own tests and directly related documentation.

## 1. Public API contract

- [x] 1.1 Define the proposed sealed immutable shape, layout, member, binding, resolved-field, resolution, and diagnostic types in `Kkts.Dynamic`, with binding/node/execution classifications and documented nullability; verify the library builds for its existing `netstandard2.0` target without new dependencies.
- [x] 1.2 Add public-consumer API surface tests covering getter-only descriptors, read-only lists, exclusive success/diagnostic results, and absence of public bindings/builders/internal types/delegates; verify the tests compile against only the public project reference and pass for the completed surface.
- [x] 1.3 Add XML/API documentation for source provenance versus direct forward alias, original mode versus effective direction, root versus nested keys, and selected versus effective layouts; verify every public descriptor/classification is documented without provider-translation or authorization claims.

## 2. Metadata graph construction

- [x] 2.1 Integrate private snapshot staging into `DynamicAssembly.Build` after resolved types/runtime registrations exist and before `Class.Clear`, reusing resolved alternatives and emitted member layouts; verify tests compare the complete finite metadata member layout with actual generated DTO properties for all six binding kinds and grouping nodes.
- [x] 2.2 Capture declaring source contexts and parent-owned overlay bindings, preserving selected registered shape identity separately from effective derived types; verify parent `Title -> Child.Title`, deep overlays, multiple parents, unoverlaid uses, and reversed declaration order against `DtoShapeTests` expectations without changing base metadata.
- [x] 2.3 Construct recursive shape/layout references using a finite identity graph and freeze it before publication; verify self-recursive and mutually recursive shapes build metadata and enumerate immediate members without recursive expansion or incomplete nodes.
- [x] 2.4 Copy immutable key/value/binding facts without retaining mutable bindings, emission objects, or callbacks; verify caller binding mutation cannot alter metadata and root key order, converted key types, tie ordering, and nested key designation match identifier extraction.
- [x] 2.5 Document finite layout enumeration, selected versus effective nested types, and parent-scoped overlay provenance in the packaged README; verify examples describe actual emitted layouts and explicitly require identity-based cycle handling for consumer recursion.

## 3. Public field resolution and alias classification

- [x] 3.1 Implement `ResolveField` using canonical emitted DTO names and ordinal case-insensitive finite segment traversal, with explicit invalid/unknown/no-nested-shape diagnostics; verify renamed, flattened, grouped, mixed-case, empty/null, empty-segment, padded, wildcard, unknown, and unregistered-object descendant cases.
- [x] 3.2 Compose inherited entity paths and effective directions from mapped ancestors while preserving independent parent-owned overlay context/direction; verify selected summary versus detail shapes, nested keys, forward-only/reverse-only leaves, restricted parents, independent overlays, and deep overlay source paths.
- [x] 3.3 Implement conservative `DirectEntityAlias` eligibility independently of source provenance and transformed execution kind; verify direct forward fields produce aliases while converted, same-type converted, computed, in-memory-only, reverse-only, and mapped whole-object substitutions never become misleading aliases.
- [x] 3.4 Report structural array/collection descendants with immutable collection-boundary information, but no root scalar alias or element operation; verify selected array/collection element layouts, leaf versus collection value types, multiple boundaries, and rejection of index syntax.
- [x] 3.5 Resolve finite repeated recursive paths without full graph expansion, selector-cache use, or per-path caches; verify `Child.Child.Id`, mutually recursive paths, and missing terminal segments, including when existing whole-shape selector construction fails its recursion limit.
- [x] 3.6 Add the public-only direct-alias adapter example and diagnostic handling to the README, including projected-default/null and collection-boundary caveats; verify an equivalent test builds canonical renamed/flattened/grouped aliases without accessing internals or referencing Kkts.Expressions.

## 4. Expression boundary and compatibility regressions

- [x] 4.1 Expose forward execution classification from enabled original binding facts, distinguishing direct member access, expression transformation, in-memory transformation, nested mapping, and no forward operation; verify expression-forward/delegate-reverse, nested delegate-only fields, computed expressions/delegates, and reverse-only cases without invoking callbacks.
- [x] 4.2 Keep individual expression retrieval/composition out of this implementation and document the scoped follow-up's fresh-projected-value, null/presence, inlining, recursion, and unsupported-operation contract; verify the public surface adds no per-field expression API and documentation identifies exactly which future converted/computed querying operations remain blocked.
- [x] 4.3 Add regressions proving metadata construction/read access does not change generated layouts, attribute-copying eligibility, virtual accessor results, intermediate-null skips, direct-null values, partial-update behavior, or selector exceptions; verify these alongside `AttributeCopyingTests`, `MappingRegressionTests`, `PartialUpdateTests`, `DtoShapeTests`, and `TransformationTests`, reusing their models/helpers where practical rather than duplicating the other active change's fixes.

## 5. Successful-build lifecycle and weak Type discovery

- [x] 5.1 Publish completed snapshots through `Class.GetMetadata` and an assembly-wide detached readiness boundary only after every build phase succeeds; verify pre-build, repeated successful build/access, early fault, and a deterministic late failure after type emission expose no partial metadata, with existing fault context preserved.
- [x] 5.2 Add weak-key generated-Type retrieval for explicitly declared root shapes and diagnostic entries for synthetic grouping/derived overlay types, with no static entity/name lookup; verify exact named shape selection, contextual-type diagnostics, unregistered-type diagnostics, null Type exceptions, and throwing versus `TryGetForType` results.
- [x] 5.3 Preserve snapshots through Class/assembly disposal without retaining mutable builder state; verify disposed Class access throws while retained snapshots and public Type lookup still inspect bindings, resolve overlays, and traverse finite recursion consistently.
- [x] 5.4 Add bounded no-inlining weak-reference lifecycle tests for ordinary, transformed, recursive, and overlaid metadata after Type lookup, including deliberate snapshot retention followed by release; verify retained metadata remains usable and static discovery does not prevent collection after all type/DTO/layout/result/snapshot references are released.
- [x] 5.5 Add concurrent enumeration, valid/invalid field-resolution, and Type-lookup tests after successful publication, including after disposal; verify race-free stable results without locks imposed on callers or use of the mutable Class selector cache.
- [x] 5.6 Document the lifecycle table, contextual Type-discovery boundary, intentional metadata retention, collectible ownership, and concurrent-read contract in the README/API docs; verify examples work after builder disposal and neither promise concurrent configuration/build nor hide unsupported discovery.

## 6. End-to-end acceptance validation

- [x] 6.1 Run the targeted metadata tests with adjacent shape/transformation/lifecycle regressions on both `net8.0` and `net10.0`, then run `dotnet test src/Kkts/Kkts.Dynamic.UnitTest/Kkts.Dynamic.UnitTest.csproj --configuration Release` for the full multi-target suite; verify every existing and new test passes on both frameworks and retain framework-specific results.
- [x] 6.2 Run `dotnet build src/Kkts/Kkts.sln --configuration Release -warnaserror`; verify the whole solution builds with warnings treated as errors, the library still targets `netstandard2.0`, and dependency manifests contain no new packages.
- [x] 6.3 Check the implemented public integration example, diagnostics, and lifecycle behavior against every delta requirement and confirm only this change's source/docs/tests were touched; verify no querying implementation, provider promises, standalone contextual shape claims, or unrelated accessor/attribute fixes were introduced.

Acceptance validation: 163/163 tests passed on `net8.0` and 163/163 on `net10.0` in Release. The Release solution build with `-warnaserror` completed with zero warnings/errors. Dependency manifests are unchanged; the library remains `netstandard2.0`. Strict OpenSpec validation passed.
