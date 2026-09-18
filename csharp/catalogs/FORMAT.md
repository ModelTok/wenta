# Wenta ζ-catalog format — version 1

An open, vendor-neutral JSON format for duct-fitting loss coefficients (ζ).
Anyone — a manufacturer, a design office, a single engineer — can publish a
catalog file; Wenta loads it at run time and it changes sizing without any
code change. Several catalogs (a generic base plus vendor overrides) merge
into one lookup, and every override is reported so a conflict is never silent.

Reference implementation: `csharp/Wenta.Core/Catalog.cs` (`Wenta.ZetaCatalog`).
Example files in this folder:

| File | Purpose |
|---|---|
| `example-generic.json` | Rectangular-duct starter set (ids `rect-elbow-r1.5`, `rect-elbow-r1.0`, `round-elbow-r1.0`, `tee-branch-typical`, `vav-box`). Shipped with the ZWCAD plugin. |
| `example-generic-round.json` | Round-duct starter set, one-element size windows `[d]`. |
| `example-vendor-style.json` | What a manufacturer file looks like: product-code ids, per-series windows, deliberate overrides of two generic ids. The manufacturer "ExampleVent" is **fictional**; every number is invented. |

None of the example values is measured vendor data. They are typical
textbook-order figures chosen to exercise the format. Replace them before
sizing real work.

---

## 1. File

- Encoding: UTF-8 (a BOM is tolerated). Extension `.json`.
- Top level: one JSON object. No comments (JSON has none) — use `description`
  and `notes` for prose.
- Numbers are plain JSON numbers with `.` as the decimal separator; readers
  must parse them culture-invariantly (a Polish locale must not turn `0.21`
  into `0,21`).
- Unknown keys at any level are **ignored** by readers (this is what makes
  adding optional fields non-breaking — see §6).

### 1.1 Top-level fields

| Field | Type | Required | Meaning |
|---|---|---|---|
| `name` | string | recommended | Short identifier of the catalog, e.g. `example-generic-rect`, `ExampleVent-fictional`. Appears in error messages, in merged-catalog names (§5) and in override warnings when an entry has no `source`. |
| `version` | integer ≥ 1 | recommended (default `1`) | **Schema** version of this file — the format the reader must understand, *not* the vendor's data revision. See §6. Put a data revision/date in `description`. |
| `description` | string | optional | Free text: scope, data revision, licence, contact, caveats. |
| `fittings` | array of entry objects | **required** | The entries, in author order. Order matters for by-type matching (§3.2). |

A file with no `fittings` array (or with `fittings` that is not an array) is
rejected.

### 1.2 Entry fields

| Field | Type | Required | Meaning |
|---|---|---|---|
| `id` | non-empty string | **required** | Key for exact lookup and for merging (§5). Unique within the file; a duplicate id is rejected. Any string is allowed — a manufacturer product code (`EV-RB-280-630`) is a fine id. See §7 for conventions. |
| `type` | string | optional (strongly recommended) | Fitting class used for by-type matching and for the correlation fallback (§4). Well-known values: `rect_elbow`, `round_elbow`, `mitered_elbow`, `tee`, `tee_straight`, `reducer`, `damper`, `diffuser`, `grille`. Authors may invent further types; a type with no built-in correlation simply has no fallback. An entry without `type` can only be found with `ById`. |
| `size_min_mm` | array of numbers | optional, paired | Lower bound of the inclusive size window, **millimetres**. `[w, h]` for rectangular (width, height), `[d]` for round. Must be given together with `size_max_mm` and have the same length. |
| `size_max_mm` | array of numbers | optional, paired | Upper bound, same shape as `size_min_mm`. Omit both to make the entry apply to every size of its `type`. |
| `zeta` | number | **required** | Loss coefficient ζ (dimensionless), referred to the velocity the fitting convention uses (elbows/dampers: duct velocity; tee branch leg: branch velocity; tee straight leg: main velocity; reducers: outlet velocity). State the reference velocity in `source` or `notes` if it is not the obvious one. A string or boolean is rejected. |
| `source` | string | optional (strongly recommended) | Provenance: where the number comes from — a table in a handbook, a manufacturer data sheet with edition/date, a measurement report. Wenta prints it next to every fitting it sizes and in every override warning. An entry with no `source` is reported under its catalog `name`, or `unknown`. |
| `knr` | string | optional | KNR (Polish *Katalog Nakładów Rzeczowych*) estimate code for this fitting. Codes differ between KNR editions and publishers, so a shipped catalog should say so — the examples use `"KNR 2-08 03xx (configure per edition)"`. Per-project mapping of kinds to real codes is issue #54 and lives outside the catalog. |
| `notes` | string | optional | Free text for the human reader (installation limits, why the entry is ordered where it is, etc.). Never interpreted. |

Validation performed by the reference reader (`ZetaCatalog.Parse`), all as
`WentaException` with the file path or label in the message:

- not valid JSON / not an object;
- `version` missing → treated as `1`; present but not a positive integer
  (string, boolean, `0`, `1.5`) → rejected; greater than the reader's
  `SchemaVersion` → rejected, naming both numbers (§6);
- no `fittings` array;
- an element of `fittings` that is not an object;
- entry without `id`, or duplicate `id`;
- entry without a numeric `zeta`;
- exactly one of `size_min_mm` / `size_max_mm` given, or the two arrays of
  different length, or either not an array of numbers.

### 1.3 Minimal valid file

```json
{
  "name": "my-office-defaults",
  "version": 1,
  "fittings": [
    { "id": "round-elbow-rd1.0", "type": "round_elbow", "zeta": 0.24,
      "source": "Hendiger tab. 4.3, R/D = 1.0" }
  ]
}
```

### 1.4 Full entry

```json
{
  "id": "EV-RB-280-630",
  "type": "round_elbow",
  "size_min_mm": [280],
  "size_max_mm": [630],
  "zeta": 0.24,
  "source": "ExampleVent RB segmented bend 90 deg, R/D = 1.0, D 280-630 (fictional example - not real vendor data)",
  "knr": "KNR 2-08 03xx (configure per edition)",
  "notes": "Referred to duct velocity."
}
```

---

## 2. Units and conventions

- All sizes are **millimetres**. Rectangular: `[width, height]`; round:
  `[diameter]`.
- ζ is dimensionless; pressure loss is Δp = ζ · ρ · v² / 2 with v the
  reference velocity of the fitting convention (§1.2, `zeta`).
- Angles, radii ratios and other parameters are *not* fields — encode them in
  the `id`/`type` and describe them in `source`. A catalog is a table of
  constants, not a correlation.

---

## 3. Matching rules

### 3.1 By id — `ById(id)`

Exact, case-sensitive string comparison. Returns the entry or `null`.

### 3.2 By type and size — `Match(type, sizeMm)`

Entries are scanned **in file order** (in merge order for a merged catalog).
The first entry that satisfies all of the following wins:

1. `entry.type == type` (exact, case-sensitive);
2. either the entry has no size window, **or** for every dimension `i`
   compared, `size_min_mm[i] <= sizeMm[i] <= size_max_mm[i]` (inclusive both
   ends). The number of dimensions compared is the shorter of the query and
   the window; extra dimensions on either side are ignored.

Returns `null` if nothing matches. Consequences authors must know:

- **First match wins.** Put the entry you want as the default for a type
  first, or give alternatives their own `type`. Two entries of the same type
  with identical windows: only the first is ever reached by `Match`
  (`example-generic-round.json`, entry `round-elbow-rd2.0`, shows this).
- **No window = any size.** A windowless entry placed before windowed entries
  of the same type shadows all of them.
- Windows are inclusive: `[100]`–`[250]` and `[250]`–`[630]` both match
  `d = 250`; the earlier one wins. Use non-overlapping series (`100–250`,
  `280–630`, `710–1250`) as in the vendor example.

### 3.3 ζ with fallback — `ZetaFor(type, sizeMm)`

`Match` first; if it returns an entry, its `zeta`. Otherwise §4.

---

## 4. Fallback to built-in correlations

When no entry matches, `ZetaFor` falls back to `Wenta.FittingsLibrary`
so a sparse catalog still sizes a whole network:

| `type` | Fallback |
|---|---|
| `rect_elbow` | `RectangularElbow(w, h, r = w, 90°)` — needs `sizeMm = [w, h]` |
| `mitered_elbow` | `MiteredElbow(90°, unvaned)` = 1.20 |
| `damper` | `DamperButterfly(100 % open)` = 0.10 |
| `diffuser` | `DiffuserCeiling(1.0)` = 0.40 |
| `grille` | `GrilleReturn(0.15)` = 0.2875 |
| `tee` | constant 0.5 |
| any other (`round_elbow`, `tee_straight`, `reducer`, vendor-invented types) | **no fallback** — `WentaException` "no catalog entry and no correlation for type '…'" |

A catalog entry therefore always wins over the correlation, and for types
without a fallback the catalog is the only source of ζ.

---

## 5. Merge and override rules

`ZetaCatalog.Merge(IList<ZetaCatalog>)` (and the two-argument convenience
`Merge(baseCatalog, overrides)`) combine catalogs **in the order given**;
pass the generic base first and the most authoritative vendor file last.

1. Entries are keyed by `id`.
2. An `id` not seen before is **appended** after every entry already merged
   (so catalog 1's entries precede catalog 2's new entries, etc.).
3. An `id` already present is **replaced in place**: the later entry takes
   the earlier entry's slot, keeping its position in the `Match` order, and
   every field (type, window, zeta, source, knr, notes) comes from the later
   entry — there is no field-level merging.
4. Every replacement appends exactly one line to the merged catalog's
   `Warnings` list, in this exact format:

   ```
   id {id}: zeta {old} (source {oldSource}) overridden by zeta {new} (source {newSource})
   ```

   `{old}`/`{new}` are the ζ values formatted culture-invariantly with the
   shortest round-trip representation (`0.30` prints as `0.3`, `0.21` as
   `0.21`). `{oldSource}`/`{newSource}` are the entry's `source`, else the
   owning catalog's `name`, else `unknown`. Example produced by merging
   `example-generic.json` then `example-vendor-style.json`:

   ```
   id vav-box: zeta 0.35 (source generic VAV box fully open (example entry)) overridden by zeta 0.3 (source ExampleVent VAV-1 fully open (fictional example - not real vendor data))
   ```

5. `Warnings` already present on an input (because it was itself a merge
   result) are carried over first, so merges compose.
6. Merged `name` = input names joined with `+` (inputs without a name are
   skipped); merged `version` = the highest input version. Entries are
   copied, never aliased.
7. Within a single file duplicate ids are an *error* (§1.2); only across files
   is a repeated id an *override*. Merging one catalog with itself therefore
   produces one warning per entry and changes nothing.

Tools that surface the merged catalog (the `WENTACATALOG` command, reports)
should print `Warnings` verbatim: a conflict between a handbook value and a
vendor value is information the engineer must see.

---

## 6. Versioning

- `version` is a single **integer** schema version; this document describes
  version **1**. A reader exposes the highest version it understands
  (`ZetaCatalog.SchemaVersion`, currently `1`).
- A reader **must reject** a file whose `version` is greater than the one it
  understands, with a message naming both numbers (reference wording:
  `catalog 'NAME' (PATH) declares version 99; this build supports catalog
  version 1 or older`). It must accept any lower or equal version.
- **Non-breaking, no bump:** adding a new *optional* field at the top level or
  in an entry; adding new well-known `type` values; adding a new fallback
  correlation. Readers ignore fields they do not know, so a version-1 reader
  keeps working on such a file.
- **Breaking, bump required:** renaming or removing a field, changing a
  field's type or units, making an optional field required, or changing the
  matching/merge rules of §3 and §5.
- A missing `version` means `1`. Always write it explicitly.
- The vendor's *data* revision (catalog edition, date) is not `version`;
  record it in `description` and/or each `source`.

---

## 7. Provenance, ids and KNR

**`source`** is the field that makes a catalog worth trusting. Write it so a
reviewer can find the number: publication and table (`Hendiger tab. 4.2,
R = 1.0·W`), or manufacturer + product series + data-sheet edition. If the
value is a typical/textbook figure rather than measured data, say so — the
shipped examples use `typical value, … example entry - replace with vendor
data` and `fictional example - not real vendor data`. Never present invented
numbers as vendor data.

**Ids.** Any non-empty string unique within the file. Conventions used by
the examples, so that generic and vendor files can deliberately share ids:

- generic entries: lower-case, hyphenated, `<shape>-<fitting>-<parameter>` —
  `rect-elbow-r1.0` (R = 1.0·W), `round-elbow-rd1.5` (R/D = 1.5),
  `round-tee-branch`, `vav-box`;
- vendor entries: the product code, optionally with the size series —
  `EV-RB-280-630`;
- to **replace** a generic value with vendor data, reuse the generic id
  (that is what triggers the override and the warning); to **add** coverage,
  use a new id.

**`knr`.** KNR estimate codes depend on the KNR edition and publisher a
project uses, so a general-purpose catalog cannot ship the "right" code. Put
the chapter-level code you know plus an explicit "configure per edition"
note, as the examples do, or omit the field. The per-project mapping from
fitting kinds to real codes is a separate configuration file (issue #54);
`knr` in a catalog is a hint for that mapping, not a substitute for it.

---

## 8. Authoring checklist

1. Start from `example-generic-round.json` or `example-vendor-style.json`;
   keep `"version": 1`.
2. Set `name` (short, stable — it appears in warnings) and `description`
   (scope, data edition/date, licence, contact).
3. One entry per product/size series. Give every entry an `id` (unique),
   a `type` (from the well-known list where possible), a numeric `zeta` and a
   `source`.
4. Sizes in **mm**, `[w, h]` or `[d]`; always both `size_min_mm` and
   `size_max_mm`, same length, inclusive, non-overlapping across series of
   the same type. Omit both only if the value truly applies to every size.
5. Order entries of the same type from most specific to least specific; put
   the default you want `Match` to return first. Remember that a windowless
   entry shadows everything after it of the same type.
6. State the reference velocity when it is not the duct velocity (tee
   branch, reducer outlet).
7. If you intend to override a generic value, reuse the generic id; if you
   intend to add coverage, use a new id.
8. Fill `knr` only with an honest per-edition note, or leave it out.
9. Validate: load the file with `ZetaCatalog.Load(path)` (or the
   `WENTACATALOG` command) — it must load without a `WentaException`; then
   merge it after the generic base and read the `Warnings` to confirm the
   overrides are exactly the ones you meant.
10. Do not put real measurements behind a fictional label, or invented
    numbers behind a real manufacturer's name.
