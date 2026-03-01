# AGENTS instructions

## Coding Standards
- All log statements should be written on a single line in code.

## Testing Standards
- Name tests as `<MethodUnderTest>_<Should>[_<When>]`.
- Use `_When` only when there are 2+ tests with the same `<MethodUnderTest>_<Should>` but different preconditions/inputs.
- If only one test exists for a given `<MethodUnderTest>_<Should>`, omit `_When`.
- Inherit from `TestBase` for all test classes unless there is a documented reason not to.
- Use `Fixture` from `TestBase` to create/freeze dependencies and expose SUT via a delegate/property pattern (for example `private MyType Sut => Fixture.Freeze<MyType>();`).
- Prefer happy-case setup in `TestInitialize` (shared defaults for the class).
- In each test, only override/mock the minimum needed for the specific behavior being asserted.
- Sort test methods alphabetically by full method name within each test class.
