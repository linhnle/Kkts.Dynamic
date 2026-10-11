# Spec Delta

## Purpose

Allow external packages to inspect built DTO shapes and resolve public DTO fields without duplicating binding configuration or relying on mutable declarations and internal implementation types.

## ADDED Requirements

### Requirement: Public built-shape identity and immutable member metadata

The system SHALL expose metadata for every successfully built explicitly declared entity-mapping shape using public APIs. Metadata MUST include declared shape name, entity type, generated DTO type, complete finite immediate-member layouts, source and DTO value types where applicable, original binding mode, key designation/order, and binding kind. Binding kinds SHALL distinguish direct, converted, computed, mapped object, mapped array, and mapped collection values. Synthetic grouping nodes MUST be distinguished from mapped values and MUST NOT acquire fictitious bindings or entity sources.

Metadata MUST expose the selected registered nested shape separately from the effective nested layout and its actual generated type. Public metadata MUST NOT expose mutable bindings, builders, internal implementation types, generated transformation delegate fields, or transformation delegates.

#### Scenario: Inspect two shapes of one entity
- **WHEN** an external consumer obtains metadata for separately named summary and detail declarations mapping the same entity type
- **THEN** each snapshot reports its own name, generated type, and exact members, while reporting the same entity type without conflating the shapes

#### Scenario: Describe every generated member
- **WHEN** a built shape contains ordinary, converted, computed, mapped-object, mapped-array, mapped-collection, and synthetic-group members
- **THEN** metadata describes every generated property with its actual DTO value type and distinguishes grouping nodes from the six mapped binding kinds without silently omitting unsupported query values

#### Scenario: Preserve composite key order
- **WHEN** a shape contains converted and ordinary top-level keys with configured ordering, including equal order values
- **THEN** root metadata lists the same DTO-side keys in the same stable order as identifier extraction, preserving configured order and existing tie behavior

### Requirement: Deterministic public DTO path resolution

The system SHALL resolve finite dot-separated public DTO paths using ordinal case-insensitive matching and return canonical generated member spelling, root-relative DTO path, source provenance where applicable, root entity type, value types, and effective mapping availability. Immediate member enumeration SHALL have deterministic ordinal case-insensitive ordering.

Resolution MUST use the declared DTO layout rather than treating DTO names as entity names or expanding arbitrary entity members. Invalid or unknown paths MUST produce an explicit diagnostic result, never an empty successful field.

#### Scenario: Renamed field
- **WHEN** `ProductName` is directly bound to DTO `Name` and a consumer resolves `nAmE`
- **THEN** resolution succeeds with canonical DTO path `Name`, source path and direct forward alias `ProductName`, and the actual source/DTO value types

#### Scenario: Flattened field
- **WHEN** entity `Category.Name` is directly bound to DTO `CategoryName`
- **THEN** resolving `categoryname` reports `Category.Name` as its source and eligible structural direct forward alias, not `CategoryName`

#### Scenario: Grouped field and grouping node
- **WHEN** entity `ExternalCode` is directly bound to DTO `External.Code`
- **THEN** `external.code` resolves to the mapped leaf and `ExternalCode` alias, while `External` resolves to a grouping node with no source path, alias, key, or binding mode

#### Scenario: Unregistered directly assigned object
- **WHEN** an unregistered entity object is directly assigned to DTO `Child` without configured descendants
- **THEN** resolving `Child` succeeds as a direct value, but resolving `Child.Name` fails with an explanation that no nested DTO shape configures that path

### Requirement: Selected nested shapes and contextual overlay layouts

Nested resolution SHALL use the exact selected shape and effective per-use layout already used by mapping. It MUST NOT select a different shape by entity type. Inherited nested sources SHALL be composed relative to the mapped entity member. Overlay sources SHALL retain their declaring entity context, including ancestor-owned sources outside the mapped child.

Effective nested layouts MUST include all emitted base and overlay members without changing registered base layouts or unrelated uses. A resolved descendant key SHALL retain its nested key designation/order but MUST NOT become a root key.

#### Scenario: Explicit selected child shape
- **WHEN** a parent selects a summary child shape while detail and edit shapes for the same child entity also exist
- **THEN** metadata names the summary shape, reports its generated child type, and resolves only fields present in that selected effective layout

#### Scenario: Parent-scoped overlay
- **WHEN** a parent maps `Child` to a registered child shape and independently maps parent `Title` to DTO `Child.Title`
- **THEN** resolution of `Child.Title` reports root source `Title`, while an inherited child `Id` reports `Child.Id`; the effective layout contains both with their own binding provenance

#### Scenario: Overlay isolation across parents
- **WHEN** two parents add different fields beneath the same registered child and a third parent uses the child without overlays
- **THEN** each parent snapshot describes only its own effective layout, the unoverlaid use retains the base generated type, and the registered child metadata remains unchanged regardless of declaration order

#### Scenario: Deep overlay context
- **WHEN** parent `Title` is bound to `Child.Detail.ExtraTitle` across two mapped-object boundaries
- **THEN** root resolution reports source `Title`, the actual effective descendant types, and no addition to the registered `Child` or `Detail` layouts

### Requirement: Binding direction and forward execution classification

Metadata SHALL distinguish effective forward/reverse mapping availability from binding kind and source provenance. Inherited descendants SHALL intersect their own binding direction with mapped ancestors' directions. Grouping nodes SHALL impose no independent direction. Parent-owned overlays SHALL retain their own enabled directions rather than being incorrectly gated by the mapped parent's binding mode.

Forward classification SHALL distinguish no enabled forward operation, ordinary member access, expression-backed transformation, in-memory-only transformation, and nested mapping. Nested mapping classification MUST NOT imply that all descendants are expression-compatible or that a full selector can be built.

#### Scenario: Forward-only field
- **WHEN** a direct field is bound one-way to DTO
- **THEN** metadata reports forward availability, no reverse availability, member-access forward execution, and an eligible structural direct forward alias

#### Scenario: Reverse-only field
- **WHEN** a field is bound one-way to entity
- **THEN** metadata reports its source and original mode but no forward operation or direct forward alias, including when a reverse converter is configured

#### Scenario: Nested direction intersection
- **WHEN** a forward-only parent object uses a child shape containing a two-way field and a reverse-only field
- **THEN** the inherited two-way field is effectively forward-only and the inherited reverse-only field has neither effective direction through that parent, while both remain inspectable

#### Scenario: Independent overlay direction
- **WHEN** a reverse-only mapped parent object has a separately declared forward-only overlay underneath it
- **THEN** inherited child fields have no forward availability, but the overlay retains its own forward availability and declaring source context

#### Scenario: Mixed transformation representations
- **WHEN** a two-way converted binding has a forward expression and a reverse-only in-memory converter
- **THEN** metadata identifies expression-backed forward execution and reverse availability without classifying the forward value as in-memory-only

### Requirement: Conservative direct alias eligibility

Metadata SHALL separate entity-path provenance from a direct forward entity-path alias. A direct alias MUST be present only for an effectively forward-enabled ordinary direct binding through grouping or mapped-object nodes without any transformation or collection-element boundary. Converted and computed values MUST NOT be aliases, including same-type or apparently identity transformations. Reverse converters MUST NOT establish alias or predicate equivalence.

A direct alias SHALL mean structural member correspondence only, not projected-null/default predicate equivalence, expression retrieval, or database-provider translation support.

#### Scenario: Formatted conversion is not an alias
- **WHEN** DTO `PriceText` is produced from entity `Price` by `Price.ToString()` with an explicit reverse converter
- **THEN** metadata reports converted kind, source `Price`, numeric entity and string DTO types, and expression-backed forward execution, but no direct alias

#### Scenario: Same-type and in-memory conversions
- **WHEN** a field uses a same-type conversion expression or an in-memory-only forward converter
- **THEN** it remains a converted non-alias field, with the accurate execution classification rather than being treated as an ordinary member

#### Scenario: Computed value
- **WHEN** DTO `Label` is computed from several entity members
- **THEN** metadata reports computed kind, its DTO value type and declaration root entity type, no single entity member path/value type or alias, and expression-backed or in-memory-only execution as configured

### Requirement: Collection boundary metadata without element query semantics

Metadata SHALL describe mapped arrays/collections, selected element shapes, actual entity and DTO collection types, and finite descendant structural paths. A resolved path through an element shape MUST explicitly identify every crossed collection boundary and MUST NOT expose a root single-value alias. Structural source paths through elements MUST NOT be presented as executable root entity member chains.

This capability MUST NOT introduce index, quantifier, collection-element query, or collection-element update syntax.

#### Scenario: Mapped array and collection layouts
- **WHEN** array and collection bindings choose different named element shapes
- **THEN** metadata distinguishes their binding kinds and actual generated collection types and describes the selected effective element layout for each

#### Scenario: Structural element field
- **WHEN** `Items.Name` resolves through a selected mapped collection element shape
- **THEN** resolution reports the element leaf types and structural source provenance with the `Items` boundary, no root single-value alias, and no inferred element query operation

#### Scenario: Element indexing is unsupported
- **WHEN** a consumer resolves `Items[0].Name`
- **THEN** resolution fails explicitly with guidance to inspect the element layout without index syntax rather than silently treating it as a member path

### Requirement: Finite recursive metadata and lookup

Recursive shape metadata SHALL form a finite shared layout graph without eagerly expanding all descendant paths. Immediate enumeration SHALL terminate. Resolution SHALL consume the finite requested path and permit repeated visits to recursive layouts only while consuming segments. It MUST NOT depend on, change, or claim to bypass existing full-selector recursion limits.

The documentation SHALL require consumers doing their own recursive enumeration to stop repeated layout identities. Resolution MUST NOT accumulate an unbounded cache of requested paths.

#### Scenario: Self-recursive shape
- **WHEN** a built shape has `Id` and a mapped `Child` of its own shape
- **THEN** metadata construction and immediate member enumeration terminate, and `Child.Child.Id` resolves to the finite structural source and effective binding without infinite expansion

#### Scenario: Mutually recursive shapes
- **WHEN** two selected shapes reference each other
- **THEN** their metadata preserves both finite layouts and a finite supplied descendant path resolves deterministically, while an unknown final segment fails explicitly

#### Scenario: Recursive projection remains independent
- **WHEN** a recursive shape's existing whole-shape selector fails its recursion limit
- **THEN** metadata and finite field resolution remain usable and do not change that selector behavior

### Requirement: Explicit metadata diagnostics

Field resolution failures SHALL contain a stable diagnostic code, declared root shape, requested DTO path, failed segment when applicable, explanation, and corrective guidance. Unknown fields, invalid syntax, and undeclared descendants MUST be distinguishable. Successful resolution SHALL have a field and no failure diagnostic; failed resolution SHALL have a diagnostic and no field.

Type lookup SHALL offer a throwing retrieval API and a non-throwing unsupported-type result with a diagnostic. Null Type arguments MUST throw `ArgumentNullException`; field-path syntax errors SHALL return explicit resolution diagnostics.

#### Scenario: Unknown field
- **WHEN** a consumer resolves `Child.Missing` in a known selected child layout
- **THEN** failure identifies the root shape, supplied path, and missing segment and suggests using a configured member of that effective layout

#### Scenario: Invalid path
- **WHEN** a consumer supplies a null/empty path, empty segment, whitespace-padded segment, wildcard, or index syntax
- **THEN** resolution reports invalid or unsupported syntax explicitly and never normalizes it into a successful different path

#### Scenario: Converted field is valid metadata
- **WHEN** a converted or in-memory-only field is resolved
- **THEN** resolution succeeds with accurate non-alias metadata rather than failing merely because future querying cannot use a direct-alias strategy

### Requirement: Successful-build publication and disposal lifecycle

Metadata SHALL become available only after complete successful build, without implicitly building or recovering a faulted builder. Before or during build, Class metadata access MUST fail explicitly as not built. After any failed build, including a late failure after type creation, no metadata from that build SHALL be publicly available.

After successful build, repeated Class access SHALL return the same immutable snapshot. Disposed Class access MUST throw `ObjectDisposedException`, while snapshots already obtained and generated-Type discovery SHALL remain usable without retaining mutable builder state.

#### Scenario: Before build and after failure
- **WHEN** metadata is requested before build or after a build faults
- **THEN** access fails with not-built or fault context respectively and never returns partially constructed metadata

#### Scenario: Late build failure
- **WHEN** build fails after some generated types or runtime registrations exist
- **THEN** neither Class access nor Type discovery exposes metadata for any shape in that failed build

#### Scenario: Builder disposal
- **WHEN** a consumer retains a successful root DTO Type and a metadata snapshot, then disposes the builder
- **THEN** the snapshot and Type lookup still resolve the same fields/layouts, while new access through the disposed Class fails explicitly

### Requirement: Generated-Type discovery and collectible ownership

Type discovery SHALL identify exact successfully built explicitly declared DTO types, including distinct named shapes of the same entity. Synthetic grouping and parent-context derived overlay types MUST NOT be advertised as independent root shapes; their lookup failure SHALL explain that effective nested metadata must be obtained through the owning root.

Static discovery infrastructure MUST NOT keep otherwise unreachable generated types or collectible assemblies alive. Explicitly retained snapshots, layout references, resolutions, or types are permitted to retain the types they describe.

#### Scenario: Exact named type lookup
- **WHEN** an external consumer has only the generated summary DTO Type after builder disposal
- **THEN** public discovery returns summary metadata rather than another shape for the entity

#### Scenario: Contextual generated type
- **WHEN** a consumer performs Type lookup on an emitted synthetic grouping or derived overlay type
- **THEN** retrieval fails with a contextual-type diagnostic directing the consumer to the owning root's effective nested layout instead of guessing an entity root

#### Scenario: Lookup does not root generated assemblies
- **WHEN** a consumer exercises metadata/type lookup for ordinary, transformed, overlaid, and recursive shapes, disposes builders, and releases all types, DTOs, metadata, layouts, and resolution results
- **THEN** bounded garbage-collection checks can observe the generated types becoming unreachable rather than retained by static discovery

#### Scenario: Intentional snapshot retention
- **WHEN** a consumer retains a metadata snapshot after builder disposal
- **THEN** it remains usable with its referenced types alive, and releasing that final snapshot permits collection once all other references are released

### Requirement: Immutable concurrent read contract

Published metadata SHALL be immutable throughout its lifetime, including nested layouts, binding descriptors, key lists, diagnostics, and resolution results. Caller binding/builder mutations and builder disposal MUST NOT change metadata. Concurrent metadata enumeration, finite field resolution, and Type discovery after successful build SHALL be supported without caller locking or mutable selector-cache use.

#### Scenario: Mutation attempts and original configuration changes
- **WHEN** consumers attempt to mutate exposed collections or change the original caller-owned binding objects after declaration/build
- **THEN** no published member, binding, key, or nested layout changes and public descriptors expose no mutation surface

#### Scenario: Concurrent reads
- **WHEN** many callers concurrently enumerate metadata and resolve valid/invalid nested paths through both Class-derived snapshots and Type discovery
- **THEN** results are consistent, complete, and race-free without modifying the shape or building selectors

### Requirement: Compatibility and integration boundary

This capability SHALL preserve existing public signatures, generated property layouts, mapping/selector behavior, directions, key ordering, partial-update behavior, null semantics, virtual accessor dispatch, and attribute-copying eligibility/fidelity. It MUST retain the library's existing runtime target and introduce no new package dependency.

Documentation SHALL demonstrate public-only direct-alias integration for a future Kkts.Querying adapter, lifecycle, collection/recursion boundaries, and the distinction between source aliases, expression representation, and provider translation. Individual forward expression retrieval SHALL be deferred, not simulated by returning an alias for a transformed value. Existing whole-shape selector APIs SHALL remain unchanged.

Existing and new tests MUST pass on .NET 8 and .NET 10, and the solution MUST build with warnings treated as errors.

#### Scenario: External direct-alias consumer
- **WHEN** an external consumer inspects a built shape and resolves renamed, flattened, and grouped ordinary forward fields using only public APIs
- **THEN** it can construct canonical DTO-to-entity structural aliases without internal types, copied bindings, Kkts.Expressions dependencies, or a claim of provider translation

#### Scenario: Expression access deferral
- **WHEN** an external consumer inspects an expression-backed converted or computed field
- **THEN** it can identify that execution representation but cannot obtain an individual value expression in this change; documentation identifies the follow-up and warns against source-path substitution

#### Scenario: Existing mapping regressions
- **WHEN** metadata is enabled for shapes covered by existing mapping, selector, null, partial-update, key, virtual-accessor, and attribute-copying tests
- **THEN** those observable results and actual generated layouts remain unchanged on .NET 8 and .NET 10, and warnings-as-errors solution validation succeeds
