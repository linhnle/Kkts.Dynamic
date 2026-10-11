# dto-shapes Specification

## Purpose

Make multiple runtime DTO shapes for one entity selectable and ensure nested objects and collections resolve their intended shape consistently.

## Requirements

### Requirement: Named shapes for the same entity
The system SHALL allow separately named summary, detail, or edit declarations for the same entity type. Root mapping, DTO creation, identifier extraction, and selectors SHALL use the exact selected declaration or generated DTO type, not an entity-only lookup. Equivalent redeclaration of one name SHALL return the same declaration; conflicting redeclaration SHALL fail.

#### Scenario: Separate summary and detail
- **WHEN** two named declarations map different members of the same entity
- **THEN** each creates and projects only its declared shape and reverse maps only its enabled bindings

### Requirement: Explicit nested shape selection
Bindings SHALL allow selecting a named shape for an entity object or supported array/collection element. Selection MUST be resolved within the owning assembly and match the exact declared entity or element type. When omitted, zero registered shapes SHALL retain direct-assignment semantics and exactly one matching shape SHALL retain automatic substitution. Multiple matching shapes MUST produce an ambiguity error requiring explicit selection, independent of declaration order.

#### Scenario: Select collection element shape
- **WHEN** a parent binding explicitly chooses the summary shape for a collection element type with multiple declarations
- **THEN** the generated collection element type, runtime mapping in both directions, and selector all use that summary shape

#### Scenario: Ambiguous implicit selection
- **WHEN** a nested binding omits selection and two matching shapes exist
- **THEN** validation lists the ambiguity and build rejects it rather than choosing the first registration

#### Scenario: Invalid shape reference
- **WHEN** a binding names a missing shape or one for the wrong entity type
- **THEN** validation reports the referring DTO path and shape mismatch before emission

### Requirement: Consistent shape identity
Nested shape selection SHALL be part of declaration equivalence and cached mapping/projection identity. Shape selection SHALL retain existing selector recursion limits and null/collection semantics. A transformation binding MUST NOT simultaneously request automatic nested shape conversion.

#### Scenario: Different nested shape redeclaration
- **WHEN** a class name is redeclared with a different explicitly selected nested shape
- **THEN** the conflicting declaration is rejected

#### Scenario: Finite selector recursion
- **WHEN** selected nested shapes exceed the configured selector recursion limit
- **THEN** selector construction fails explicitly instead of dropping members

### Requirement: Isolated nested overlays
When a parent maps a registered nested object and additional DTO paths beneath that object, the system SHALL create a per-use derived shape containing the selected base members and the added overlay members. The registered base shape and other uses MUST NOT acquire those overlay members. A nested use without overlays SHALL retain the selected base shape's generated type. Normal mapping, selectors, identifier extraction, and partial updates MUST agree on the effective per-use shape.

#### Scenario: Overlay does not leak
- **WHEN** one parent maps a child identifier both through the selected child shape and to an additional child DTO field, while another parent uses the same child shape without that overlay
- **THEN** only the first parent's child DTO contains the additional field, and root DTO creation from the registered child shape remains unchanged

#### Scenario: Separate overlays for separate parents
- **WHEN** two parents add different fields beneath the same registered child shape
- **THEN** each parent's nested DTO contains its own effective shape without the other parent's additions, regardless of declaration order

#### Scenario: Conflicting overlay
- **WHEN** an overlay attempts to redefine an existing base leaf with an incompatible type or binding behavior, or turns a base scalar leaf into a grouping object
- **THEN** validation reports the conflict before emission instead of silently ignoring or overwriting it
