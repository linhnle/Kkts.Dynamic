# accessor-dispatch Specification

## Purpose

Ensure mapping honors the runtime entity's reference-type property overrides without changing supported binding paths, null handling, or exception propagation.

## Requirements

### Requirement: Polymorphic forward property reads

Entity-to-DTO mapping SHALL invoke getter overrides on reference-type entity instances even when a binding traverses a base-declared nested property. For reachable members and equivalent fresh destination shapes, explicit runtime mapping, one-call DTO creation, and compiled selectors MUST yield the same mapped values. Compatible derived root entities MUST remain accepted.

#### Scenario: Reported nested getter reproduction
- **WHEN** a forward-only binding maps `Child.Id` to DTO `Id`, `Child` is declared as a base class with a virtual `Id` getter whose base value is `0`, and the runtime child overrides that getter to return `42`
- **THEN** runtime mapping, fresh DTO creation, and a compiled selector each produce DTO `Id` equal to `42`

#### Scenario: Getter overrides on derived roots and grouped paths
- **WHEN** a declaration for a base entity receives a compatible derived entity with overridden root or intermediate getters and maps their reachable leaves to flat or grouped DTO paths
- **THEN** each forward operation observes the runtime getter overrides and produces the corresponding values in the selected shape

### Requirement: Polymorphic reverse property writes and child access

Full and partial DTO-to-entity mapping SHALL invoke setter overrides and intermediate getter overrides on reference-type entities for reverse-enabled bindings. Existing non-null child objects MUST be updated rather than replaced solely because their runtime type is derived. Missing children MUST continue to be created using existing declared-type constructor requirements and assigned through the runtime parent's setter.

#### Scenario: Derived entity scalar setter
- **WHEN** a base-entity declaration maps a reachable scalar into a compatible derived entity whose setter records the supplied value and applies distinct override behavior
- **THEN** both full mapping and partial mapping selecting that scalar invoke the override and exhibit its recorded value and resulting state

#### Scenario: Existing derived child update
- **WHEN** a two-way flattened or grouped binding targets a child leaf, the parent's overridden getter returns an existing derived child, and that child overrides the leaf setter
- **THEN** full mapping and partial mapping selecting that leaf update the same child instance through its override without replacing it

#### Scenario: Missing child creation through virtual parent setter
- **WHEN** a reverse-enabled nested leaf is supplied, its entity parent is absent, and the containing entity overrides the child setter
- **THEN** full mapping and partial mapping selecting the leaf create the required declared-type child, initialize the leaf, and invoke the containing entity's setter override

#### Scenario: Registered mapped object creation and reuse
- **WHEN** a registered child shape is mapped through a root or nested entity property with overridden accessors and the destination child is either absent or an existing compatible derived instance
- **THEN** full mapping and partial mapping selecting the non-null whole object observe the parent overrides, create a child only when absent, and honor child setter overrides during reuse

#### Scenario: Collection setter override remains effective
- **WHEN** a reverse-enabled mapped array or collection is supplied to a derived entity that overrides its assignment setter
- **THEN** full mapping and partial mapping selecting the collection invoke that override and preserve existing replacement and null-element behavior

### Requirement: Existing null and error contracts remain unchanged

Accessor dispatch corrections MUST NOT change binding directions, public signatures, supported member paths, construction rules, or null semantics. Null legacy mapper arguments SHALL remain no-ops; null intermediate objects SHALL retain skip/preserve behavior; reachable null scalar and collection values SHALL retain assignment behavior. Null mapped alternative objects SHALL retain full-mapping preservation and explicitly selected partial-update clearing. User getter, constructor, and setter failures MUST continue to propagate without suppression or reflection invocation wrapping introduced by mapping.

#### Scenario: Null intermediate forward source
- **WHEN** a nested source parent is null and forward mapping targets an existing DTO with populated leaf values
- **THEN** mapping preserves those values, while fresh DTO creation and fresh compiled projection retain their established default/null shape behavior without invoking the unreachable getter

#### Scenario: Missing DTO grouping parent in reverse mapping
- **WHEN** a reverse nested binding has a null DTO grouping parent
- **THEN** full mapping skips that binding without creating or changing its entity parent, while partial mapping explicitly selecting the inaccessible descendant retains its existing explicit failure

#### Scenario: Intentional whole-object null difference
- **WHEN** a mapped alternative DTO property is null and its entity object already exists
- **THEN** full reverse mapping preserves the entity object, while partial mapping explicitly selecting that property clears it through its runtime setter override

#### Scenario: Direct null and omitted partial path
- **WHEN** a reachable nullable scalar or collection value is null
- **THEN** full mapping assigns null and partial mapping assigns null only when that path is selected; an omitted partial path causes no accessor invocation or parent creation

#### Scenario: Throwing override
- **WHEN** an invoked getter or setter override throws a distinctive user exception
- **THEN** runtime mapping and applicable expression-based operations propagate that exception without replacing it with a default or a mapping-generated invocation wrapper

#### Scenario: Direction and compatibility controls
- **WHEN** forward-only and reverse-only bindings, ordinary non-virtual accessors, field bindings, and legacy null mapper arguments are exercised
- **THEN** their previously supported directions, values, and no-op behavior remain unchanged
