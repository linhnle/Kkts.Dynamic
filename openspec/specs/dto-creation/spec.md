# dto-creation Specification

## Purpose

Remove repetitive runtime-type lookup and activation from the workflow for mapping an entity into a selected generated DTO.

## Requirements

### Requirement: Create and map a selected DTO
The system SHALL provide a one-call operation on a built declaration that returns a fresh DTO of that exact shape and maps the supplied entity using its forward bindings. The operation MUST NOT implicitly build the assembly or choose a shape by entity type.

#### Scenario: One-call mapping parity
- **WHEN** a caller creates a DTO from a compatible entity through a built declaration
- **THEN** the returned object has the declaration's generated type and the same values and identifiers as explicit creation followed by entity-to-DTO mapping

#### Scenario: Fresh instances
- **WHEN** the same declaration creates two DTOs
- **THEN** distinct DTO instances are returned without leaking destination values between calls

### Requirement: Explicit creation failures
The new operation MUST reject null entities with an argument-null error, reject incompatible entity types with an argument error, and reject unbuilt, faulted, or disposed declarations using existing builder lifecycle errors. Mapping exceptions MUST propagate directly. Existing mapper null-argument no-ops SHALL remain unchanged.

#### Scenario: Null or incompatible entity
- **WHEN** creation receives null or an entity incompatible with the declaration
- **THEN** it fails explicitly rather than returning an empty DTO

#### Scenario: Builder lifecycle
- **WHEN** creation is requested before build or after a failed build or disposal
- **THEN** it throws the corresponding lifecycle error without implicit build or recovery

#### Scenario: User accessor failure
- **WHEN** an entity getter throws during creation
- **THEN** the original exception propagates without reflection invocation wrapping
