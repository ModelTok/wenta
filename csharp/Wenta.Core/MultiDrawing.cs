using System;
using System.Collections.Generic;
using System.Globalization;

namespace Wenta
{
    /// <summary>One cross-drawing connection: the outlet of the component with
    /// <see cref="FromGuid"/> feeds the inlet of the component with
    /// <see cref="ToGuid"/>, where the two components live in *different*
    /// drawings. GUIDs (not component ids) are used because ids are only
    /// unique inside a single drawing, while the GUID written by
    /// <see cref="NetworkJson"/> survives a re-import of that drawing.
    ///
    /// <see cref="FromPort"/> defaults to the source component's (single)
    /// outlet and <see cref="ToPort"/> to the target component's (single)
    /// inlet; name them explicitly when the component has more than one, which
    /// is how a riser <see cref="Tee"/> drives a link from its
    /// <c>straight</c> or <c>branch</c> leg.</summary>
    public sealed class DrawingLink
    {
        /// <summary>GUID of the upstream component (in some document).</summary>
        public string FromGuid;

        /// <summary>GUID of the downstream component (in another document).</summary>
        public string ToGuid;

        /// <summary>Out-port on the upstream component; null/empty = its only
        /// out-port ("outlet" for ducts and fittings).</summary>
        public string FromPort;

        /// <summary>In-port on the downstream component; null/empty = its only
        /// in-port ("inlet", or "combined" for a <see cref="Tee"/>).</summary>
        public string ToPort;
    }

    /// <summary>One drawing's worth of network: the scope name (storey /
    /// drawing / model-space identifier), the parsed <see cref="Network"/>, and
    /// the per-component-id <see cref="ComponentMeta"/> side table that carries
    /// the GUIDs the links refer to.</summary>
    public sealed class DrawingDocument
    {
        /// <summary>Drawing/storey name, e.g. "riser", "L1". Used as the id
        /// prefix in the merged network; must be non-empty.</summary>
        public string Scope;

        /// <summary>The drawing's own network, ids unique within the drawing.</summary>
        public Network Network;

        /// <summary>GUID / drawing scope per component id of <see cref="Network"/>.</summary>
        public Dictionary<string, ComponentMeta> Meta;
    }

    /// <summary>Multi-storey / multi-drawing networks (issue #55, library half):
    /// stitch several per-drawing <see cref="Network"/>s into one hydraulic
    /// network that the ordinary <see cref="Solver"/> can solve, using stable
    /// GUIDs as the cross-drawing glue.
    ///
    /// Intended storey pattern:
    /// <list type="bullet">
    /// <item>one *riser* drawing holds the <see cref="Source"/> (AHU/fan), the
    /// vertical riser ducts and the riser <see cref="Tee"/>s — one tee leg per
    /// storey take-off;</item>
    /// <item>each *storey* drawing (one per floor) has **no** <see cref="Source"/>;
    /// it starts at a duct whose <c>inlet</c> is fed by a
    /// <see cref="DrawingLink"/> from a riser tee leg
    /// (<c>straight</c> = carry on up the riser, <c>branch</c> = take off to the
    /// storey) and ends at that storey's terminals;</item>
    /// <item>the storey manager (plugin half) keeps the drawing list and the
    /// link list; this class only needs the exported JSON of each drawing.</item>
    /// </list>
    ///
    /// Merged component ids are "&lt;scope&gt;/&lt;id&gt;", which keeps ids from
    /// colliding between drawings and keeps the originating storey readable in
    /// solver output, BOM and pressure reports. Because ids are split on the
    /// first '.', neither a scope nor a component id may contain a '.'.
    ///
    /// <see cref="Merge"/> re-parents the document's <see cref="Component"/>
    /// instances into the merged network (it does not clone them), so the
    /// per-drawing <see cref="Network"/> objects should be treated as consumed
    /// once merged; reload them with <see cref="LoadDocument"/> if they are
    /// needed again.</summary>
    public static class MultiDrawing
    {
        // ---- load ----------------------------------------------------------

        /// <summary>Parse one drawing's network JSON (see <see cref="NetworkJson"/>)
        /// into a <see cref="DrawingDocument"/> tagged with
        /// <paramref name="scope"/>: components that carry no
        /// <c>drawing_scope</c> get <paramref name="scope"/>, components that
        /// carry no <c>guid</c> get a fresh one so that a
        /// <see cref="DrawingLink"/> can name them. Throws
        /// <see cref="WentaException"/> for an empty scope or malformed JSON.</summary>
        public static DrawingDocument LoadDocument(string json, string scope)
        {
            if (string.IsNullOrEmpty(scope))
                throw new WentaException("MultiDrawing.LoadDocument: drawing scope is required");

            Dictionary<string, ComponentMeta> meta;
            Network net = NetworkJson.Parse(json, out meta);
            if (meta == null) meta = new Dictionary<string, ComponentMeta>();

            foreach (KeyValuePair<string, Component> kv in net.Components)
            {
                ComponentMeta m;
                if (!meta.TryGetValue(kv.Key, out m) || m == null)
                {
                    m = new ComponentMeta();
                    meta[kv.Key] = m;
                }
                if (string.IsNullOrEmpty(m.DrawingScope)) m.DrawingScope = scope;
                if (string.IsNullOrEmpty(m.Guid)) m.Guid = System.Guid.NewGuid().ToString("D");
            }

            var doc = new DrawingDocument();
            doc.Scope = scope;
            doc.Network = net;
            doc.Meta = meta;
            return doc;
        }

        /// <summary>Merged id of a component: "&lt;scope&gt;/&lt;id&gt;".</summary>
        public static string QualifiedId(string scope, string componentId)
        {
            return scope + "/" + componentId;
        }

        // ---- merge ---------------------------------------------------------

        /// <summary>Stitch <paramref name="docs"/> into a single solvable
        /// <see cref="Network"/>: every component keeps its physics but is
        /// re-registered under <see cref="QualifiedId"/>, every intra-document
        /// connection is rebuilt, and every <see cref="DrawingLink"/> becomes an
        /// ordinary connection. <paramref name="mergedMeta"/> receives the
        /// GUID / drawing scope of each merged component (keyed by the merged
        /// id), ready for <see cref="ToJson"/>.
        ///
        /// Throws <see cref="WentaException"/> when a document has no scope,
        /// when one GUID appears in two documents, when a link names a GUID no
        /// document owns, or when both ends of a link live in the same document
        /// (that is an ordinary <see cref="Network.Connect"/>, not a
        /// cross-drawing link). Port problems (unknown port name, wrong
        /// direction) surface from <see cref="Network.Connect"/>.</summary>
        public static Network Merge(IList<DrawingDocument> docs, IList<DrawingLink> links,
                                    out Dictionary<string, ComponentMeta> mergedMeta)
        {
            if (docs == null || docs.Count == 0)
                throw new WentaException("MultiDrawing.Merge: no drawing documents");

            var ownerDoc = new Dictionary<string, int>();     // guid -> document index
            var ownerCid = new Dictionary<string, string>();  // guid -> component id
            for (int i = 0; i < docs.Count; i++)
            {
                DrawingDocument doc = docs[i];
                if (doc == null || doc.Network == null)
                    throw new WentaException("MultiDrawing.Merge: document "
                        + i.ToString(CultureInfo.InvariantCulture) + " is empty");
                if (string.IsNullOrEmpty(doc.Scope))
                    throw new WentaException("MultiDrawing.Merge: document "
                        + i.ToString(CultureInfo.InvariantCulture) + " has no drawing scope");

                foreach (KeyValuePair<string, Component> kv in doc.Network.Components)
                {
                    string guid = GuidOf(doc, kv.Key);
                    if (string.IsNullOrEmpty(guid)) continue;
                    if (ownerDoc.ContainsKey(guid))
                        throw new WentaException("MultiDrawing.Merge: guid '" + guid
                            + "' is used by component '" + ownerCid[guid] + "' of drawing '"
                            + docs[ownerDoc[guid]].Scope + "' and by component '" + kv.Key
                            + "' of drawing '" + doc.Scope + "'");
                    ownerDoc[guid] = i;
                    ownerCid[guid] = kv.Key;
                }
            }

            var merged = new Network();
            var name = "";
            mergedMeta = new Dictionary<string, ComponentMeta>();

            // 1. components
            for (int i = 0; i < docs.Count; i++)
            {
                DrawingDocument doc = docs[i];
                name += (name.Length > 0 ? "+" : "") + doc.Scope;
                foreach (KeyValuePair<string, Component> kv in doc.Network.Components)
                {
                    string qid = QualifiedId(doc.Scope, kv.Key);
                    merged.Add(qid, kv.Value);
                    var m = new ComponentMeta();
                    m.Guid = GuidOf(doc, kv.Key);
                    m.DrawingScope = ScopeOf(doc, kv.Key);
                    mergedMeta[qid] = m;
                }
            }
            merged.Name = name;

            // 2. intra-document connections, enumerated exactly the way
            //    NetworkJson.Serialize does (every predecessor of an in-port
            //    node is an out-port node "srcId:srcPort").
            foreach (DrawingDocument doc in docs)
            {
                foreach (KeyValuePair<string, Component> kv in doc.Network.Components)
                {
                    foreach (Port p in kv.Value.Ports)
                    {
                        if (!p.IsIn) continue;
                        foreach (string pred in doc.Network.Predecessors(
                                     Network.PortNodeId(kv.Key, p.Name)))
                        {
                            int colon = pred.LastIndexOf(':');
                            if (colon < 0) continue;
                            string srcId = pred.Substring(0, colon);
                            string srcPort = pred.Substring(colon + 1);
                            merged.Connect(QualifiedId(doc.Scope, srcId) + "." + srcPort,
                                           QualifiedId(doc.Scope, kv.Key) + "." + p.Name);
                        }
                    }
                }
            }

            // 3. cross-drawing links
            if (links != null)
            {
                foreach (DrawingLink link in links)
                {
                    if (link == null)
                        throw new WentaException("MultiDrawing.Merge: null drawing link");
                    int fromDoc = RequireOwner(ownerDoc, link.FromGuid, "from");
                    int toDoc = RequireOwner(ownerDoc, link.ToGuid, "to");
                    if (fromDoc == toDoc)
                        throw new WentaException("MultiDrawing.Merge: link '" + link.FromGuid
                            + "' -> '" + link.ToGuid + "' has both ends in drawing '"
                            + docs[fromDoc].Scope + "'; use Network.Connect for a link inside one drawing");
                    merged.Connect(
                        QualifiedId(docs[fromDoc].Scope, ownerCid[link.FromGuid]) + PortSuffix(link.FromPort),
                        QualifiedId(docs[toDoc].Scope, ownerCid[link.ToGuid]) + PortSuffix(link.ToPort));
                }
            }
            return merged;
        }

        /// <summary>Serialise a merged network back to network JSON; the merged
        /// ids ("&lt;scope&gt;/&lt;id&gt;") and the per-component GUID /
        /// drawing scope are written as they stand, so the whole multi-drawing
        /// assembly round-trips through <see cref="NetworkJson.Parse(string)"/>.</summary>
        public static string ToJson(Network merged, Dictionary<string, ComponentMeta> mergedMeta)
        {
            return NetworkJson.Serialize(merged, mergedMeta);
        }

        // ---- validate ------------------------------------------------------

        /// <summary>Problems with a document + link set; empty list = healthy.
        /// Never throws. Reports: a document without a scope, a GUID owned by
        /// two documents, a link naming an unknown GUID, a link with both ends
        /// in one document, a link leaving a port that is not an out-port or
        /// arriving at a port that is not an in-port (including port names the
        /// component does not have), and a document that has neither a
        /// <see cref="Source"/> of its own nor an incoming link — i.e. a storey
        /// drawing nothing feeds.</summary>
        public static List<string> Validate(IList<DrawingDocument> docs, IList<DrawingLink> links)
        {
            var problems = new List<string>();
            if (docs == null || docs.Count == 0)
            {
                problems.Add("no drawing documents");
                return problems;
            }

            var ownerDoc = new Dictionary<string, int>();
            var ownerCid = new Dictionary<string, string>();
            for (int i = 0; i < docs.Count; i++)
            {
                DrawingDocument doc = docs[i];
                string where = "document " + i.ToString(CultureInfo.InvariantCulture);
                if (doc == null || doc.Network == null) { problems.Add(where + " is empty"); continue; }
                if (string.IsNullOrEmpty(doc.Scope)) { problems.Add(where + " has no drawing scope"); continue; }
                foreach (KeyValuePair<string, Component> kv in doc.Network.Components)
                {
                    string guid = GuidOf(doc, kv.Key);
                    if (string.IsNullOrEmpty(guid))
                    {
                        problems.Add("component '" + kv.Key + "' of drawing '" + doc.Scope
                            + "' has no guid, so no link can reference it");
                        continue;
                    }
                    if (ownerDoc.ContainsKey(guid))
                    {
                        problems.Add("duplicate guid '" + guid + "': component '" + ownerCid[guid]
                            + "' of drawing '" + docs[ownerDoc[guid]].Scope + "' and component '"
                            + kv.Key + "' of drawing '" + doc.Scope + "'");
                        continue;
                    }
                    ownerDoc[guid] = i;
                    ownerCid[guid] = kv.Key;
                }
            }

            var fed = new List<int>();
            if (links != null)
            {
                foreach (DrawingLink link in links)
                {
                    if (link == null) { problems.Add("null drawing link"); continue; }
                    bool knownFrom = link.FromGuid != null && ownerDoc.ContainsKey(link.FromGuid);
                    bool knownTo = link.ToGuid != null && ownerDoc.ContainsKey(link.ToGuid);
                    if (!knownFrom)
                        problems.Add("link references unknown guid '" + (link.FromGuid ?? "null") + "'");
                    if (!knownTo)
                        problems.Add("link references unknown guid '" + (link.ToGuid ?? "null") + "'");
                    if (!knownFrom || !knownTo) continue;

                    int fromDoc = ownerDoc[link.FromGuid], toDoc = ownerDoc[link.ToGuid];
                    if (fromDoc == toDoc)
                    {
                        problems.Add("link '" + link.FromGuid + "' -> '" + link.ToGuid
                            + "' has both ends in drawing '" + docs[fromDoc].Scope
                            + "'; use Network.Connect for a link inside one drawing");
                        continue;
                    }
                    CheckPort(problems, docs[fromDoc], ownerCid[link.FromGuid], link.FromPort, false);
                    CheckPort(problems, docs[toDoc], ownerCid[link.ToGuid], link.ToPort, true);
                    if (!fed.Contains(toDoc)) fed.Add(toDoc);
                }
            }

            for (int i = 0; i < docs.Count; i++)
            {
                DrawingDocument doc = docs[i];
                if (doc == null || doc.Network == null || string.IsNullOrEmpty(doc.Scope)) continue;
                if (fed.Contains(i)) continue;
                if (doc.Network.Sources().Count > 0) continue;
                problems.Add("drawing '" + doc.Scope
                    + "' has no Source and no incoming link, so nothing feeds it");
            }
            return problems;
        }

        // ---- helpers -------------------------------------------------------

        private static void CheckPort(List<string> problems, DrawingDocument doc, string cid,
                                      string portName, bool wantIn)
        {
            string side = wantIn ? "arrives at" : "leaves";
            string kind = wantIn ? "an in-port" : "an out-port";
            Component c = doc.Network.Components[cid];
            if (string.IsNullOrEmpty(portName))
            {
                int n = 0;
                foreach (Port p in c.Ports) if (p.IsIn == wantIn) n++;
                if (n == 0)
                    problems.Add("link " + side + " component '" + QualifiedId(doc.Scope, cid)
                        + "', which has no " + kind);
                else if (n > 1)
                    problems.Add("link " + side + " component '" + QualifiedId(doc.Scope, cid)
                        + "', which has " + n.ToString(CultureInfo.InvariantCulture)
                        + " " + kind + "s; name one");
                return;
            }
            foreach (Port p in c.Ports)
            {
                if (p.Name != portName) continue;
                if (p.IsIn != wantIn)
                    problems.Add("link " + side + " port '" + QualifiedId(doc.Scope, cid) + "."
                        + portName + "', which is not " + kind);
                return;
            }
            problems.Add("link " + side + " port '" + QualifiedId(doc.Scope, cid) + "."
                + portName + "', which does not exist");
        }

        private static int RequireOwner(Dictionary<string, int> ownerDoc, string guid, string side)
        {
            int index;
            if (guid == null || !ownerDoc.TryGetValue(guid, out index))
                throw new WentaException("MultiDrawing.Merge: link '" + side + "' guid '"
                    + (guid ?? "null") + "' is not owned by any drawing document");
            return index;
        }

        private static string PortSuffix(string portName)
        {
            return string.IsNullOrEmpty(portName) ? "" : "." + portName;
        }

        private static string GuidOf(DrawingDocument doc, string cid)
        {
            ComponentMeta m;
            if (doc.Meta == null || !doc.Meta.TryGetValue(cid, out m) || m == null) return null;
            return m.Guid;
        }

        private static string ScopeOf(DrawingDocument doc, string cid)
        {
            ComponentMeta m;
            if (doc.Meta == null || !doc.Meta.TryGetValue(cid, out m) || m == null || string.IsNullOrEmpty(m.DrawingScope))
                return doc.Scope;
            return m.DrawingScope;
        }
    }
}
