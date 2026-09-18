using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace Wenta
{
    /// <summary>KNR estimate-code mapping, configurable per KNR edition
    /// without a rebuild (issue #54). <see cref="Bom.Build(Network, KnrMap)"/>
    /// asks the map for a code per BOM row; the shipped
    /// <see cref="Default"/> reproduces the placeholder codes in
    /// <see cref="Bom.KnrMap"/>, so a BOM built without a file is unchanged.
    ///
    /// Format (mapping JSON; see <c>csharp/catalogs/knr-example.json</c>):
    /// {
    ///   "schema_version": 1,                        // integer, <= SchemaVersion
    ///   "edition": "KNR 2-08 (configure per edition)",  // free text, optional
    ///   "codes": {                                  // item kind -> code
    ///     "duct": "…", "flex": "…", "fitting": "…", "terminal": "…",
    ///     "source": ""                              // "" = deliberately no position
    ///   },
    ///   "overrides": [                              // optional, checked first
    ///     { "match": { "kind": "duct", "shape": "rectangular" }, "code": "…" },
    ///     { "match": { "kind": "fitting", "type": "tee" },       "code": "…" }
    ///   ]
    /// }
    /// Keys beginning with "_" (e.g. "_comment") are ignored everywhere, so a
    /// file can document itself although JSON has no comments.
    ///
    /// Matching in <see cref="CodeFor"/> is exact and case-sensitive:
    /// overrides are tried first, in file order, and an override matches when
    /// its <c>kind</c> equals the item kind and every <c>shape</c>/<c>type</c>
    /// it specifies equals the value the caller supplied (an override that
    /// names a shape never matches a call without one). Failing that,
    /// <c>codes[kind]</c> is used. When neither applies the result is
    /// <c>null</c> and the query is appended once to <see cref="Unmapped"/> —
    /// a missing mapping is reported, never fabricated. An explicit empty
    /// string in <c>codes</c> is a valid mapping ("no code") and is not
    /// reported.
    ///
    /// Kinds emitted by <see cref="Bom"/>: <c>duct</c>, <c>flex</c>,
    /// <c>fitting</c>, <c>terminal</c>, <c>source</c>. Shapes: <c>round</c>,
    /// <c>rectangular</c> (ducts and fittings with a cross-section; flex is
    /// always round). Fitting types: <c>tee</c>, <c>inline</c>
    /// (<see cref="TwoPortFitting"/>), otherwise the component class name.</summary>
    public sealed class KnrMap
    {
        /// <summary>One <c>overrides[]</c> entry. Null Shape/Type = "any".</summary>
        public sealed class Override
        {
            public string Kind;
            public string Shape;
            public string Type;
            public string Code;
        }

        /// <summary>Highest <c>schema_version</c> this build understands.</summary>
        public const int SchemaVersion = 1;

        /// <summary>Free-text edition label from the file (null for <see cref="Default"/>).</summary>
        public string Edition;
        public int Version = SchemaVersion;
        /// <summary>Path or label the map was parsed from ("default" for <see cref="Default"/>).</summary>
        public string Origin;

        /// <summary>Per-kind codes; an empty string means "deliberately no code".</summary>
        public readonly Dictionary<string, string> Codes = new Dictionary<string, string>();
        /// <summary>Per-section overrides, in file order (first match wins).</summary>
        public readonly List<Override> Overrides = new List<Override>();

        /// <summary>Distinct queries <see cref="CodeFor"/> could not map, in
        /// first-seen order. Format: <c>kind</c>, followed by
        /// <c> shape=…</c> and/or <c> type=…</c> when the caller supplied them,
        /// e.g. <c>"fitting shape=round type=tee"</c>.</summary>
        public readonly List<string> Unmapped = new List<string>();

        /// <summary>The built-in placeholder mapping: a copy of
        /// <see cref="Bom.KnrMap"/> taken now, with no overrides. Behaviour of
        /// a BOM built without a mapping file is therefore unchanged.</summary>
        public static KnrMap Default()
        {
            var m = new KnrMap();
            m.Origin = "default";
            foreach (KeyValuePair<string, string> kv in Bom.KnrMap)
                m.Codes[kv.Key] = kv.Value;
            return m;
        }

        /// <summary>Load a mapping from a JSON file.</summary>
        public static KnrMap Load(string path)
        {
            string json = File.ReadAllText(path);
            return Parse(json, path);
        }

        /// <summary>Parse mapping JSON. <paramref name="origin"/> (a path or
        /// label) is only used in error messages. Throws
        /// <see cref="WentaException"/> for invalid JSON, a
        /// <c>schema_version</c> that is not a positive integer or is newer
        /// than <see cref="SchemaVersion"/>, a missing or non-object
        /// <c>codes</c>, a non-string code, or a malformed override (no
        /// <c>match.kind</c>, non-string <c>code</c>, non-object entries).</summary>
        public static KnrMap Parse(string json, string origin)
        {
            var ser = new JavaScriptSerializer();
            Dictionary<string, object> root;
            try
            {
                root = ser.Deserialize<Dictionary<string, object>>(json);
            }
            catch (Exception ex)
            {
                throw new WentaException("invalid KNR map JSON: " + origin + " (" + ex.Message + ")");
            }
            if (root == null)
                throw new WentaException("invalid KNR map JSON: " + origin);

            var m = new KnrMap();
            m.Origin = origin;
            if (root.ContainsKey("edition")) m.Edition = root["edition"] as string;
            string label = "KNR map '" + (m.Edition ?? "?") + "' (" + origin + ")";

            m.Version = root.ContainsKey("schema_version")
                ? ParseVersion(root["schema_version"], label) : 1;
            if (m.Version > SchemaVersion)
                throw new WentaException(label + " declares schema_version "
                    + m.Version.ToString(CultureInfo.InvariantCulture)
                    + "; this build supports schema_version "
                    + SchemaVersion.ToString(CultureInfo.InvariantCulture) + " or older");

            var codes = root.ContainsKey("codes") ? root["codes"] as Dictionary<string, object> : null;
            if (codes == null)
                throw new WentaException(label + ": 'codes' must be an object of kind -> code");
            foreach (KeyValuePair<string, object> kv in codes)
            {
                if (kv.Key.StartsWith("_", StringComparison.Ordinal)) continue;
                m.Codes[kv.Key] = RequireString(kv.Value, label + ": codes['" + kv.Key + "']");
            }

            if (root.ContainsKey("overrides") && root["overrides"] != null)
            {
                var list = root["overrides"] as System.Collections.ArrayList;
                if (list == null)
                    throw new WentaException(label + ": 'overrides' must be an array");
                int index = 0;
                foreach (object o in list)
                {
                    string where = label + ": overrides[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                    var entry = o as Dictionary<string, object>;
                    if (entry == null)
                        throw new WentaException(where + " is not an object");
                    var match = entry.ContainsKey("match") ? entry["match"] as Dictionary<string, object> : null;
                    if (match == null)
                        throw new WentaException(where + " has no 'match' object");
                    var ov = new Override();
                    ov.Kind = match.ContainsKey("kind") ? match["kind"] as string : null;
                    if (string.IsNullOrEmpty(ov.Kind))
                        throw new WentaException(where + ".match has no 'kind'");
                    ov.Shape = OptionalString(match, "shape", where + ".match.shape");
                    ov.Type = OptionalString(match, "type", where + ".match.type");
                    if (!entry.ContainsKey("code"))
                        throw new WentaException(where + " has no 'code'");
                    ov.Code = RequireString(entry["code"], where + ".code");
                    m.Overrides.Add(ov);
                    index++;
                }
            }
            return m;
        }

        /// <summary>Code for an item, or <c>null</c> when unmapped (see the
        /// class summary for the matching rules). Unmapped queries are
        /// recorded once in <see cref="Unmapped"/>.</summary>
        public string CodeFor(string kind, string shape = null, string type = null)
        {
            if (kind == null) throw new WentaException("KnrMap.CodeFor: kind is null");
            foreach (Override ov in Overrides)
            {
                if (ov.Kind != kind) continue;
                if (ov.Shape != null && ov.Shape != shape) continue;
                if (ov.Type != null && ov.Type != type) continue;
                return ov.Code;
            }
            string code;
            if (Codes.TryGetValue(kind, out code)) return code;

            string query = QueryKey(kind, shape, type);
            if (!Unmapped.Contains(query)) Unmapped.Add(query);
            return null;
        }

        /// <summary>The <see cref="Unmapped"/> entry text for a query:
        /// <c>kind</c> plus <c> shape=…</c> / <c> type=…</c> when given.</summary>
        public static string QueryKey(string kind, string shape, string type)
        {
            string query = kind;
            if (shape != null) query += " shape=" + shape;
            if (type != null) query += " type=" + type;
            return query;
        }

        // ---- helpers -----------------------------------------------------------

        private static int ParseVersion(object v, string label)
        {
            if (v == null || v is string || v is bool)
                throw new WentaException(label + ": 'schema_version' must be a positive integer");
            double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (d != Math.Floor(d) || d < 1.0 || d > int.MaxValue)
                throw new WentaException(label + ": 'schema_version' must be a positive integer, got "
                    + d.ToString("R", CultureInfo.InvariantCulture));
            return (int)d;
        }

        private static string RequireString(object v, string where)
        {
            string s = v as string;
            if (s == null)
                throw new WentaException(where + " must be a string");
            return s;
        }

        private static string OptionalString(Dictionary<string, object> obj, string key, string where)
        {
            if (!obj.ContainsKey(key) || obj[key] == null) return null;
            return RequireString(obj[key], where);
        }
    }
}
