default:
    @just --list

# Build Wenta.Core + the test runner and replay the parity vectors +
# closed-form regression tests (sizing, solver, fittings, spline, units,
# catalog, bom, balancing, room, standards, fan, insulation, sound, ...).
csharp-parity:
    cmd /c csharp\\build.cmd
    csharp\\bin\\Wenta.Core.Tests.exe

# Build the ZWCAD plugin (Wenta.Core compiled in) and assemble Wenta.CUIX.
# Requires the ZWCAD 2021 SDK; run csharp-parity first.
zwcad-build:
    cmd /c zwcad-plugin\\build.cmd

# Everything that can run without ZWCAD installed.
test-all: csharp-parity
    @echo "All tests passed."
