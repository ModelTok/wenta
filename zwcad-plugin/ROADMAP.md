# Wenta → C# / ZWCAD Plugin — Roadmap

**Decision (2026-09): C# is the language of the stack.** Every CAD/BIM
plugin host — ZWCAD, AutoCAD, BricsCAD, Revit, Civil 3D — speaks .NET.
**All code is C#.** The Python `wenta` reference, its Mojo port and the
Rust `venti` library served as the math oracle while `Wenta.Core` was
ported; once every module had a C# counterpart with tests they were
removed from the repo (issue #64, git tag `legacy-py-mojo-rust`).

```
csharp/Wenta.Core (pure C#, net48, zero deps)    ← ALL engineering math
        │  Wenta.Core.Tests: frozen CSV parity vectors + closed-form tests
        ▼
zwcad-plugin (WentaZwcad.dll, ZWCAD 2021)        ← core compiled in, one DLL
```

Ground rules:

- **No runtime Python/Mojo/Rust/WASM in the plugin.** One DLL (`Wenta.Core.dll`)
  next to one plugin DLL. No FFI, no cdylib staging, no Wasmtime.
- **Every engineering function lands in `Wenta.Core` with tests first.**
  The CSV vectors generated from the old oracle are frozen golden
  fixtures; new work is covered by closed-form tests in
  `Wenta.Core.Tests/Program.cs` citing the standard/correlation it
  implements. `.github/workflows/csharp.yml` runs the suite on
  `windows-latest`.
- C# never invents math: a new feature = a cited formula/table + its
  tests, then the C# implementation, then the suite goes green.
- Everything learned about ZWCAD 2021 (API DLLs, registry install, CUIX
  ribbon, evidence-log testing) stays the deployment layer — see README.

Competitive frame (verified against vendor pages + this session):

| | Wentyle (ALNOR, on ZWCAD) | Ventpack (Fluid Desk, BricsCAD) | **Wenta C# plugin** |
|---|---|---|---|
| Host | ZWCAD, bundled via ALNOR | BricsCAD + own platform | ZWCAD first |
| Math | basic ΔP | hydraulic + sound + reports | **oracle-tested, open (MIT)** |
| Fitting data | sponsored vendor libs (closed) | PartShelf24 (closed) | **open JSON catalog format** |
| BOM / estimating | KNR + vendor BOM | material schedules | BOM + fabrication + **KNR export** |
| Drawing UX | simple 2D | **best-in-class** (routing, sections) | dedicated phase below |
| 3D / clash | 3D MASTER add-on | 3D + HLR + clash | phase 7 |
| Multi-storey | partial | ✅ multi-drawing | **phase 6 (PL-market item)** |

---

## Phase 0 — Foundation (DONE, keep green)

✅ build pipeline (csc, x64), registry auto-load (`LOADCTRLS=14`),
`WENTAHELLO`/`WENTADUCT`/`WENTAPANEL`, palette, headless evidence-log
harness. 🔨 `Wenta.CUIX` ribbon package built — **MENULOAD test pending**.

## Phase 1 — `Wenta.Core` C# port (the stack conversion) ← CURRENT

Port the entire wenta library surface to `csharp/Wenta.Core`
(dependency-free, compiles with the same csc build chain, loads in ZWCAD):

| C# module | Source | Status |
|---|---|---|
| `Units` | units constants + ACH | port |
| `Fluid` (StandardAir, AirAtAltitude) | core/fluid | port |
| `Geometry` (Round, Rect, EquivalentRoundDiameter) | core/geometry | port |
| `Friction` (Re, Swamee–Jain, Colebrook) | physics/friction | port |
| `Losses`, `Flex` | physics/losses, flex | port |
| `StandardSizes` (EN 1505/1506 + nearest) | data/standard_sizes | port |
| `FittingsLibrary` (9 correlations) | components/fittings_library | port |
| `ElbowRound` (not-a-knot bicubic of Hendiger table) | components/elbow | port |
| `Components` (Port, Source, Terminal, RigidDuct, FlexDuct, TwoPortFitting, Tee) | components/* | port |
| `Network` + `Solver` (propagate, compute, critical-path DP) | network/* | port |
| `Sizing` (velocity, equal-friction, budget, NC, aspect-ratio) | sizing.py | port |
| `Schemas/Dtos` | schemas.py (pydantic ↔ C# DTOs) | port |
| **`Catalog`** (open ζ-catalog JSON, vendor merge) | new (venti FR-19 design) | port |
| **`Bom`** (bill of materials + KNR rows) | new (venti bom.rs design) | port |

> Phase 1 done: all 13 modules ported, **551/551 parity green** (vectors + inline catalog/bom/balancing/room runner).

Deliverables: `csharp/` tree, frozen CSV vectors, console test runner
`Wenta.Core.Tests` (1146 assertions), `build.cmd`, CI on `windows-latest`.

## Phase 2 — Plugin becomes real (drawing ⇄ math)

**Every engine half of this phase is in `Wenta.Core` with tests; what is
left in each item is the ZWCAD command**, which needs the ZWCAD 2021 SDK
on the build machine.

| Item | Engine | Command |
|---|---|---|
| `WENTADUCT` sizing + label + XData design record | `Sizing` ✓ | ships |
| `WENTASIZE` batch over a selection set | `BatchSizing` ✓ (5 methods, EN/ASHRAE/DIN snap, per-request errors) | #24 |
| `WENTAPRESSURE` critical-path ΔP | `PressureReport` ✓ (per-port rows, cumulative, share) | #47 |
| `WENTAREAD` / `WENTAWRITE` JSON round-trip | `NetworkJson` ✓ (schema_version 1, GUID + `wenta_class` + `drawing_scope`) | #20, #22 |
| `WENTATRACE` polylines → network | `Topology.Trace` ✓ (tee detection, flatten) | #19 |
| `WENTAFITTING` blocks → ζ | `Catalog` + `ReCorrections` ✓ | #21 |

## Phase 3 — UX: the Ventpack-class drawing experience

Where Ventpack wins today; a full phase, not an afterthought:

1. **Continuous routing** — single/multi-run drawing with mid-run diameter
   + elevation changes, offset tracing from walls (XData drives it). #56 —
   no engine half; this is drawing UX.
2. **Quick-connect** — auto-insert reducers/transitions/flexes/spacers on
   join. Engine done: `QuickConnect.Plan` picks the chain and costs it
   (taper rule, ζ from catalog or correlation). #57 for the command.
3. **Auto-network recognition** — trace polylines → `Network`. Engine done:
   `Topology.Trace`. #19 for the command.
4. **Smart annotation** — branch numbering (`Marking` ✓), size/flow/
   velocity labels as parametric blocks, auto-update on edit. #59.
5. **Intelligent sections** — section view at any angle, section entities
   excluded from BOM (Phase 5). #58 — no engine half.
6. Palette = modeless sizing form (live preview), ribbon = command
   surface; both persisted. #25 — the solver meets the budget with room to
   spare (1290 components re-solve in ~1.3 ms against a 200 ms target,
   guarded by `RunPerformance` in the suite); the threading and the form
   are the remaining work.

## Phase 4 — Beyond-wenta engineering (C# ports of venti's feature set)

The modules `venti` proved out are all in C# now (library half done in
#64); what remains per module is the plugin command
(`WENTASOUND`, `WENTABALANCE`, `WENTAFAN`, `WENTAINSULATE`, …).

| former venti module | C# file | Library | Plugin command |
|---|---|---|---|
| `balancing` (damper ζ/open-%, branch surplus-Δ) | `Balancing.cs` | ✓ | #49 |
| `room` (balance/ACH, RoomBalanceSet + CSV) | `Room.cs` | ✓ | #26 |
| `sound` (regenerated noise, room equation, NC) | `Sound.cs` | ✓ | #48 |
| `fan` (curves, duty point, power) | `Fan.cs` | ✓ | #50 |
| `insulation` (condensation / heat-loss thickness) | `Insulation.cs` | ✓ | #51 |
| `standards` (EN / ASHRAE / DIN tables) | `Standards.cs` | ✓ (#52) | — |
| `analysis` / `marking` | `Analysis.cs` `Marking.cs` | ✓ | #59 |
| `results` / `settings` / `electrical` | `Results.cs` `Settings.cs` `Electrical.cs` | ✓ | — |
| `re` (Re/size ζ corrections) | `ReCorrections.cs` | ✓ | #21 |
| `topology` (polylines → network, flatten) | `Topology.cs` | ✓ | #19 |
| `clash` (segment clearance) | `Clash.cs` | ✓ | #60 |
| `fabrication` / `development` | `Fabrication.cs` `Development.cs` | ✓ | #29 |

## Phase 5 — PL-market items (item 3 of the competitive review)

1. **Open library feed** ✓ — the ζ-catalog JSON format, three example
   catalogs and the published spec (`csharp/catalogs/FORMAT.md`), with
   vendor merge that records every override. #53 closed; choosing the
   active catalog from the ribbon is the remaining plugin bit.
2. **KNR-ready BOM** — `KnrMap` ✓ makes the codes a per-edition data file
   with unmapped kinds reported (#54 closed); `BomExport` ✓ writes JSON and
   XLSX. Per-vendor schedules and dimensioned fabrication drawings as
   drawing tables are still open (#29).
3. **Multi-drawing / multi-storey projects** — `MultiDrawing` ✓ merges
   scoped drawing documents linked by stable GUIDs into one solvable
   network, with validation; the storey-manager palette is #55.

## Phase 6 — Distribution & quality

WiX MSI (DLL + CUIX + registry + example catalog), semver, CI on a
Windows+ZWCAD2021 runner (evidence-log assert + screenshot diff), crash
discipline (no unhandled exception in ZWCAD, `wenta.log`), `WENTAHELP`.
Docs ✓: EN/PL user guide (`docs/user-guide.{en,pl}.md`), catalog spec, and
the library CI (`.github/workflows/csharp.yml`) — #37 less `WENTAHELP`.
The MSI (#36) needs WiX, which is not installed on the current machine.

## Phase 7 — 3D, clash, IFC (full-suite scope)

Elevation → solids/fittings 3D, sections/isometrics, BIM views (#60) —
drawing work, no engine half. Clash check ✓ (`Clash.cs`, segment clearance
with CSV) and IFC4 export ✓ (`IfcExport.cs` — duct segments, fittings, air
terminals, `Pset_Wenta`, deterministic GlobalIds; not yet opened in a
viewer) are in the library; `WENTACLASH` and the export command are the
remaining plugin work. After M3.

---

## Milestones

| | Phases | Outcome |
|---|---|---|
| M1 | 0+1 | full wenta math in C#, parity green, ribbon installed |
| M2 | 2 | a drawing sized/pressurized/round-tripped by the plugin |
| M3 | 3 | Ventpack-class drawing UX demo on a floor plan |
| M4 | 4+5 | sound/balancing/fan/insulation + KNR BOM + catalog feed + multi-storey |
| M5 | 6+7 | installer + CI; 3D/IFC scope |

## Non-goals (explicit)

- Runtime Python/Rust/WASM anywhere in the shipped plugin.
- Certified vendor loss data (we ship the *format*).
- Native Revit integration (later, cheaply — C# API is shared).
- Linux support (the product lives in ZWCAD; CI runs on `windows-latest`).
