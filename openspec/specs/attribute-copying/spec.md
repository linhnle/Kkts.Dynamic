# attribute-copying Specification

## Purpose

Preserve CLR-supported custom attribute argument values and types on eligible generated DTO properties while retaining the existing attribute-copying policy.

## Requirements

### Requirement: Attribute constructor argument fidelity

Eligible generated DTO properties SHALL retain source custom attributes with constructor arguments supported by CLR custom attribute encoding. Copies MUST preserve scalar values, enum types and values, `Type` values, boxed argument types, and exact array element types, order, length, and permitted null elements. Null arrays MUST remain null and empty arrays MUST remain empty arrays of the declared element type. Valid configurations using these arguments MUST build successfully without a metadata representation cast failure.

#### Scenario: Reported string array reproduction
- **WHEN** an entity property has a public attribute whose constructor accepts `string[]` with values `"a"` and `"b"` and an unchanged-type two-way binding copies that property
- **THEN** configuration validation remains valid, building succeeds, and the generated DTO property's instantiated attribute contains a `string[]` equal to `["a", "b"]` in that order

#### Scenario: Scalar and enum constructors
- **WHEN** eligible source attributes use primitive/string scalar and enum constructor arguments
- **THEN** instantiated generated attributes contain the same scalar values and enum values with the same enum type

#### Scenario: Typed arrays and null elements
- **WHEN** eligible source attributes use primitive, enum, string, or `Type` arrays, including permitted null elements
- **THEN** generated attribute contents preserve each array's exact CLR element type and each element's value or null

#### Scenario: Null and empty constructor arrays
- **WHEN** eligible attributes declare null arrays and empty arrays as constructor arguments
- **THEN** generated attribute contents distinguish null from empty and preserve the empty array's declared element type

### Requirement: Named attribute argument fidelity

Attribute copying SHALL apply the same value/type fidelity to named properties and named fields as to constructor arguments. Copies MUST preserve the distinction between a named property and a named field, and MUST preserve their assigned scalar, enum, `Type`, boxed, and array values for CLR-supported forms.

#### Scenario: Named array property and field
- **WHEN** an eligible source attribute declares a named `string[]` property and a named `string[]` field containing distinct non-empty values
- **THEN** the instantiated generated attribute contains each exact typed array on the correct named member with its original ordering and values

#### Scenario: Named null and empty arrays
- **WHEN** eligible source attributes assign null or empty arrays to named properties and fields
- **THEN** each generated named member retains null or the exact typed empty array respectively

#### Scenario: Named enum and type arguments
- **WHEN** eligible source attributes assign enum and `Type` values to named properties and fields
- **THEN** instantiated generated attributes retain those enum types/values and type identities

### Requirement: Boxed attribute argument fidelity

For CLR-supported arguments declared as `object` or `object[]`, generated attributes MUST retain the encoded value's actual type and contents rather than coercing it to a string or untyped collection. This SHALL apply to constructor arguments, named properties, and named fields, including legal boxed scalar, enum, `Type`, array, and null values.

#### Scenario: Boxed constructor values
- **WHEN** eligible attributes use object constructor arguments containing a primitive, enum, `Type`, legal array, or null
- **THEN** instantiated generated attributes retain the corresponding actual boxed CLR type and value, or null

#### Scenario: Heterogeneous object array
- **WHEN** an eligible attribute has an object array containing legal mixed scalar, enum, `Type`, and null elements
- **THEN** the generated attribute contains an `object[]` with the same element order, runtime types, values, and null elements

#### Scenario: Named boxed values
- **WHEN** eligible source attributes assign legal boxed values or arrays to object-valued named properties and fields
- **THEN** generated attribute contents retain those values' actual CLR types and array contents on the respective named members

### Requirement: Attribute copying eligibility remains unchanged

The system SHALL continue copying source-member attributes only for bindings that can write back, have a source member, and generate a DTO property of the same type as that source member. Eligible ordinary and same-type converted bindings MUST retain their attributes. Forward-only, changed-type converted, computed, and type-substituted bindings MUST NOT gain copied source attributes. The fix MUST NOT change the public API, binding modes, or behavior for attributes already copied successfully.

#### Scenario: Copying binding modes
- **WHEN** unchanged-type source members with attributes are declared using two-way, reverse-only, and forward-only bindings
- **THEN** generated two-way and reverse-only properties contain the copied attributes and forward-only properties do not

#### Scenario: Transformation and computation boundaries
- **WHEN** the same attributed member is declared using an eligible same-type two-way conversion, a changed-type conversion, and a forward-only computation
- **THEN** only the same-type converted DTO property contains copied source attributes

#### Scenario: Registered type substitution boundary
- **WHEN** an attributed entity property is mapped to a registered DTO object or collection shape with a generated property type different from the source member type
- **THEN** its source-member attributes are not copied onto that type-substituted DTO property
