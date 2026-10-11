# Design

## Context

See [proposal.md](./proposal.md) for motivation and [the metadata delta](./specs/dto-shape-metadata/spec.md) for the public contract.

Source inspection establishes:

- `Class` exposes its name, generated type, creation, and whole-shape selector, but `MappingType`, `Bindings`, alternatives, and nested emission dictionaries are internal.
- `DynamicAssembly.Build` resolves bindings, creates isolated derived layouts, emits properties/methods/types, initializes transformations, registers partial-update plans, then calls `Class.Clear`. Snapshot construction must happen before that final clearing.
- `Class.ResolveBindings` substitutes the effective derived class into a binding's resolved alternative. `DynamicAssembly.DeriveShape` copies base bindings and carries additional layout paths; actual overlay bindings remain owned by the parent. A derived class's bindings alone therefore do not describe the complete effective nested use.
- `DtoShapeTests` demonstrate parent-scoped overlays: `Child.Title` can source `Parent.Title`, not `Child.Title`. Deep overlays can cross multiple mapped-object boundaries. Base and unrelated layouts must remain unchanged.
- Generated assemblies use `RunAndCollect`. `Mapper` already uses `ConditionalWeakTable` for mapping, factories, and partial updates; its detached plans survive disposal. `Class.Dispose` clears resolved alternatives, so retaining the declaration is not a suitable metadata ownership model.
- `SelectMemberBindingExpressionTree` preserves source guards, synthetic-group presence, mapped-object presence, and collection null elements. It inlines expression transformations via `BindingExpressions`, rejects delegate-only forward bindings, and fails explicitly on recursive full projection. These behaviors must not be reimplemented in a metadata resolver.
- Tests already target `net8.0;net10.0`; the library targets `netstandard2.0`. Existing diagnostics use immutable properties, read-only collections, stable codes, and corrective guidance.
- There are no main specs. Both other active changes are complete but unarchived. Their shape/transformation, accessor-dispatch, and attribute-copying deltas constrain this additive change; current source already contains those fixes.

## Goals / Non-Goals

**Goals:**

- Publish a durable public description of the exact generated layout and binding provenance, with finite, cycle-safe structural traversal.
- Separate source-member provenance, direct forward aliases, configured expression representation, and provider translation.
- Support consumers that retain either a declaration during build or only its generated root DTO type afterward.

**Non-Goals:**

- Do not expose binding configuration for mutation or create another mapping/projection engine.
- Do not make parent-context overlays into independent entity shapes or expand arbitrary members of directly assigned unregistered objects.
- Do not promise expression retrieval, full recursive projection, SQL translation, or predicate equivalence.

## Decisions

### 1. Class entry point plus generated-Type lookup

Use `Class.GetMetadata()` and static `DtoShapeMetadata.GetForType(Type)` / `TryGetForType(Type, out ..., out diagnostic)` in `Kkts.Dynamic`. All names below are **proposed APIs**, not existing members.

| Option | Advantages | Limitations |
| --- | --- | --- |
| Class-only snapshot | Smallest surface; no registry | Consumers must retain/pass declarations or snapshots; DTO-type-only pipelines cannot discover configuration |
| Class plus weak generated-Type lookup (chosen) | Fits runtime DTO integration and existing weak-cache pattern; works after builder disposal | Requires publication discipline and an explicit boundary for contextual generated types |
| Reflect properties/attributes or expose bindings | Little snapshot infrastructure | Reflection cannot recover source paths, computations, directions, or parent-scoped overlays; mutable bindings leak internals |

Register only explicitly declared entity-mapping DTO types. Automatically generated grouping types and derived overlay types do not have an independent root entity contract. Type lookup for these returns `ContextualDtoType` with guidance to use the owning root shape's nested layout. A registered base shape's type remains discoverable independently. Different names for one entity get distinct entries; no entity-only discovery or name-based static registry is added.

`GetForType` throws a descriptive argument exception for unsupported/unregistered types; `TryGetForType` returns false with a non-null immutable diagnostic. Null type arguments throw `ArgumentNullException` in both forms. There is no empty-metadata fallback.

### 2. Separate shape identity, layout, binding provenance, and resolved fields

Use sealed, getter-only descriptors, private construction, read-only copied lists, and no public setters or mutation methods:

- `DtoShapeMetadata`: declared `Name`, `EntityType`, `DtoType`, `Layout`, ordered `PrimaryKeys`, and `ResolveField(string)`.
- `DtoLayoutMetadata`: actual generated `DtoType` and immediate `Members`; a finite structural graph, not a flattened recursive field list.
- `DtoMemberMetadata`: canonical local `Name`, `DtoValueType`, node kind (`MappedValue` or `Grouping`), optional immutable `Binding`, selected registered `NestedShape`, and effective `NestedLayout`.
- `DtoBindingMetadata`: declaring shape name, declaration-relative DTO/entity paths, declaration entity root type, entity/DTO value types, existing `BindingMode`, key designation/order, binding kind, and forward execution kind.
- `DtoFieldMetadata`: resolved canonical root-relative `DtoPath`, structural root-relative `EntityPath` when present, root `EntityType`, leaf `EntityValueType`/`DtoValueType`, original `Binding`, effective forward/reverse availability, collection boundaries, optional `DirectEntityAlias`, selected shape, and effective layout.
- `DtoFieldResolution` and `DtoMetadataDiagnostic`: explicit success/field versus failure/code, shape, requested path, failed segment, message, and suggested fix. Exactly one success field or failure diagnostic is present.

The six binding kinds are `Direct`, `Converted`, `Computed`, `MappedObject`, `MappedArray`, and `MappedCollection`. Synthetic grouping is a node kind, not a seventh binding kind; it has no binding, entity source, direction, or key. Unregistered directly assigned objects/collections remain `Direct`, not mapped nested shapes.

Classify any configured transformation/value-type override as `Converted`, even if CLR types happen to match or a converter looks like identity. Computed fields have no single entity member path/value type; their declaration root entity type is still known. Never inspect converter bodies to guess aliases.

`ForwardExecutionKind` distinguishes `None`, `MemberAccess`, `Expression`, `InMemory`, and `NestedMapping`. It describes the enabled binding itself. For mapped objects/arrays/collections, `NestedMapping` means inspect the effective descendants; it is not a claim that a complete selector exists. An expression-backed forward conversion with an in-memory reverse conversion reports `Expression`, not `InMemory`. Reverse-only bindings report `None`.

`PrimaryKeys` contains only keys of that declared root shape, in exactly the stable order of `GetIds`: `PrimaryKeyOrder`, then existing normalized binding order for ties. A resolved nested key retains its child key designation/order but is not a root key. Non-key order is nullable in the metadata contract rather than a misleading default integer.

### 3. Snapshot resolved information before it is discarded

Build a private finite graph from normalized declarations, resolved alternatives, actual generated property layouts, and the parent-owned overlay bindings. Reuse effective derived-layout identities rather than re-running shape selection or reconstructing overlays from type names.

Stage the graph after all generated types and existing runtime registrations are ready, but before `Clear`. Freeze/copy binding scalars, path segments, source contexts, and types; do not keep `PropertyBinding`, `AlternationInfo`, `Class`, `DynamicAssembly`, emission builders, transformation delegates, or expression closures in public metadata or private registry values.

Represent each registered shape/layout once, with a construction identity map to break recursive references. Complete all graph nodes privately before exposing them. Overlay layouts contain the union of actual base and overlay members; a reused base layout remains unchanged. Their source edges remember whether a binding is declared on the selected child or on an ancestor/root. This is needed even though two members occupy the same nested DTO object.

Use a builder-independent publication holder shared by the assembly's staged registry entries, with a readiness gate set only after every build phase succeeds. Assign Class snapshot references and open that gate at the successful completion boundary. Failed builds expose no snapshot or usable type entry, including after a late initialization/registration failure; do not make availability depend on a mutable builder reference. Entries for contextual types contain an immutable diagnostic classification, not a retained parent declaration.

### 4. Resolve finite paths, not an eagerly expanded graph

Resolution accepts a non-empty dot-separated public DTO path, uses `StringComparer.OrdinalIgnoreCase`, and returns canonical emitted member spelling. Reject empty segments, whitespace-padded segments, indexers, wildcards, and collection-element syntax such as `Items[0]`; do not trim or reinterpret input. Immediate member enumeration is deterministic in ordinal case-insensitive order.

Walk one segment per structural node. On mapped-object edges, use the selected shape's effective layout, not an entity-type lookup. Parent-owned explicit overlay bindings take precedence over inherited child provenance for their exact member, using the same resolved layout identity as emission. Validation remains responsible for rejecting conflicting overlays; metadata never chooses an arbitrary winner.

Examples:

| DTO path | Source provenance | Direct forward alias |
| --- | --- | --- |
| `Name` | `ProductName` | `ProductName` |
| `CategoryName` | `Category.Name` | `Category.Name` |
| `External.Code` | `ExternalCode` | `ExternalCode` |
| `External` | No source: grouping node | None |
| `Child.Name` inherited from selected child shape | `Child.ProductName` | That path if effectively forward-enabled |
| `Child.Title` overlaid from the parent | `Title` | `Title` if the overlay is an ordinary forward binding |
| `PriceText` converted with `Price.ToString()` | `Price` | None, even with a reverse converter |
| `Label` computed from several members | No single source path | None |

Inherited descendant directions are the intersection of the mapped ancestors' enabled directions and the leaf's mode. Grouping nodes add no directional restriction. Overlay bindings are separate parent-declared operations: retain their own directions and source context, not the mapped parent's mode. This preserves existing behavior rather than inventing an inheritance rule for overlays. A reverse-only parent with a forward overlay can leave inherited child fields forward-unavailable while the overlay remains forward-enabled. No API suggests that field direction is an authorization policy.

A direct alias is available only for an effectively forward-enabled, ordinary direct member binding reached through grouping and mapped-object edges, without a collection-element boundary or a transformation. It is structural member correspondence, not a promise that a predicate on the entity path equals a predicate on the projected DTO value. Missing intermediate parents can make fresh DTO defaults differ from naive entity-path access; future querying must handle those semantics explicitly.

For `Items.Name`, resolution can describe the selected element field and canonical structural source path, but reports the collection boundary (`Items`) and no root single-value alias. That source path is not an executable CLR member chain from the root. Array/collection fields themselves retain their entity collection type and actual generated DTO collection type; descendant fields expose leaf types. No indices, quantifiers, element query behavior, or updates are introduced.

For a directly assigned unregistered object, resolve the whole binding but reject undeclared descendants with `NoNestedShape`. Reflection over arbitrary entity members is not DTO-shape configuration.

Cycles are represented by shared layout references. Construction and immediate enumeration are finite. Resolution consumes the finite supplied path and can revisit a recursive layout, so `Child.Child.Id` is valid even when full projection is recursive. It neither uses nor changes `Class.MaxRecursionDepthToBuildSelectorExpression`, caches no unbounded path set, and never follows descendants without consuming a segment. Consumers recursively enumerating layouts must use reference identity to stop cycles; document this explicitly.

### 5. Defer individual forward value expressions

Compared options:

1. Wrap the existing whole-shape selector and access one DTO property. This retains construction, expands all nested shapes, fails on unrelated delegate-only fields/recursive siblings, and does not provide a useful isolated query expression.
2. Independently reconstruct entity access and conversions from metadata. This duplicates mapping semantics and is particularly unsafe for absent grouping nodes, mapped-object presence, and parent-scoped overlays.
3. Extract a shared selective value/presence planner from the existing selector infrastructure. This is the correct route, but a separate projection-focused change is needed to keep its acceptance criteria precise.

Choose option 3 as a **narrow follow-up, not an implementation task in this change**. Metadata retains only execution classification, not the underlying transformation expressions/delegates. Source paths never stand in for converted/computed forward values.

Immediate integration: inspect shape membership/types/directions, reject computed/converted fields from a direct-alias strategy, derive ordinary forward aliases, detect nested layouts and collection boundaries, and use existing whole-shape selectors for expression-compatible complete shapes.

Still blocked: standalone scalar expressions for converted/computed values; isolated projection of an expression-compatible field when other active fields are delegate-only; selection-specific null-presence composition; filtering/sorting by transformed projected values. Kkts.Querying itself and its policies remain out of scope.

The follow-up must define a single-field expression as the value observed through the corresponding path in a **fresh projected DTO**, returning the leaf CLR default if an intermediate projected DTO node is absent. It must share selector value/presence semantics, inline expression-backed conversions/computations, preserve directly mapped null inputs and skipped intermediate-null conversions, and not rewrite computed null navigation. Explicit unsupported results are required for in-memory-only values, grouping nodes, whole objects/collections, and paths through collection elements until individually designed. Selective finite recursive paths must not force whole-graph expansion. Reverse converters never establish predicate equivalence. Constructability, compilability, and provider translation remain separate concerns.

### 6. Lifecycle and concurrency

| State | `Class.GetMetadata()` | Declared DTO Type lookup | Retained snapshot |
| --- | --- | --- | --- |
| Before/during build | Explicit not-built failure; no implicit build | Unavailable | None |
| Successful build | Same completed immutable snapshot | Available | Usable |
| Failed build | Explicit fault failure with build context | No published metadata | None from that build |
| Disposed declaration/assembly | `ObjectDisposedException`, matching existing Class APIs | Still available while type is reachable | Usable without builder |

Disposal does not mutate snapshots. Publication is thread-safe; after build, list reads, field resolution, and type lookup require no mutable builder/selector state and support concurrent callers. Configuration/build remains single-threaded as documented today. Avoid per-path caches: allocate immutable resolution results on demand.

Use a `ConditionalWeakTable<Type, ...>` for discovery. Its values can refer to their own key and related generated types because ephemeron ownership permits the otherwise unreachable graph to collect. Do not add static strong type/name dictionaries, captured builders, or enumerable global shape registries. Explicitly retained metadata/types/layouts/resolution results naturally keep referenced types alive; release all such references before asserting collection.

### 7. Proposed API examples and external integration

**Illustrative proposed API usage; this code is not supported until implementation.**

```csharp
assembly.Build();
var metadata = productClass.GetMetadata();
var result = metadata.ResolveField("external.code");
if (!result.Success)
    throw new InvalidOperationException(result.Diagnostic.Message);

DtoFieldMetadata field = result.Field;
Console.WriteLine(field.DtoPath);           // External.Code
Console.WriteLine(field.EntityPath);        // ExternalCode
Console.WriteLine(field.DirectEntityAlias); // ExternalCode
Console.WriteLine(metadata.EntityType);     // Product
```

```csharp
Type dtoType = productClass.GetBuiltType();
((IDisposable)assembly).Dispose();
var metadata = DtoShapeMetadata.GetForType(dtoType);
var field = metadata.ResolveField("Name").Field;
// No Class, PropertyBinding, internal types, or builder are needed here.
```

**Illustrative future Kkts.Querying adapter**, not an assertion of a current Kkts.Expressions API:

```csharp
var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
foreach (string requestedPath in requestedDtoFields)
{
    var result = metadata.ResolveField(requestedPath);
    if (!result.Success)
        throw new InvalidOperationException(result.Diagnostic.Message);
    if (result.Field.DirectEntityAlias == null)
        throw new NotSupportedException(
            $"'{result.Field.DtoPath}' needs a value-expression or element strategy.");
    aliases.Add(result.Field.DtoPath, result.Field.DirectEntityAlias);
}
// A separate adapter can pass these structural aliases to Kkts.Expressions.
// It must separately handle projected-null/default semantics and query policy.
```

No dependency on Kkts.Expressions is introduced. The metadata does not expose `CanFilter`, `CanSort`, operators, provider capability, or client-evaluation decisions. A converter such as `Price.ToString()` must not become a `Price` alias; even a genuine direct alias is not a database translation guarantee.

## Risks / Trade-offs

- [Overlay provenance can be lost by snapshotting only child bindings] -> Include parent-owned binding contexts and actual emitted layouts; test unrelated parent sources and deep overlays.
- [Incomplete metadata escapes on a late failed build] -> Stage a complete detached graph and gate all public access at successful build completion.
- [Read-only lists wrap mutable backing data] -> Copy backing data and freeze all nodes before publication; test mutation attempts and concurrent resolution.
- [Type discovery retains collectible assemblies] -> Use only weak-key discovery; exercise lookup, recursive/overlay snapshots, disposal, intentional retention, and release with existing no-inlining GC test patterns.
- [Consumers equate provenance/expression representation with query correctness] -> Conservative direct-alias eligibility, explicit element boundaries, and documentation separating null semantics, transformations, and provider responsibility.
- [Expression deferral limits early querying integration] -> State the blocked operations explicitly; pursue shared selective projection in a separate change rather than ship misleading aliases.
- [Contextual generated types cannot be independently discovered] -> Return actionable diagnostics and expose effective nested layouts from the root snapshot; do not guess an entity context.

## Migration Plan

Add APIs without changing existing signatures, output layouts, or dependencies. Construct metadata as an additional successful-build artifact and publish only complete snapshots. Verify new public-consumer tests and the entire existing suite on .NET 8 and .NET 10, then build the solution with warnings treated as errors. Update the packaged README with proposed API names replaced by the implemented contract and lifecycle/integration examples.

No configuration migration is required. Existing consumers need not call the new APIs. Rollback removes this additive metadata surface/build hook without changing declaration or mapping configuration; once published as a release, normal public API compatibility rules apply.
