using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Runtime;
using Wenta;

[assembly: CommandClass(typeof(WentaZwcad.LabelCommands))]

namespace WentaZwcad
{
    // #59: smart labels. WENTALABEL puts one WENTA_LABEL block reference per
    // drawn duct on WENTA-ANNO, its attributes (ID, BRANCH, SIZE, FLOW, VEL,
    // DPM) from Wenta.Annotation; the duct's tag keeps the label's handle
    // ("anno_h"). The style (fields shown, text height, decimals) is kept in
    // the drawing, so the editing commands refresh every label after an
    // edit with the same style, and a new style applies to all of them in
    // one WENTALABEL run.
    public class LabelCommands
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        internal const string BlockName = "WENTA_LABEL";
        private const string StyleKey = "WENTA_LABEL_STYLE";
        private const string HandleKey = "anno_h";
        private static readonly string[] Tags = { "ID", "BRANCH", "SIZE", "FLOW", "VEL", "DPM" };

        internal sealed class RefreshStats
        {
            public int Labels, Created, Updated, Stale, Removed;
        }

        // ---------------------------------------------------------- style storage

        private static LabelStyle StoredStyle(Transaction tr, Database db)
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(StyleKey)) return null;
            var xr = tr.GetObject(nod.GetAt(StyleKey), OpenMode.ForRead) as Xrecord;
            if (xr == null || xr.Data == null) return null;
            var sb = new StringBuilder();
            foreach (TypedValue tv in xr.Data.AsArray()) sb.Append(Convert.ToString(tv.Value, C));
            try { return LabelStyle.Parse(sb.ToString(), "drawing"); }
            catch (WentaException) { return null; }
        }

        private static void StoreStyle(Transaction tr, Database db, LabelStyle style)
        {
            string json = style.ToJson();
            var values = new List<TypedValue>();
            for (int i = 0; i < json.Length; i += 200)
                values.Add(new TypedValue(1, json.Substring(i, Math.Min(200, json.Length - i))));
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
            if (nod.Contains(StyleKey))
            {
                var old = (Xrecord)tr.GetObject(nod.GetAt(StyleKey), OpenMode.ForWrite);
                old.Data = new ResultBuffer(values.ToArray());
                return;
            }
            var xr = new Xrecord { Data = new ResultBuffer(values.ToArray()) };
            nod.SetAt(StyleKey, xr);
            tr.AddNewlyCreatedDBObject(xr, true);
        }

        // ---------------------------------------------------------- block

        private static ObjectId EnsureBlock(Transaction tr, Database db, LabelStyle style)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(BlockName)) return bt[BlockName];
            bt.UpgradeOpen();
            var def = new BlockTableRecord { Name = BlockName, Origin = Point3d.Origin };
            ObjectId id = bt.Add(def);
            tr.AddNewlyCreatedDBObject(def, true);
            for (int i = 0; i < Tags.Length; i++)
            {
                var ad = new AttributeDefinition(new Point3d(0.0, 0.0, 0.0), "", Tags[i], Tags[i], db.Textstyle)
                {
                    Height = style.TextHeightMm,
                    Layer = "0",
                };
                def.AppendEntity(ad);
                tr.AddNewlyCreatedDBObject(ad, true);
            }
            return id;
        }

        // Rough text width (mm) for laying the visible attributes out in a row.
        private static double Width(string s, double h) { return Math.Max(1, s.Length) * h * 0.75; }

        private static Point3d Anchor(Entity duct, LabelStyle style)
        {
            List<Wenta.Point2> pts = CadIo.Vertices(duct);
            if (pts.Count < 2) return Point3d.Origin;
            int best = 1;
            double bestLen = -1.0;
            for (int i = 1; i < pts.Count; i++)
            {
                double len = pts[i - 1].DistanceTo(pts[i]);
                if (len > bestLen) { bestLen = len; best = i; }
            }
            Wenta.Point2 a = pts[best - 1], b = pts[best];
            return new Point3d((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0 + style.OffsetMm, 0.0);
        }

        // Sets the attributes of a label; true when anything changed.
        private static bool Apply(Transaction tr, BlockReference br, SmartLabel label, LabelStyle style, Point3d at)
        {
            bool changed = br.Position.DistanceTo(at) > 1e-6;
            if (changed) br.Position = at;
            double x = at.X;
            foreach (ObjectId aid in br.AttributeCollection)
            {
                var ar = (AttributeReference)tr.GetObject(aid, OpenMode.ForWrite);
                int i = Array.IndexOf(Tags, ar.Tag);
                if (i < 0) continue;
                string field = LabelStyle.AllFields[i];
                string value;
                label.Values.TryGetValue(field, out value);
                value = value ?? "";
                bool show = style.Shows(field) && value.Length > 0;
                if (ar.TextString != value) { ar.TextString = value; changed = true; }
                if (ar.Invisible == show) { ar.Invisible = !show; changed = true; }
                if (Math.Abs(ar.Height - style.TextHeightMm) > 1e-9) { ar.Height = style.TextHeightMm; changed = true; }
                var p = new Point3d(x, at.Y, 0.0);
                if (ar.Position.DistanceTo(p) > 1e-6) { ar.Position = p; changed = true; }
                if (show) x += Width(value, style.TextHeightMm) + style.TextHeightMm;
            }
            return changed;
        }

        // ---------------------------------------------------------- refresh

        // Labels for the drawn network with `style` (the stored one when
        // null): updated in place, created where missing, and labels whose
        // duct is gone erased. Null when the drawing does not validate.
        internal static RefreshStats Refresh(Document doc, LabelStyle style, bool onlyIfPresent)
        {
            Database db = doc.Database;
            var ids = new Dictionary<string, ObjectId>();
            NetworkReadResult r = NetworkDrawing.Read(RoundTripCommands.Scan(db, ids), Path.GetFileNameWithoutExtension(doc.Name));
            if (!r.Ok) return null;
            r.Network.Solve();

            var stats = new RefreshStats();
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord ms = CadIo.ModelSpace(tr, db);
                var existing = new List<ObjectId>();
                foreach (ObjectId id in ms)
                {
                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br != null && !br.IsErased && br.Name == BlockName) existing.Add(id);
                }
                if (onlyIfPresent && existing.Count == 0) return null;

                if (style == null) style = StoredStyle(tr, db) ?? LabelStyle.Default();
                StoreStyle(tr, db, style);
                CadIo.EnsureLayer(tr, db, NetworkDrawing.LayerAnno, 3);
                ObjectId defId = EnsureBlock(tr, db, style);

                var kept = new HashSet<ObjectId>();
                foreach (SmartLabel label in Annotation.Build(r.Network, style))
                {
                    string handle;
                    ObjectId ductId;
                    if (!r.HandleOf.TryGetValue(label.ComponentId, out handle) || !CadIo.TryGetId(db, handle, out ductId)) continue;
                    var duct = (Entity)tr.GetObject(ductId, OpenMode.ForWrite);
                    WentaTag tag = CadIo.ReadTag(duct) ?? new WentaTag();
                    Point3d at = Anchor(duct, style);

                    BlockReference br = null;
                    string h = tag.Get(HandleKey);
                    ObjectId lid;
                    if (!string.IsNullOrEmpty(h) && CadIo.TryGetId(db, h, out lid) && !lid.IsErased)
                        br = tr.GetObject(lid, OpenMode.ForWrite) as BlockReference;
                    if (br != null && br.Name != BlockName) br = null;

                    if (br == null)
                    {
                        br = new BlockReference(at, defId) { Layer = NetworkDrawing.LayerAnno };
                        ms.AppendEntity(br);
                        tr.AddNewlyCreatedDBObject(br, true);
                        var def = (BlockTableRecord)tr.GetObject(defId, OpenMode.ForRead);
                        foreach (ObjectId eid in def)
                        {
                            var ad = tr.GetObject(eid, OpenMode.ForRead) as AttributeDefinition;
                            if (ad == null || ad.Constant) continue;
                            var ar = new AttributeReference();
                            ar.SetAttributeFromBlock(ad, br.BlockTransform);
                            br.AttributeCollection.AppendAttribute(ar);
                            tr.AddNewlyCreatedDBObject(ar, true);
                        }
                        Apply(tr, br, label, style, at);
                        tag.Set(HandleKey, br.Handle.ToString());
                        CadIo.WriteTag(tr, db, duct, tag);
                        stats.Created++;
                    }
                    else
                    {
                        if (Apply(tr, br, label, style, at)) stats.Stale++;
                        stats.Updated++;
                    }
                    kept.Add(br.ObjectId);
                    stats.Labels++;
                }
                foreach (ObjectId id in existing)
                    if (!kept.Contains(id))
                    {
                        tr.GetObject(id, OpenMode.ForWrite).Erase();
                        stats.Removed++;
                    }
                tr.Commit();
            }
            return stats;
        }

        // Called by the editing and round-trip commands after they redraw:
        // refreshes the labels when the drawing has any.
        internal static void RefreshIfPresent(Document doc, string cmd)
        {
            try
            {
                RefreshStats s = Refresh(doc, null, true);
                if (s == null) return;
                doc.Editor.WriteMessage(string.Format(C, "\n{0}: {1} label(s) refreshed ({2} changed, {3} new, {4} removed).",
                    cmd, s.Labels, s.Stale, s.Created, s.Removed));
                Commands.Log(string.Format(C, "{0} labels refreshed {1} changed {2} new {3} removed {4}",
                    cmd, s.Labels, s.Stale, s.Created, s.Removed));
            }
            catch (System.Exception ex)
            {
                Commands.Log(cmd + " label refresh FAILED " + ex.Message);
            }
        }

        [CommandMethod("WENTALABEL")]
        public void Label()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            try
            {
                if (EditCommands.ReadDrawn(doc, "WENTALABEL") == null) return;
                var po = new PromptStringOptions("\nLabel style JSON (Enter keeps the drawing's style): ") { AllowSpaces = true };
                PromptResult pr = ed.GetString(po);
                if (pr.Status != PromptStatus.OK) return;
                string path = (pr.StringResult ?? "").Trim().Trim('"');
                LabelStyle style = null;
                if (path.Length > 0)
                {
                    path = Environment.ExpandEnvironmentVariables(path);
                    if (!File.Exists(path) && !Path.IsPathRooted(path) && File.Exists(Path.Combine(RoundTripCommands.PluginDir(), path)))
                        path = Path.Combine(RoundTripCommands.PluginDir(), path);
                    style = LabelStyle.Load(path);
                }
                RefreshStats s = Refresh(doc, style, false);
                if (s == null) return;
                ed.WriteMessage(string.Format(C,
                    "\nWENTALABEL: {0} label(s) on {1}: {2} new, {3} updated ({4} changed), {5} removed{6}.",
                    s.Labels, NetworkDrawing.LayerAnno, s.Created, s.Updated, s.Stale, s.Removed,
                    path.Length > 0 ? ", style " + Path.GetFileName(path) : ""));
                Commands.Log(string.Format(C, "WENTALABEL ok labels {0} new {1} updated {2} stale {3} removed {4} style {5}",
                    s.Labels, s.Created, s.Updated, s.Stale, s.Removed, path.Length > 0 ? Path.GetFileName(path) : "drawing"));
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nWENTALABEL: " + ex.Message);
                Commands.Log("WENTALABEL FAILED " + ex.Message);
            }
        }
    }
}
