# partial-updates Specification

## Purpose

Provide opt-in reverse mapping that distinguishes omitted fields from supplied nulls using explicit caller-provided DTO paths.

## Requirements

### Requirement: Explicit field presence
The system SHALL expose a distinct partial reverse-mapping operation accepting a generated DTO, a compatible entity, and a caller-supplied set of DTO paths. Omitted paths MUST preserve destination values and MUST NOT read or convert omitted values or create their entity parents. Paths SHALL use case-insensitive DTO-path matching and duplicate paths SHALL be deduplicated. Null DTO, entity, or path-set arguments MUST fail explicitly; an empty path set SHALL perform no updates.

#### Scenario: Omitted field
- **WHEN** only the name path is supplied while the DTO also contains a price
- **THEN** only name is updated and the existing price is preserved

#### Scenario: Empty and duplicate presence
- **WHEN** the supplied set is empty or contains the same path with different casing
- **THEN** the empty set performs no writes and the duplicate path is applied only once

### Requirement: Supplied null and supplied value
A supplied non-null value SHALL update its target using the configured reverse mapping and conversion. A supplied null MUST clear a reference/nullable target, including mapped alternative objects, unless a configured reverse conversion explicitly converts that null to an assignable non-null result. A null result targeting a non-nullable value type MUST fail before mutation. Existing full reverse-mapping null-preservation semantics MUST remain unchanged.

#### Scenario: Three field states
- **WHEN** a nullable field is omitted, supplied as null, or supplied with a value
- **THEN** the respective outcomes are preserve, clear, and update

#### Scenario: Clear mapped alternative
- **WHEN** a mapped object path is supplied with null
- **THEN** partial mapping clears the entity object while legacy full reverse mapping still preserves it

### Requirement: Validate the complete supplied path set
All supplied paths MUST resolve to reverse-enabled mapped leaves or directly mapped object/collection bindings, using the effective per-use derived shape when nested overlays exist. Synthetic grouping paths, unknown paths, computed/forward-only paths, element/index paths, and overlapping whole-object/descendant paths MUST fail with actionable errors before any mutation. A selected descendant whose DTO parent is null MUST fail rather than treating an inaccessible value as omitted or null. Configuration and supplied-value checks SHALL finish before assignments; arbitrary user getter, constructor, converter, or setter failures SHALL propagate without a transactional rollback guarantee.

#### Scenario: Unknown or forward-only path
- **WHEN** the supplied set contains a valid editable path plus an unknown or forward-only path
- **THEN** validation rejects the request and no entity values change

#### Scenario: Null grouping object
- **WHEN** a supplied path is nested beneath a null DTO grouping object
- **THEN** the request fails explicitly before entity mutation

#### Scenario: Overlapping selection
- **WHEN** both a mapped object and one of its descendants are supplied
- **THEN** the request is rejected as ambiguous before mutation

#### Scenario: Presence follows the per-use shape
- **WHEN** an editable overlay descendant is supplied for the parent that declares it
- **THEN** that descendant updates its mapped entity member, while supplying the same path to a parent without that overlay is rejected before mutation

### Requirement: Whole objects and collections
A supplied non-null mapped object SHALL use its selected shape's full reverse mapping; omitted outer object paths SHALL leave the object unchanged. A supplied collection/array SHALL use the existing replacement policy and preserve null elements. A supplied null collection/array SHALL clear the target. Partial selection of collection elements, merge-by-key, and delete-by-key MUST NOT be supported. Supplied nested scalar paths SHALL create only the entity parents needed by those selected paths using existing constructor requirements.

#### Scenario: Whole collection update
- **WHEN** an array or collection binding is supplied with a complete value, null, or omitted
- **THEN** it is respectively replaced, cleared, or preserved without element reconciliation

#### Scenario: Nested scalar update
- **WHEN** a flattened editable child property is supplied and its entity parent is absent
- **THEN** the required parent is created and only the selected leaf is written
