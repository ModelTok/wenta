# Competitive acceptance — Wentyle 6.2 and Ventpack 6.0

Tracking issue: [#38](https://github.com/ModelTok/wenta/issues/38). This file
records, item by item, whether a designer moving from one of the incumbents
finds an equivalent in Wenta today, and where the gap is tracked. It is an
honest status document: an item is only marked **done** when the code exists
in this repository and is covered by the test suite.

Competitor feature sets are those recorded in #38 from the 2026-09-03 review
of the vendors' published material. Nothing here has been verified against a
running copy of either product.

## Legend

| Mark | Meaning |
|---|---|
| **L** | Implemented in `Wenta.Core` and covered by `Wenta.Core.Tests` |
| **C** | Available as a ZWCAD command in the shipped plugin |
| **—** | Not implemented; the issue that tracks it is named |

The distinction matters: **L** means the engineering output exists and can be
produced from a network built in code or loaded from JSON. **C** means a
designer can get it from the drawing. Most of the remaining work is turning
**L** into **C**, which needs the ZWCAD 2021 SDK on the build machine.

## Wentyle / WentylePLUS 6.2 (ALNOR, ZWCAD)

| Their capability | Wenta today | Where |
|---|---|---|
| Round + rectangular duct drawing | C (single section per command) | `WENTADUCT`; continuous routing is #56 |
| Vendor fitting libraries | **L** | `Catalog.cs` — open JSON ζ-catalog, vendor merge with override warnings, spec in `csharp/catalogs/FORMAT.md`; `WENTACATALOG` demonstrates a lookup |
| Automatic bill of materials | **L**, C (reference network) | `Bom.cs`; `WENTABOM` writes `%TEMP%\wenta_bom.csv` but solves a built-in network, not the drawing (#19/#20) |
| KNR estimate tables | **L** | `KnrMap.cs` — mapping file per KNR edition, unmapped kinds reported not guessed (#54) |
| Per-vendor schedules | partial **L** | Catalog entries carry `source` provenance; grouping a BOM by vendor is not implemented (#29) |
| Dimensioned fabrication drawings of rect fittings | partial **L** | `Development.cs` flat patterns and `Fabrication.cs` areas/weights/cutting schedule produce the numbers; dimensioned drawings are #29 |
| Pressure-drop calculation | **L** | `Solver.cs` critical path, `PressureReport.cs` per-port ΔP sheet with cumulative and share columns (#47 for the command) |
| Sizing to a target | **L** | `Sizing.cs` five methods, `BatchSizing.cs` over request lists with EN/ASHRAE/DIN snapping (#24 for the command) |

## Ventpack 6.0 (Fluid Desk, BricsCAD)

| Their capability | Wenta today | Where |
|---|---|---|
| Hydraulic calculation + report | **L** | `Solver.cs`, `Results.cs` (per-component table/CSV), `Analysis.cs` (per-branch), `PressureReport.cs` |
| Sound calculation + report | **L** | `Sound.cs` — regenerated noise, room equation, NC limits per space type; per-branch noise in `Analysis.cs`. A standalone acoustic report document is not implemented (#48) |
| Numbering / labelling | **L** | `Marking.cs` branch numbering and ID marks with CSV; parametric label blocks that update on edit are #59 |
| Dimensioning | — | #58, #59 |
| Quick-connect fittings | **L** | `QuickConnect.cs` — reducer/expander/transition/flex/spacer chain with taper rule, ζ from catalog or correlation, ΔP (#57 for the command) |
| Continuous routing UX | — | #56 |
| Intelligent sections | — | #58 |
| Collision detection | **L** | `Clash.cs` — segment clearance check with CSV; `WENTACLASH` is #60 |
| Multi-storey, multi-drawing management | **L** | `MultiDrawing.cs` — drawing-scope documents linked by stable GUIDs, merged into one solvable network (#55 for the storey manager UI) |
| Material schedules | **L** | `Bom.cs` + `BomExport.cs` (JSON and XLSX) |
| Fan selection | **L** | `Fan.cs` — curves, duty point, power (#50 for the command) |
| Insulation | **L** | `Insulation.cs` — condensation and heat-loss thickness, EN ISO 12241 materials (#51 for the command) |
| Balancing | **L** | `Balancing.cs` damper ζ / open %, `ReFit.cs` per-terminal balancing hints (#49, #27) |
| Room air balance / ACH | **L** | `Room.cs`, `Units.AirChangesPerHour` (#26 for diffuser blocks) |
| Live recalculation in a panel | — | #25 |

## Where Wenta differentiates

- **Open ζ-catalog format** — `csharp/catalogs/FORMAT.md` is a published spec
  with versioning, provenance and vendor merge rules; both incumbents ship
  closed libraries. A third party can author a catalog without our code.
- **KNR mapping as data** — codes live in a JSON file per KNR edition
  (`csharp/catalogs/knr-example.json`); changing edition needs no rebuild.
- **Open exchange** — versioned network JSON (`NetworkJson.cs`) and IFC4
  export (`IfcExport.cs`) rather than a proprietary drawing database.
- **MIT licence, ZWCAD-first, one DLL** — no runtime Python/Mojo/Rust/WASM.
- **Auditable engineering** — 1000+ assertions in the test suite, each
  correlation citing its source in the XML doc comments.

## Acceptance verdict (2026-09-19)

Against the acceptance criterion in #38 — *"every output they produce has an
equivalent, every workflow step has a command, and the KNR/schedule formats
round-trip into existing deliverable templates"*:

1. **Outputs — substantially met.** Every schedule and calculation listed for
   either incumbent has a library equivalent, except per-vendor BOM grouping,
   dimensioned fabrication drawings and a standalone acoustic report
   document. The formats produced are CSV, JSON, XLSX and IFC4.
2. **Workflow steps — not met.** Five commands ship (`WENTADUCT`,
   `WENTACATALOG`, `WENTABOM`, `WENTAPANEL`, `WENTAHELLO`). The drawing-side
   workflow — trace, route, connect, size on selection, annotate, section,
   clash, storey management — is roadmap phases 2 and 3 and needs the ZWCAD
   2021 SDK, which is not installed on the current build machine, so none of
   it can be compiled or verified here.
3. **Round-trip into deliverable templates — unverified.** The KNR mapping is
   configurable and the BOM exports to XLSX, but neither has been checked
   against a real Polish estimating package (Norma, Zuzia) or a customer
   template, and the XLSX has not been opened in Excel on this machine.

**This issue cannot be closed on the strength of the library alone.** Closing
it requires: the plugin commands for phases 2–3, one real KNR template
round-trip, and a side-by-side comparison performed by a designer who uses
one of the incumbents.
