# Proposal

## Why

The current v2.0.0 checkout (`4bf0f92`) still emits non-virtual calls for some entity accessors, making nested runtime mapping bypass overrides that compiled selectors respect. It also forwards reflection's array attribute metadata directly to `CustomAttributeBuilder`, so a valid binding can fail during DTO generation instead of copying its attributes.

## What Changes

- Correct reference-type entity getter and setter dispatch across `MemberTree`, `InjectFromMemberBindingTree`, and `InjectToMemberBindingTree`, including nested traversal, child creation/reuse, and mapped alternative objects.
- Include the user-approved, tightly coupled correction to a pre-existing forward nested-alternative null-guard stack imbalance exposed by the required creation/reuse regressions; preserve existing skip/preserve null semantics.
- Distinguish entity accessors from generated, non-virtual DTO accessors and static mapper helpers; do not perform a blanket opcode replacement.
- Convert custom attribute constructor arguments, named property values, and named field values into CLR-compatible values, including arrays, null arrays, enums, `Type`, and boxed attribute arguments.
- Add value-based regressions for virtual dispatch, generated attribute contents, and the existing binding-mode, null, and exception contracts.
- Verify forward runtime/compiled-selector parity and full/partial reverse parity only for inputs where those APIs have the same semantics.
- Preserve the public API, library target, binding modes, attribute-copying eligibility, and existing null handling. This is a non-breaking correctness fix with no unrelated features, refactoring, or new dependencies expected.

## Capabilities

### New Capabilities

- `accessor-dispatch`: Runtime mapping must invoke reference-type entity accessor overrides, consistently with expression-based paths where their contracts overlap.
- `attribute-copying`: Eligible generated DTO properties must retain the contents and types of CLR-supported custom attribute arguments without expanding copying eligibility.

These are newly documented capabilities, not new public APIs. `openspec list --specs` currently reports no main specs. The completed `enhance-runtime-dto-mapping` change remains unmodified; its DTO creation, transformation, shape, and partial-update contracts are constraints on this change.

### Modified Capabilities

None.

## Impact

- Implementation scope: `src/Kkts/Kkts.Dynamic/Internal/MemberTree.cs`, `InjectFromMemberBindingTree.cs`, `InjectToMemberBindingTree.cs`, and attribute emission in `src/Kkts/Kkts.Dynamic/Class.cs`. Existing expression-based selector, transformation, and partial-update helpers are parity references, not presumed defects.
- Test scope: existing xUnit test project, particularly `MappingRegressionTests.cs`, `PartialUpdateTests.cs`, and `TransformationTests.cs`, with a focused attribute regression class if needed.
- Documentation scope: clarify virtual dispatch and supported attribute copying in `README.md` without changing its compatibility or null guarantees.
- Acceptance: both reported reproductions pass, all existing and new tests pass for the already configured `net8.0` and `net10.0` targets, and `src/Kkts/Kkts.sln` builds with warnings treated as errors. The library remains `netstandard2.0`.
- Verification so far is read-only source inspection, not execution of reproduction tests: the identified opcode and metadata-conversion defects are present in the current source. Runtime regression execution is explicitly part of implementation and validation.
