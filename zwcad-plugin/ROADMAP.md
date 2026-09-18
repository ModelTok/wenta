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
`Wenta.Core.Tests` (857 assertions after #64), `build.cmd`, CI on
`windows-latest`.

## Phase 2 — Plugin becomes real (drawing ⇄ math) 

- `WENTADUCT` sizes via `Sizing` (flow prompt → velocity/EF/NC method →
  EN size → drawn polyline + label with flow/velocity/Δp/m; XData carries
  the full design record).
- `WENTASIZE` batch over selection sets; live re-size on edit (UX).
- `WENTAPRESSURE` — `Network`/`Solver` on traced drawing; critical-path ΔP
  reported per source.
- `WENTAREAD` / `WENTAWRITE` — wenta-JSON round-trip (pydantic-compatible
  DTOs), stable IDs in XData.

## Phase 3 — UX: the Ventpack-class drawing experience

Where Ventpack wins today; a full phase, not an afterthought:

1. **Continuous routing** — single/multi-run drawing with mid-run diameter
   + elevation changes, offset tracing from walls (XData drives it).
2. **Quick-connect** — auto-insert reducers/transitions/flexes/spacers on
   join (EN transformation tables from `StandardSizes`).
3. **Auto-network recognition** — trace polylines → `Network` (topology,
   tee detection) so pressure results come from geometry, not menus.
4. **Smart annotation** — branch numbering (`marking` semantics), size/
   flow/velocity labels as parametric blocks, auto-update on edit.
5. **Intelligent sections** — section view at any angle, section entities
   excluded from BOM (Phase 5).
6. Palette = modeless sizing form (live preview), ribbon = command
   surface; both persisted.

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

1. **Open library feed** — the ζ-catalog JSON format (Phase 1 `Catalog`) +
   one shipped example catalog + published format spec; vendors/users
   contribute data without code (Wentyle's sponsored-library moat, opened).
2. **KNR-ready BOM** — `Bom` produces KNR-formatted estimate rows,
   per-vendor schedules, and dimensioned fabrication drawings (rect
   fittings) as drawing tables + CSV/XLSX export.
3. **Multi-drawing / multi-storey projects** — drawing-scope IDs in the
   network DTOs (schema designed in Phase 2), cross-drawing connection
   registry, storey manager palette; a network spans drawings via
   stable-GUID links.

## Phase 6 — Distribution & quality

WiX MSI (DLL + CUIX + registry + example catalog), semver, CI on a
Windows+ZWCAD2021 runner (evidence-log assert + screenshot diff), crash
discipline (no unhandled exception in ZWCAD, `wenta.log`), docs EN/PL,
`WENTAHELP`.

## Phase 7 — 3D, clash, IFC (full-suite scope)

Elevation → solids/fittings 3D, sections/isometrics, clash check,
IFC export (C# toolkit — no Python side), BIM views. After M3.

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
