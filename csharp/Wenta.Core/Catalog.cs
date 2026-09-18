using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Wenta
{
    /// <summary>Open ζ-catalog: pluggable manufacturer loss data.
    ///
    /// The competitive answer to Wentyle's sponsored vendor libraries and
    /// Ventpack's PartShelf24: an *open* JSON format anyone can produce.
    /// Fittings are matched by id or by (type + size window); each entry
    /// carries provenance so drawings document which data sized them.
    /// The full format specification lives in <c>csharp/catalogs/FORMAT.md</c>.
    ///
    /// Format (catalog JSON):
    /// {
    ///   "name": "example-generic-rect",
    ///   "version": 1,                        // schema version (integer)
    ///   "description": "optional free text",
    ///   "fittings": [
    ///     {
    ///       "id": "rect-elbow-r1.0",          // unique within the file
    ///       "type": "rect_elbow",           // rect_elbow | round_elbow | tee |
    ///                                        // damper | diffuser | grille | ...
    ///       "size_min_mm": [100, 100],      // inclusive window [w,h] or [d]
    ///       "size_max_mm": [1200, 2000],
    ///       "zeta": 0.21,
    ///       "source": "Hendiger tab. 4.2",
    ///       "knr": "2.08.02.01",            // optional KNR estimate code
    ///       "notes": "optional free text"
    ///     }
    ///   ]
    /// }
    ///
    /// Several catalogs (a generic base plus vendor overrides) combine with
    /// <see cref="Merge(IList{ZetaCatalog})"/>: later catalogs override earlier
    /// ones by entry id, and every override is recorded in
    /// <see cref="Warnings"/> so a conflict is never silent.</summary>
    public sealed class ZetaCatalog
    {
        public sealed class CatalogEntry
        {
            public string Id;
            public string Type;
            public double[] SizeMinMm;   // may be null (applies to all sizes)
            public double[] SizeMaxMm;   // may be null
            public double Zeta;
            public string Source;
            public string Knr;
            public string Notes;

            /// <summary>Field-by-field copy (size arrays are copied too).</summary>
            public CatalogEntry Clone()
            {
                var c = new CatalogEntry();
                c.Id = Id;
                c.Type = Type;
                c.SizeMinMm = SizeMinMm == null ? null : (double[])SizeMinMm.Clone();
                c.SizeMaxMm = SizeMaxMm == null ? null : (double[])SizeMaxMm.Clone();
                c.Zeta = Zeta;
                c.Source = Source;
                c.Knr = Knr;
                c.Notes = Notes;
                return c;
            }
        }

        /// <summary>Highest catalog <c>version</c> this build understands.
        /// <see cref="Parse"/> rejects catalogs declaring a newer version.
        /// Adding optional fields does not bump it; renaming or removing
        /// fields, or changing the matching rules, does.</summary>
        public const int SchemaVersion = 1;

        public string Name;
        public string Description;
        public int Version = SchemaVersion;
        public readonly List<CatalogEntry> Fittings = new List<CatalogEntry>();

        /// <summary>Override records produced by <see cref="Merge(IList{ZetaCatalog})"/>,
        /// one line per id that a later catalog replaced:
        /// <c>id X: zeta A (source S1) overridden by zeta B (source S2)</c>.
        /// Empty for a catalog that came straight from <see cref="Parse"/>.</summary>
        public readonly List<string> Warnings = new List<string>();

        /// <summary>Load a catalog from a JSON file.</summary>
        public static ZetaCatalog Load(string path)
        {
            string json = File.ReadAllText(path);
            return Parse(json, path);
        }

        /// <summary>Parse catalog JSON. <paramref name="origin"/> (a path or
        /// label) is only used in error messages. Throws
        /// <see cref="WentaException"/> for a missing <c>fittings</c> array,
        /// a <c>version</c> newer than <see cref="SchemaVersion"/> (or not a
        /// positive integer), an entry without <c>id</c> or <c>zeta</c>, a
        /// duplicate id, or a half-specified / mismatched size window.</summary>
        public static ZetaCatalog Parse(string json, string origin)
        {
            var ser = new JavaScriptSerializer();
            Dictionary<string, object> root;
            try
            {
                root = ser.Deserialize<Dictionary<string, object>>(json);
            }
            catch (Exception ex)
            {
                throw new WentaException("invalid catalog JSON: " + origin + " (" + ex.Message + ")");
            }
            if (root == null)
                throw new WentaException("invalid catalog JSON: " + origin);
            var cat = new ZetaCatalog();
            if (root.ContainsKey("name")) cat.Name = root["name"] as string;
            if (root.ContainsKey("description")) cat.Description = root["description"] as string;
            cat.Version = root.ContainsKey("version")
                ? ParseVersion(root["version"], cat.Name, origin) : 1;
            if (cat.Version > SchemaVersion)
                throw new WentaException(
                    "catalog '" + (cat.Name ?? "?") + "' (" + origin + ") declares version "
                    + cat.Version.ToString(CultureInfo.InvariantCulture)
                    + "; this build supports catalog version "
                    + SchemaVersion.ToString(CultureInfo.InvariantCulture)
                    + " or older");
            if (!root.ContainsKey("fittings") || !(root["fittings"] is System.Collections.ArrayList))
                throw new WentaException("catalog JSON has no 'fittings' array: " + origin);
            var fittings = (System.Collections.ArrayList)root["fittings"];
            var seen = new Dictionary<string, bool>();
            int index = 0;
            foreach (object o in fittings)
            {
                var f = o as Dictionary<string, object>;
                if (f == null)
                    throw new WentaException("catalog " + origin + ": fittings["
                        + index.ToString(CultureInfo.InvariantCulture) + "] is not an object");
                var e = new CatalogEntry();
                e.Id = f.ContainsKey("id") ? f["id"] as string : null;
                if (string.IsNullOrEmpty(e.Id))
                    throw new WentaException("catalog " + origin + ": fittings["
                        + index.ToString(CultureInfo.InvariantCulture) + "] has no 'id'");
                if (seen.ContainsKey(e.Id))
                    throw new WentaException("catalog " + origin + ": duplicate fitting id '" + e.Id + "'");
                seen[e.Id] = true;
                e.Type = f.ContainsKey("type") ? f["type"] as string : null;
                if (!f.ContainsKey("zeta") || f["zeta"] == null || f["zeta"] is string || f["zeta"] is bool)
                    throw new WentaException("catalog " + origin + ": fitting '" + e.Id + "' has no numeric 'zeta'");
                e.Zeta = Convert.ToDouble(f["zeta"], CultureInfo.InvariantCulture);
                e.Source = f.ContainsKey("source") ? f["source"] as string : null;
                e.Knr = f.ContainsKey("knr") ? f["knr"] as string : null;
                e.Notes = f.ContainsKey("notes") ? f["notes"] as string : null;
                e.SizeMinMm = ToDoubles(f.ContainsKey("size_min_mm") ? f["size_min_mm"] : null);
                e.SizeMaxMm = ToDoubles(f.ContainsKey("size_max_mm") ? f["size_max_mm"] : null);
                if ((e.SizeMinMm == null) != (e.SizeMaxMm == null))
                    throw new WentaException("catalog " + origin + ": fitting '" + e.Id
                        + "' must give both 'size_min_mm' and 'size_max_mm' or neither");
                if (e.SizeMinMm != null && e.SizeMinMm.Length != e.SizeMaxMm.Length)
                    throw new WentaException("catalog " + origin + ": fitting '" + e.Id
                        + "' has size_min_mm/size_max_mm of different lengths");
                cat.Fittings.Add(e);
                index++;
            }
            return cat;
        }

        /// <summary>Exact-id lookup.</summary>
        public CatalogEntry ById(string id)
        {
            foreach (CatalogEntry e in Fittings)
                if (e.Id == id) return e;
            return null;
        }

        /// <summary>Match by type and size window (mm). First match wins;
        /// entries without a size window match any size of that type.</summary>
        public CatalogEntry Match(string type, double[] sizeMm)
        {
            foreach (CatalogEntry e in Fittings)
            {
                if (e.Type != type) continue;
                if (e.SizeMinMm == null) return e;
                bool ok = true;
                for (int i = 0; i < sizeMm.Length && i < e.SizeMinMm.Length; i++)
                {
                    if (sizeMm[i] < e.SizeMinMm[i] || sizeMm[i] > e.SizeMaxMm[i])
                    { ok = false; break; }
                }
                if (ok) return e;
            }
            return null;
        }

        /// <summary>ζ for (type, size); falls back to the built-in
        /// correlation library when the catalog has no match.</summary>
        public double ZetaFor(string type, double[] sizeMm)
        {
            CatalogEntry e = Match(type, sizeMm);
            if (e != null) return e.Zeta;
            return FittingsCorrelationFallback(type, sizeMm);
        }

        // ---- vendor merge ---------------------------------------------------

        /// <summary>Combine several catalogs into one. Entries are keyed by id:
        /// an id already present is <b>replaced in place</b> (it keeps the
        /// earlier catalog's position, so <see cref="Match"/> order is stable)
        /// and every such override is appended to <see cref="Warnings"/>; ids
        /// not seen before are appended after all earlier entries. Later
        /// catalogs therefore win over earlier ones — pass the generic base
        /// first and the vendor overrides last. Entries are copied, so the
        /// result does not alias its inputs. <c>Name</c> is the inputs' names
        /// joined with "+", <c>Version</c> the highest input version.</summary>
        public static ZetaCatalog Merge(IList<ZetaCatalog> catalogs)
        {
            if (catalogs == null || catalogs.Count == 0)
                throw new WentaException("Merge needs at least one catalog");
            var merged = new ZetaCatalog();
            var names = new StringBuilder();
            int version = 0;
            var position = new Dictionary<string, int>();
            // Catalog that currently owns each merged slot (for the warning's
            // "(source ...)" fallback when an entry has no source of its own).
            var owner = new List<ZetaCatalog>();
            for (int c = 0; c < catalogs.Count; c++)
            {
                ZetaCatalog cat = catalogs[c];
                if (cat == null)
                    throw new WentaException("Merge: catalog #"
                        + c.ToString(CultureInfo.InvariantCulture) + " is null");
                if (!string.IsNullOrEmpty(cat.Name))
                {
                    if (names.Length > 0) names.Append('+');
                    names.Append(cat.Name);
                }
                if (cat.Version > version) version = cat.Version;
                // Warnings of already-merged inputs are carried over.
                foreach (string w in cat.Warnings) merged.Warnings.Add(w);
                foreach (CatalogEntry e in cat.Fittings)
                {
                    CatalogEntry copy = e.Clone();
                    int at;
                    if (position.TryGetValue(e.Id, out at))
                    {
                        CatalogEntry old = merged.Fittings[at];
                        merged.Warnings.Add(OverrideWarning(old, owner[at], copy, cat));
                        merged.Fittings[at] = copy;
                        owner[at] = cat;
                    }
                    else
                    {
                        position[e.Id] = merged.Fittings.Count;
                        merged.Fittings.Add(copy);
                        owner.Add(cat);
                    }
                }
            }
            merged.Name = names.Length > 0 ? names.ToString() : null;
            merged.Version = version > 0 ? version : SchemaVersion;
            return merged;
        }

        /// <summary>Two-catalog convenience: <c>Merge(new[] { baseCatalog, overrides })</c>.</summary>
        public static ZetaCatalog Merge(ZetaCatalog baseCatalog, ZetaCatalog overrides)
        {
            return Merge(new ZetaCatalog[] { baseCatalog, overrides });
        }

        /// <summary>Exact line format (documented in FORMAT.md, keep in sync):
        /// <c>id {id}: zeta {old} (source {oldSource}) overridden by zeta {new} (source {newSource})</c>.</summary>
        private static string OverrideWarning(CatalogEntry old, ZetaCatalog oldCatalog,
                                              CatalogEntry replacement, ZetaCatalog replacementCatalog)
        {
            return "id " + old.Id
                + ": zeta " + FormatZeta(old.Zeta)
                + " (source " + Provenance(old, oldCatalog) + ")"
                + " overridden by zeta " + FormatZeta(replacement.Zeta)
                + " (source " + Provenance(replacement, replacementCatalog) + ")";
        }

        /// <summary>Entry source, falling back to the owning catalog's name,
        /// then "unknown" — so a warning never reads "(source )".</summary>
        private static string Provenance(CatalogEntry e, ZetaCatalog owner)
        {
            if (!string.IsNullOrEmpty(e.Source)) return e.Source;
            if (owner != null && !string.IsNullOrEmpty(owner.Name)) return owner.Name;
            return "unknown";
        }

        private static string FormatZeta(double z)
        {
            // Culture-invariant so warnings are byte-identical on pl-PL machines.
            return z.ToString("R", CultureInfo.InvariantCulture);
        }

        // ---- helpers -----------------------------------------------------------

        private static int ParseVersion(object v, string name, string origin)
        {
            string label = "catalog '" + (name ?? "?") + "' (" + origin + ")";
            if (v == null || v is string || v is bool)
                throw new WentaException(label + ": 'version' must be a positive integer");
            double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (d != Math.Floor(d) || d < 1.0 || d > int.MaxValue)
                throw new WentaException(label + ": 'version' must be a positive integer, got "
                    + d.ToString("R", CultureInfo.InvariantCulture));
            return (int)d;
        }

        private static double FittingsCorrelationFallback(string type, double[] s)
        {
            switch (type)
            {
                case "rect_elbow":
                    return FittingsLibrary.RectangularElbow(s[0] / 1000.0, s[1] / 1000.0,
                                                             s[0] / 1000.0, 90.0);
                case "mitered_elbow":
                    return FittingsLibrary.MiteredElbow(90.0, false);
                case "damper":
                    return FittingsLibrary.DamperButterfly(100.0);
                case "diffuser":
                    return FittingsLibrary.DiffuserCeiling(1.0);
                case "grille":
                    return FittingsLibrary.GrilleReturn(0.15);
                case "tee":
                    return 0.5;
                default:
                    throw new WentaException(
                        "no catalog entry and no correlation for type '" + type + "'");
            }
        }

        private static double[] ToDoubles(object o)
        {
            if (o == null) return null;
            var arr = o as System.Collections.ArrayList;
            if (arr == null)
                throw new WentaException("size_min_mm / size_max_mm must be JSON arrays of numbers");
            var d = new double[arr.Count];
            for (int i = 0; i < arr.Count; i++)
                d[i] = Convert.ToDouble(arr[i], CultureInfo.InvariantCulture);
            return d;
        }
    }
}
