# Design

## Context

See [proposal.md](./proposal.md) for motivation and scope. The inspected checkout is v2.0.0 at `4bf0f92`; verification here is source-level, with executable reproductions deferred to the regression tasks.

Observed behavior and relevant prior art:

- `MemberTree.BuildGet` switches to `OpCodes.Call` when the preceding member is nullable and its null guard is enabled. A null check proves a receiver exists; it does not make a virtual method non-virtual. For `Child.Id`, the declared base `Id` getter is therefore called directly after checking `Child`.
- `InjectToMemberBindingTree` directly calls entity getters in `BuildChild` and the root/nested alternative-object paths. Its root/nested scalar writes use `Call`, and `BuildChildSet` uses `Call` at the root. These can bypass both leaf setters and parent accessors on derived objects.
- `InjectFromMemberBindingTree` reads entity members through `MemberTree`, but its direct getter/setter calls address generated DTO properties. `Class.DeclareAutoProperty` emits those properties without `MethodAttributes.Virtual`. Static mapper/factory calls also intentionally use `Call`.
- `TreeEvaluationResult.BuildParentGuard` already uses `Callvirt` for property traversal. Compiled selectors use member-access expressions. `BindingExpressions.Write` and `PartialUpdatePlan` use expression member access/assignment to create and update entity paths. These are existing dispatch references, not reasons to rewrite those implementations.
- `Class.DeclareAutoProperty` passes `.Value` directly for all three custom attribute argument surfaces. Array metadata is a collection of `CustomAttributeTypedArgument`, not the declared CLR array. Enum metadata can carry an underlying integral value, and boxed arguments need their metadata type preserved.
- `Class.BuildProperties` supplies attributes only when the binding is not `OneWayToDto`, has a source member, and the generated property type equals the source type. This includes eligible `TwoWay`, `OneWayToEntity`, and same-type converted members, but excludes changed-type transformations, computed bindings, and type-substituted properties.
- The library targets `netstandard2.0`; the xUnit/VSTest test project already targets `net8.0;net10.0`. Both SDKs are installed. No main specs exist; the completed, unarchived `enhance-runtime-dto-mapping` deltas document compatibility constraints and must not be edited by this change.

## Goals / Non-Goals

**Goals:**

- Choose accessor dispatch from receiver/accessor semantics, independently of null-guard control flow.
- Use a single private conversion routine for attribute argument values on all emission surfaces, preserving both values and declared/boxed types.
- Make tests observe override effects, object identity, exception identity, and instantiated attribute contents rather than only build success.

**Non-Goals:**

- No mapper redesign, accessor caching layer, public helper, new attribute eligibility policy, or additional binding kinds.
- No expansion of supported nested value-type paths, CLR attribute argument forms, or selector/database translation guarantees.
- No normalization of intentional differences between legacy full mapping, fresh selectors, transformations, and presence-aware partial updates.

## Decisions

### 1. Use virtual dispatch for entity accessors on reference receivers

Emit `Callvirt` for entity instance getters/setters on supported reference-type receivers, including inherited accessors on derived roots and nested objects. It handles virtual overrides and preserves ordinary non-virtual instance calls. Determine receiver kind from existing tree/declared-type information, not from whether a parent null guard was emitted.

Accessor audit and intended treatment:

| Surface | Receiver | Treatment |
| --- | --- | --- |
| `MemberTree.BuildGet` entity traversal | Entity root or intermediate entity | Reference receivers use `Callvirt` regardless of the nullable branch |
| `MemberTree.BuildGet` DTO traversal | Generated DTO root/group | Keep valid existing non-virtual dispatch; do not mistake this for entity polymorphism |
| `InjectFromMemberBindingTree` direct property access | Generated DTO | Keep existing valid calls; entity reads are corrected through `MemberTree` |
| `InjectToMemberBindingTree.BuildChild` and alternative-object getter paths | Entity parent | Use reference-instance dispatch on both existence-check and reuse paths |
| `InjectToMemberBindingTree` scalar, collection, alternative, and child assignment paths | Entity parent | Correct direct scalar/parent calls; retain already-correct `Callvirt` writes |
| `TreeEvaluationResult` parent guards | Entity or DTO | Existing reference-property `Callvirt` is correct |
| Static mapping helpers, constructors, field reads/writes | Not property dispatch | Keep `Call`, `Newobj`, `Ldfld`, and `Stfld` respectively |

Preserve stack duplication/pops, labels, branch targets, guard placement, and existing constructor rules for dispatch-only edits. An implementation regression exposed a pre-existing stack imbalance when a nested entity alternative object maps to a top-level DTO property: `BuildAlternativePropertyMap` did not place a destination under the source for `MemberTree`'s two-pop intermediate-null guard. The user approved correcting this tightly coupled path. Load the destination before traversal, use it for the non-null assignment, and explicitly pop it on the null-leaf branch; test absent intermediate parents and null leaves. Keep the current value-type restrictions and any valid value-type receiver handling unchanged; do not introduce blanket `Callvirt` or new boxing/constrained-call support. Prefer local corrections using existing tree information over a new abstraction.

Alternatives rejected:

- Replacing every `Call`: changes generated DTO/static calls unnecessarily and risks invalid IL.
- Using `Call` after null checks: confuses null safety with dispatch and retains the root defect.
- Switching runtime mapping to expressions/reflection: unnecessary architectural change and could alter exception/performance behavior.

### 2. Convert attribute metadata recursively before emission

Add a private value converter near `Class.DeclareAutoProperty`, accepting a `CustomAttributeTypedArgument` and returning its CLR value. Use it for `ConstructorArguments`, each named property's `TypedValue`, and each named field's `TypedValue`, leaving their reflected constructor/member descriptors intact.

Conversion order and rules:

1. A null metadata value returns null, including typed null arrays. Do not turn null arrays into empty arrays.
2. Unwrap nested typed arguments if a boxed argument representation contains one, then recursively convert using its actual `ArgumentType`.
3. For an array argument, obtain the declared element type, allocate an exact one-dimensional CLR array with `Array.CreateInstance`, and recursively convert/set each metadata element in order. Empty arrays retain their element type; `object[]` elements retain their individual boxed types and legal null elements.
4. For enum arguments, produce a value of the actual enum type using `Enum.ToObject` when reflection provides an underlying number. This also applies to enum elements and enums boxed as `object`.
5. Preserve primitive, string, and `Type` values as CLR values. When a constructor or named member is declared `object`, use the typed metadata's actual value type rather than coercing everything to the member's declared `object` type; this preserves boxed scalars, enums, types, and legal arrays.

The converter supports the forms permitted by CLR custom attribute encoding, not arbitrary user object graphs or general-purpose conversions. No attribute constructor is invoked while copying metadata; instantiate attributes only when inspecting the built DTO in tests. Do not suppress failures, drop attributes, or replace unhandled metadata with defaults. Existing explicit emission failures remain failures; no new validation API or fallback is introduced.

Alternatives rejected:

- Passing `.Value` through: reflection collections cannot satisfy array parameters.
- Converting to `object[]` or only handling `string[]`: loses exact array types and leaves named/enum/boxed cases broken.
- Instantiating source attributes and reflecting their state: invokes user code and can lose constructor/named-argument fidelity.
- A standalone conversion library: unnecessary dependency and scope.

### 3. Leave attribute eligibility and null contracts at their current owners

Do not change the `BuildProperties` eligibility predicate. Conversion runs only on metadata already selected for copying. Test all modes, same-type versus changed-type conversions, and computed/type-substituted bindings as policy boundaries.

Keep legacy full-mapper null arguments as no-ops, intermediate-null skip/preserve behavior, direct nullable/scalar/collection assignment behavior, and mapped-alternative null preservation. Keep `Class.CreateDto`'s stricter creation contract. Partial updates continue to distinguish omission, explicit null clearing, and selected descendants with inaccessible DTO parents.

### 4. Compare APIs only on overlapping semantics

- For reachable getters and fresh DTOs, compare explicit runtime mapping, `Class.CreateDto`, and compiled selectors; the reported nested getter must yield `42` on each.
- For reverse-enabled reachable values, compare full mapping and partial mapping selecting exactly those values, including derived roots, an existing derived child, and absent child creation through virtual parent setters.
- Cover flattened/grouped paths and registered alternative-object branches, since their emitter paths differ. Include collection assignment override coverage for already-correct paths.
- Null intermediate forward mapping into an existing DTO preserves its values, whereas a fresh selector yields default values; assert these separately.
- A null mapped alternative is preserved by full reverse mapping but cleared when explicitly selected by partial mapping; assert these separately.
- Use typed compiled selector delegates when asserting thrown exceptions to avoid `DynamicInvoke` introducing a reflection wrapper. Runtime and partial calls must propagate the original user exception without swallowing it.

## Risks / Trade-offs

- [Invalid IL or stack imbalance in nested paths] -> Change accessor instructions without reorganizing control flow; exercise scalar, grouping, alternative, and collection paths with null/non-null parents.
- [Accidentally replacing a derived child or bypassing its parent override] -> Assert object identity, override-specific values, and setter observations for both creation and reuse.
- [Reflection representation differences across runtimes] -> Test exact instantiated values/types on both .NET 8 and .NET 10, including empty/null arrays and heterogeneous boxed values.
- [Attribute-policy expansion] -> Retain the existing predicate and use negative copying assertions for excluded bindings.
- [Overstated parity] -> Limit equality assertions to shared semantics and retain explicit null-difference tests.
- [Pre-existing warning failures] -> Run the required warnings-as-errors solution build, record any unrelated baseline blocker separately, and do not weaken the acceptance criterion or fix unrelated code.

## Migration Plan

No API, configuration, or data migration is required. Add failing regressions, implement the targeted fixes, update related README guarantees, and validate the existing solution/test targets before release. If rollback is needed, revert only this change's implementation/documentation/test edits through normal version control; existing declarations and binding modes remain compatible.
