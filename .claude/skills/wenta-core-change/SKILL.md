---
name: wenta-core-change
description: Use when adding or changing a Wenta.Core C# source file, a calculation (sizing, friction, fittings, fan, sound, insulation...), its regression tests or parity vectors, or when building or testing the wenta ZWCAD plugin.
---

# Changing Wenta.Core

Rule: every Wenta.Core change is proved by `just csharp-parity`, which must end with `==== N passed, 0 failed ====`. Quote N. The plugin build and the in-ZWCAD checks are separate and need ZWCAD.

## Build and test (Windows, VS 2022 Roslyn `csc` against .NET Framework 4.x, no NuGet, no project files)
- `just csharp-parity`: runs `csharp\build.cmd`, then `csharp\bin\Wenta.Core.Tests.exe` (vectors are copied to `csharp\bin\vectors`).
- `just zwcad-build`: `zwcad-plugin\build.cmd`; needs the ZWCAD 2021 SDK; run `csharp-parity` first. The plugin DLL is `-platform:x64`; Wenta.Core and the test runner are `anycpu`.
- `just test-all`: everything that runs without ZWCAD.
- CI: `.github/workflows/csharp.yml` on `windows-latest` runs the same two steps.

## Adding a source file
1. List it in both `csharp\build.cmd` and `zwcad-plugin\build.cmd` (the plugin compiles Wenta.Core in). Missing it in one breaks only that build.
2. Add a `RunX()` to `Main` in `csharp/Wenta.Core.Tests/Program.cs`, using the helpers there: `Check(id, actual, expected, tol)`, `CheckInt`, `CheckTrue`, `CheckStr`, `ExpectError(bool error, id, act)`.

## Rules
- `csharp/Wenta.Core.Tests/vectors/*.csv` are frozen golden fixtures, compared at `1e-12`. Don't regenerate or edit them to make a test pass; a changed number needs the owner's decision and the reason.
- Parse and format with `CultureInfo.InvariantCulture`, error messages included (#65, #66: a Polish-locale decimal comma broke a message and a test).
- Invalid input raises `WentaException` (`csharp/Wenta.Core/Units.cs`); don't return NaN or 0.
- Cite the formula's source (ASHRAE, Hendiger, Swamee-Jain...) in the PR, and derive expected test values from it.
- The sources carry no comments; don't add them. Commit 7206161 stripped comments with a tool and damaged C# verbatim strings (`@"..."`); 37888ac fixed it forward. Never strip comments mechanically.

## Plugin in ZWCAD
- `zwcad-plugin\install.ps1` needs an elevated shell and writes HKLM demand-loading keys. Ask before running it.
- Headless checks: `zwcad-plugin\full_test.scr` (script run in ZWCAD) and `zwcad-plugin\uia-check.ps1` (UI Automation).
