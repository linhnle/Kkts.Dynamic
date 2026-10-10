# Spec Delta

## Purpose

Allow runtime DTO declarations to use compiler-checked entity member paths while preserving configuration-driven string bindings.

## ADDED Requirements

### Requirement: Fluent typed member bindings
The system SHALL accept generic entity declarations with chainable bindings from entity expressions to string DTO paths, including binding direction and primary-key metadata. Supported expressions MUST be instance property/field chains rooted at the entity parameter. DTO-path omission SHALL retain the existing entity-path default.

#### Scenario: Rename and flatten typed members
- **WHEN** a declaration maps an identifier, a renamed property, a public field, and a nested property with different directions
- **THEN** generated DTO types, mapping in both directions, identifiers, and selectors behave identically to equivalent string bindings

#### Scenario: Omitted DTO path
- **WHEN** a typed binding omits the DTO path
- **THEN** the complete entity member path is used as the DTO path

### Requirement: Reject non-path expressions
The system MUST reject method calls, computations, indexers, static members, captured-object access, casts that change member-path meaning, and expressions not rooted at the entity parameter. A boxing conversion around the final member SHALL be accepted without changing its actual type. Direction-specific accessor and nested value-type restrictions SHALL remain the same as string bindings.

#### Scenario: Computation is not a member binding
- **WHEN** a typed member binding receives a concatenation or method-call expression
- **THEN** declaration fails with an actionable argument error directing the caller to the transformation/computed-value API

#### Scenario: Invalid expression does not register a class
- **WHEN** expression validation fails during fluent configuration
- **THEN** no partial class declaration is registered

### Requirement: Compatible configuration snapshots
The system SHALL retain string declarations, preserve direction and primary-key semantics, and snapshot fluent configuration so later mutation cannot change a declared class.

#### Scenario: Equivalent redeclaration
- **WHEN** typed and string declarations use the same class name and equivalent normalized bindings
- **THEN** the same declaration is returned, while a conflicting configuration is rejected

#### Scenario: Later builder mutation
- **WHEN** a captured fluent builder is changed after declaration
- **THEN** the declared DTO shape and behavior do not change
