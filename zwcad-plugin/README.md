# WentaZwcad — ZWCAD 2021 duct plugin (wenta C# core)

A ductwork design plugin for ZWCAD 2021, powered by **Wenta.Core** — the
C# port of the `wenta` library (see `../csharp/README.md`). The whole math
core is compiled into the plugin DLL: single-file deployment, no Python,
Mojo, Rust or WASM at runtime.

## Commands

| Command | What it does |
|---|---|
| `WENTADUCT` | Prompts shape (Rect/Round) + flow + target velocity → sizes via `Sizing.VelocityMethod` (EN 1505/1506), draws the section + label (size · flow · v · Pa/m), stores the design record in **XData** (app `WENTA`) |
| `WENTACATALOG` | Loads the open ζ-catalog (`example-generic.json`) and demos a lookup with provenance |
| `WENTABOM` | Solves the reference tee network (critical path 7.63 Pa — parity-vector value) and exports the BOM + KNR rows to `%TEMP%\wenta_bom.csv` |
| `WENTAPANEL` | Opens the dockable sizing palette: flow/velocity/shape in, live section preview, one-click `WENTADUCT` |
| `WENTAHELLO` | Version/info banner + evidence log |

## UI

- **Ribbon tab "Wenta"** — partial CUIX (`Wenta.CUIX`, built by
  `tools/MakeCuix.cs`, modeled 1:1 on ZWSOFT's own `APP+.cuix` package format).
  Buttons: Duct Section · Wenta Panel · Plugin Info · Fitting Catalog ·
  BOM + KNR. Load once with `MENULOAD`; verified live in the ZWCAD UI tree
  (`Wenta [ControlType.TabItem]`, see `uia-check.ps1`).
- **Palette** — no longer auto-opens at startup (user preference: ribbon);
  `WENTAPANEL` / ribbon button opens it on demand.

## Build → install → verify

```cmd
just csharp-parity            # 551/551 vector + catalog/bom/balancing/room assertions
just zwcad-build              # WentaZwcad.dll (core compiled in) + CUIX
powershell -ExecutionPolicy Bypass -File install.ps1   # elevated
```

Headless end-to-end (`full_test.scr`): auto-loads from the registry,
`MENULOAD`s the CUIX, runs every command, saves the DWG. Verified output:

```
plugin loaded (wenta C# core)
WENTADUCT ok  200×200 mm  0,150 m3/s  3,75 m/s  0,9530 Pa/m
WENTACATALOG ok  5 entries
WENTABOM ok  7 rows  7,63 Pa
WENTAHELLO ok  ZWCAD 21.10.21.0
```

## ZWCAD platform facts (hard-won, keep)

- Managed API = `ZwManaged.dll` (acmgd) + `ZwDatabaseMgd.dll` (acdbmgd),
  mixed-mode **x64** → compile `/platform:x64`.
- Install = HKLM registry demand-loading only (`LOADCTRLS=14`; HKCU is
  ignored; UAC required). `ZwSoft.ZwCAD.Runtime.Exception` shadows
  `System.Exception`.
- `SendStringToExecute(cmd, activate, wrapUpInactiveDoc, echoCommand)`.
- NETLOAD-in-script needs `FILEDIA 0`; MENULOAD likewise.
- Ribbon = CUIX zip-of-XML (schema in APP+/ZWCAD.CUIX); tab needs
  `DefaultDisplay="AddToWorkSpace" WorkspaceBehavior="MergeOrAddTab"`.
- Headless evidence: log lines to `%TEMP%\wenta_zwcad_test.txt`; UI
  verification via `uia-check.ps1` (UIA tree) — screenshot reading is not
  always available.

## Repo map

```
WentaZwcad/        plugin sources (Commands, Plugin, WentaPanel)
../csharp/         Wenta.Core + parity suite + vector generator + catalogs
tools/MakeCuix.cs  CUIX ribbon package builder
build.cmd install.ps1 uninstall.ps1   build & deploy
load_test.scr full_test.scr autoload_test.scr   headless test scripts
uia-check.ps1      UI-tree verification (ribbon tab presence)
ROADMAP.md         the plan (C#-first, UX phase, PL-market items)
```
