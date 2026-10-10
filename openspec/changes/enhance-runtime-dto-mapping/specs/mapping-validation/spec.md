# Spec Delta

## Purpose

Help callers diagnose multiple runtime DTO configuration problems before irreversible type emission with structured, actionable reports.

## ADDED Requirements

### Requirement: Aggregate actionable diagnostics
The system SHALL provide validation for both registered declarations and raw candidate declarations without registering or emitting the candidates. Reports SHALL contain all independently detectable errors rather than stopping at the first error. Each diagnostic MUST include a stable code, severity, class/shape name, binding index when applicable, entity and DTO paths when available, affected direction or configuration scope, explanation, and corrective guidance. Reports SHALL expose overall validity and deterministic ordering.

#### Scenario: Several candidate errors
- **WHEN** candidate bindings contain a malformed path, a duplicate DTO path, and a missing entity member
- **THEN** one report contains the independently detectable errors with binding locations and guidance without registering the candidate

#### Scenario: Directional accessor failure
- **WHEN** a reverse binding targets a getter-only member
- **THEN** its diagnostic identifies the reverse direction, paths, and required writable accessor

### Requirement: Complete mapping configuration checks
Validation MUST cover path syntax and resolution, modes, duplicate DTO paths and entity writes, DTO parent/leaf conflicts, accessor visibility, indexers, nested value types, reverse constructors, primary-key restrictions, shape references/ambiguity, incompatible base/overlay leaf definitions, transformation signatures/directions, and conflicting binding kinds. A missing upstream type/member SHALL prevent only dependent checks, not validation of other bindings or shapes. Expressionless forward transformations SHALL produce a projection-unavailable warning rather than an in-memory build error.

#### Scenario: Multiple shape and conversion errors
- **WHEN** one declaration references a missing nested shape and another lacks a required reverse conversion
- **THEN** both errors appear in the same report

#### Scenario: Delegate-only forward transformation
- **WHEN** an otherwise valid configuration uses an explicitly in-memory-only forward delegate
- **THEN** validation remains valid for build and reports a warning identifying selector unavailability

#### Scenario: Base and overlay conflict
- **WHEN** a parent overlay defines a nested leaf incompatible with the selected base shape
- **THEN** the report identifies the parent path, selected base shape, and conflicting leaf without mutating the base declaration

### Requirement: Non-emitting validation and compatible build failures
Validation MUST NOT emit generated DTO properties, methods, or completed types, mutate caller bindings, or fault an otherwise usable assembly. Build SHALL run the shared checks before property/method emission and SHALL fail with an invalid-operation error carrying the report if errors exist. Existing declaration-time argument validation SHALL remain fail-fast and failed build SHALL permanently fault the assembly as before.

#### Scenario: Validation before build
- **WHEN** a caller validates an invalid raw candidate and then declares a valid class
- **THEN** declaration and build remain available and caller configuration is unchanged

#### Scenario: Build reports and faults
- **WHEN** registered declarations contain build-time configuration errors
- **THEN** build fails with the aggregate report before property/method emission and subsequent builder use retains the existing faulted-assembly behavior

#### Scenario: Legacy malformed declaration
- **WHEN** an existing string declaration contains a malformed path
- **THEN** declaration still throws its argument error and leaves the class registry unchanged
