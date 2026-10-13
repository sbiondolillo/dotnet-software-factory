# Plan: the total line rounds the spend to four decimal places

## Cause
`BuildBodyAsync` in `src/Core/Deliver.cs` (line 53) interpolates `known.Spend`, a `decimal`, unrounded. The costs from Claude Code parse as decimals with up to 17 significant digits, so the sum keeps them all.

## Change (only `src/Core/Deliver.cs`)
In the last `body.Append`, write `decimal.Round(known.Spend, 4, MidpointRounding.AwayFromZero)` in place of `known.Spend`. Keep `CultureInfo.InvariantCulture`. Do not use a `F4` or `0.0000` format string.

Why `decimal.Round` and not a format string: `Round` never adds scale, so existing tests stay unchanged.
- The default `0m` still prints `$0` (`DeliverTests` expects `Total: $0 in 0 s`).
- `0.7500m` still prints `0.7500`, and `2.8347` is unchanged.
- `0.12645510000000004 + 0.09 = 0.21645510000000004` rounds to `0.2165`, which the new scenario expects.

No change to `FactoryRun`, `JobSummary` or the `DeliveryFacts` record. The spend stays unrounded there, and only the line of the body is rounded.

## Results the build owes
1. The scenario "The total rounds the spend to four decimal places" in `specs/features/deliver-body.feature` passes. The last line of the body is `Total: $0.2165 in 209 s`. The step "the plan answer has the cost ... and comes N seconds after its prompt" already exists in `tests/Specs/Steps`, so no new step is expected. If the build finds the step missing, it binds one beside the existing cost steps.
2. Every other existing scenario and unit test passes unchanged. That includes `$2.8347 in 856 s`, `$0.7500 in 31 s` and `$0 in 0 s`.
3. The build compiles and passes style and analyzers with warnings as errors. `dotnet format` is clean.
4. Optionally add a unit test to `tests/Specs/Unit/DeliverTests.cs` (class name ends `Tests`). It passes `new DeliveryFacts("base", Spend: 0.21645510000000004m, Seconds: 209)` and asserts the body ends with `Total: $0.2165 in 209 s`.
