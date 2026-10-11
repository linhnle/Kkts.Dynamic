# binding-transformations Specification

## Purpose

Support converted and computed values in generated DTO shapes with explicit direction, type, null, and projection contracts.

## Requirements

### Requirement: Directional value conversions
The system SHALL allow member bindings to specify a DTO value type and expression-based or explicitly in-memory-only directional transformations. Each enabled mapping direction MUST have a compatible transformation when entity and DTO types differ. No reverse conversion SHALL be inferred. Unsupported conversion/type combinations MUST produce configuration errors before type emission.

#### Scenario: Enum displayed as string
- **WHEN** a forward-only enum binding declares a string DTO value and an enum-to-string expression
- **THEN** runtime mapping produces the string property and reverse mapping does not write the enum

#### Scenario: Two-way conversion requires both directions
- **WHEN** a two-way converted binding omits its reverse conversion
- **THEN** validation reports the missing reverse transformation and build fails before emitting properties or methods

#### Scenario: Explicit reverse conversion
- **WHEN** a two-way binding supplies compatible conversions in both directions
- **THEN** normal and partial reverse mapping use the configured reverse conversion rather than implicit coercion

### Requirement: Forward-only computed properties
The system SHALL support computed DTO properties derived from an entity expression or an explicitly in-memory-only delegate, with the DTO value type inferred from the declared result type. Computed properties MUST be forward-only and MUST NOT be primary keys or reverse-update targets. Member-path bindings SHALL remain restricted to member paths.

#### Scenario: Compute a full name
- **WHEN** a computed property combines first and last name
- **THEN** forward mapping assigns the computed value and reverse mapping leaves both source properties unchanged

#### Scenario: Reject reverse computed value
- **WHEN** a computed property requests a reverse direction or primary-key designation
- **THEN** validation reports an actionable configuration error

### Requirement: Explicit projection support
Expression-backed transformations SHALL be composed into selector expressions without invoking compiled delegates and SHALL yield the same values as fresh runtime mapping for the same input. Selector construction MUST fail with the shape and DTO path when any active forward binding, including a nested shape binding, is delegate-only. Reverse-only delegates MUST NOT block forward selectors. The system MUST NOT promise database translation or introduce silent client-side evaluation.

#### Scenario: Project an expression-backed formatted value
- **WHEN** a selector containing an expression-backed formatted value is compiled or used with LINQ-to-Objects
- **THEN** its result matches fresh DTO mapping

#### Scenario: Delegate-only nested projection
- **WHEN** a selected nested shape has a delegate-only forward transformation
- **THEN** selector construction fails explicitly and identifies that nested shape and path

#### Scenario: Reverse-only delegate
- **WHEN** only a reverse conversion uses a delegate and all forward bindings have expressions
- **THEN** forward selector construction succeeds

### Requirement: Transformation null and exception behavior
Member conversions SHALL preserve existing intermediate-null guards and receive directly mapped null values when their input type allows null. Computed expressions/delegates SHALL own their null navigation and handling; the system MUST NOT silently rewrite arbitrary calculations. Transformation exceptions MUST propagate directly. Converted primary keys SHALL be permitted only on top-level member bindings and SHALL return the DTO-side value in identifier extraction.

#### Scenario: Null member conversion
- **WHEN** a member's parent is absent
- **THEN** runtime mapping skips that binding and a fresh selector yields the existing default behavior without calling its conversion

#### Scenario: Direct null and user failure
- **WHEN** a reachable nullable member is null or its transformation throws
- **THEN** the transformation receives null and its original exception, if any, propagates

#### Scenario: Converted identifier
- **WHEN** a top-level primary-key binding converts its value
- **THEN** identifier extraction returns the converted DTO value in the configured key order
