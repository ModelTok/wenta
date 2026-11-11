using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace Wenta
{
    // One attenuator (silencer) of an attenuator catalog: a round in-line
    // unit with its connection diameter, length, single-number insertion
    // loss and pressure-loss coefficient (referred to the velocity in the
    // connection diameter).
    public sealed class Attenuator
    {
        public string Id;
        public double DiameterM;
        public double LengthM;
        public double InsertionLossDb;
        public double Zeta;
        public string Source;
        public string Notes;
    }

    // Open, vendor-neutral attenuator catalog (docs/user-guide.en.md §5.5):
    //   { "name": ..., "version": 1, "attenuators": [ { "id": ...,
    //     "diameter_mm": 200, "length_mm": 600, "insertion_loss_db": 8,
    //     "zeta": 0.5 }, ... ] }
    public sealed class AttenuatorCatalog
    {
        public const int SchemaVersion = 1;

        public string Name;
        public string Description;
        public int Version = SchemaVersion;
        public readonly List<Attenuator> Attenuators = new List<Attenuator>();

        public static AttenuatorCatalog Load(string path)
        {
            return Parse(File.ReadAllText(path), path);
        }

        public static AttenuatorCatalog Parse(string json, string origin)
        {
            Dictionary<string, object> root;
            try
            {
                root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            }
            catch (Exception ex)
            {
                throw new WentaException("invalid attenuator catalog JSON: " + origin + " (" + ex.Message + ")");
            }
            if (root == null)
                throw new WentaException("invalid attenuator catalog JSON: " + origin);
            var cat = new AttenuatorCatalog();
            cat.Name = Str(root, "name");
            cat.Description = Str(root, "description");
            if (root.ContainsKey("version"))
            {
                object v = root["version"];
                if (!(v is int) || (int)v < 1)
                    throw new WentaException("attenuator catalog " + origin + ": 'version' must be an integer >= 1");
                cat.Version = (int)v;
            }
            if (cat.Version > SchemaVersion)
                throw new WentaException("attenuator catalog '" + (cat.Name ?? "?") + "' (" + origin
                    + ") declares version " + cat.Version.ToString(CultureInfo.InvariantCulture)
                    + "; this build supports attenuator catalog version "
                    + SchemaVersion.ToString(CultureInfo.InvariantCulture) + " or older");
            ArrayList items = root.ContainsKey("attenuators") ? root["attenuators"] as ArrayList : null;
            if (items == null)
                throw new WentaException("attenuator catalog JSON has no 'attenuators' array: " + origin);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                string where = "attenuator catalog " + origin + ": attenuators[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                var d = items[i] as Dictionary<string, object>;
                if (d == null) throw new WentaException(where + " is not an object");
                var a = new Attenuator();
                a.Id = Str(d, "id");
                if (string.IsNullOrEmpty(a.Id)) throw new WentaException(where + " has no 'id'");
                if (!seen.Add(a.Id))
                    throw new WentaException("attenuator catalog " + origin + ": duplicate attenuator id '" + a.Id + "'");
                where = "attenuator catalog " + origin + ": '" + a.Id + "'";
                a.DiameterM = Positive(d, "diameter_mm", where) / 1000.0;
                a.LengthM = Positive(d, "length_mm", where) / 1000.0;
                a.InsertionLossDb = Positive(d, "insertion_loss_db", where);
                a.Zeta = NonNegative(d, "zeta", where);
                a.Source = Str(d, "source");
                a.Notes = Str(d, "notes");
                cat.Attenuators.Add(a);
            }
            return cat;
        }

        public Attenuator Find(string id)
        {
            if (id == null) return null;
            foreach (Attenuator a in Attenuators) if (a.Id == id) return a;
            return null;
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            return d.ContainsKey(key) ? d[key] as string : null;
        }

        private static double Number(Dictionary<string, object> d, string key, string where)
        {
            object o = d.ContainsKey(key) ? d[key] : null;
            if (!(o is int || o is long || o is decimal || o is double))
                throw new WentaException(where + ": '" + key + "' is missing or not a number");
            return Convert.ToDouble(o, CultureInfo.InvariantCulture);
        }

        private static double Positive(Dictionary<string, object> d, string key, string where)
        {
            double v = Number(d, key, where);
            if (!(v > 0.0)) throw new WentaException(where + ": '" + key + "' must be positive");
            return v;
        }

        private static double NonNegative(Dictionary<string, object> d, string key, string where)
        {
            double v = Number(d, key, where);
            if (v < 0.0) throw new WentaException(where + ": '" + key + "' must not be negative");
            return v;
        }
    }

    // Room sound of one terminal and the attenuator chosen for it.
    public sealed class RoomAttenuation
    {
        public string TerminalId;
        public string DuctId;              // the rigid run feeding the terminal
        public double VelocityMs;
        public double SoundPowerDb;        // regenerated noise of that run
        public double PressureLevelDb;     // in the room, without any attenuator
        public double NcLimit;
        public double RequiredDb;          // insertion loss needed (0 when it already meets)
        public string ExistingId;          // attenuator already between run and terminal
        public double ExistingLossDb;
        public Attenuator Picked;          // newly selected, or null
        public string InsertedId;          // component id of the inserted attenuator
        public double LevelAfterDb;        // with every attenuator on the path
        public bool Ok;
        public string Problem;
    }

    public sealed class AttenuatorResult
    {
        public Network Network;
        public Dictionary<string, ComponentMeta> Meta;
        public readonly List<RoomAttenuation> Rooms = new List<RoomAttenuation>();
        public int Inserted;
        public double CriticalPa;
    }

    // WENTAATTEN (#48): the room sound of DrawingSound.Rooms evaluated on a
    // network (regenerated noise of the rigid run feeding each terminal,
    // room equation, NC limit of the space type), then, for every room over
    // its limit, the catalog attenuator with the connection diameter of that
    // run whose insertion loss covers the excess, the lowest zeta first.
    // The attenuator is spliced in between the run and the terminal as a
    // TwoPortFitting (role "attenuator", catalog entry and provenance in the
    // component metadata), so the solve includes its pressure loss. The
    // acoustic model is single-number, like WENTASOUND: the attenuator's own
    // regenerated noise and duct attenuation are not modelled.
    public static class AttenuatorSelection
    {
        public const string Role = "attenuator";
        public const double DiameterToleranceM = 0.0005;
        public const double OffsetMm = 500.0;

        // Lowest zeta among the attenuators of this diameter whose insertion
        // loss is at least the requirement; ties go to the shorter unit,
        // then to catalog order. Null when none qualifies.
        public static Attenuator Pick(AttenuatorCatalog catalog, double diameterM, double requiredDb)
        {
            Attenuator best = null;
            foreach (Attenuator a in catalog.Attenuators)
            {
                if (Math.Abs(a.DiameterM - diameterM) > DiameterToleranceM) continue;
                if (a.InsertionLossDb + 1e-9 < requiredDb) continue;
                if (best == null || a.Zeta < best.Zeta - 1e-12
                    || (Math.Abs(a.Zeta - best.Zeta) <= 1e-12 && a.LengthM < best.LengthM - 1e-12))
                    best = a;
            }
            return best;
        }

        public static AttenuatorResult Apply(Network net, IDictionary<string, ComponentMeta> meta,
            AttenuatorCatalog catalog, string spaceType, double roomSurfaceAreaM2, double absorption,
            Fluid fluid = null)
        {
            if (net == null) throw new WentaException("AttenuatorSelection: network is null");
            if (catalog == null) throw new WentaException("AttenuatorSelection: catalog is null");
            fluid = fluid ?? Fluid.StandardAir();
            double limit;
            if (!Sound.NoiseLimitsNc.TryGetValue(spaceType ?? "", out limit))
                throw new WentaException("unknown space type '" + spaceType + "'");
            var metaCopy = new Dictionary<string, ComponentMeta>(StringComparer.Ordinal);
            if (meta != null) foreach (KeyValuePair<string, ComponentMeta> kv in meta) metaCopy[kv.Key] = kv.Value;

            net.Solve(fluid);
            var result = new AttenuatorResult { Meta = metaCopy };
            var before = new Dictionary<string, Attenuator>(StringComparer.Ordinal); // terminal -> new attenuator
            var terminals = new List<string>();
            foreach (KeyValuePair<string, Component> kv in net.Components)
                if (kv.Value is Terminal) terminals.Add(kv.Key);

            foreach (string tid in terminals)
            {
                var room = new RoomAttenuation { TerminalId = tid, NcLimit = limit };
                double existingLoss;
                string existingId;
                string duct = FeedingDuct(net, tid, metaCopy, catalog, out existingLoss, out existingId);
                room.ExistingId = existingId;
                room.ExistingLossDb = existingLoss;
                if (duct == null)
                {
                    room.Problem = "not fed by a rigid run through in-line fittings only; skipped";
                    result.Rooms.Add(room);
                    continue;
                }
                var rd = (RigidDuct)net.Components[duct];
                room.DuctId = duct;
                room.VelocityMs = (rd.InletFlowrate() ?? 0.0) / rd.CrossSection.Area;
                if (!(room.VelocityMs > 0.0))
                {
                    room.Problem = "run '" + duct + "' carries no flow; skipped";
                    result.Rooms.Add(room);
                    continue;
                }
                room.SoundPowerDb = Sound.RegeneratedNoiseRound(room.VelocityMs, rd.CrossSection.HydraulicDiameter, fluid.Density);
                room.PressureLevelDb = Sound.DuctPressureLevel(room.SoundPowerDb, roomSurfaceAreaM2, absorption);
                double withExisting = room.PressureLevelDb - existingLoss;
                room.RequiredDb = Math.Max(0.0, room.PressureLevelDb - limit);
                room.LevelAfterDb = withExisting;
                if (Sound.NcOkTarget(limit, withExisting))
                {
                    room.Ok = true;
                }
                else if (existingId != null)
                {
                    room.Problem = "attenuator '" + existingId + "' is already fitted but gives only "
                        + existingLoss.ToString("0.0", CultureInfo.InvariantCulture) + " dB of the "
                        + room.RequiredDb.ToString("0.0", CultureInfo.InvariantCulture) + " dB needed";
                }
                else
                {
                    Round r = rd.CrossSection as Round;
                    Attenuator a = r != null ? Pick(catalog, r.Diameter, room.RequiredDb) : null;
                    if (a == null)
                        room.Problem = r == null
                            ? "run '" + duct + "' is rectangular; the catalog lists round attenuators only"
                            : "no attenuator of Ø" + (r.Diameter * 1000.0).ToString("0", CultureInfo.InvariantCulture)
                              + " gives " + room.RequiredDb.ToString("0.0", CultureInfo.InvariantCulture) + " dB";
                    else
                    {
                        room.Picked = a;
                        room.LevelAfterDb = room.PressureLevelDb - a.InsertionLossDb;
                        room.Ok = true;
                        before[tid] = a;
                    }
                }
                result.Rooms.Add(room);
            }

            result.Network = before.Count == 0 ? net : Splice(net, metaCopy, before, catalog, result.Rooms);
            result.Inserted = before.Count;
            result.CriticalPa = result.Network.Solve(fluid);
            result.Rooms.Sort(delegate (RoomAttenuation a, RoomAttenuation b)
            {
                int c = (b.PressureLevelDb - b.NcLimit).CompareTo(a.PressureLevelDb - a.NcLimit);
                return c != 0 ? c : string.CompareOrdinal(a.TerminalId, b.TerminalId);
            });
            return result;
        }

        // The rigid run whose outlet reaches the terminal through two-port
        // fittings only; the insertion loss of catalog attenuators on that
        // path is summed. Null when a tee, flex duct or source comes first.
        public static string FeedingDuct(Network net, string terminalId, IDictionary<string, ComponentMeta> meta,
            AttenuatorCatalog catalog, out double attenuatorLossDb, out string attenuatorId)
        {
            attenuatorLossDb = 0.0;
            attenuatorId = null;
            string node = Network.PortNodeId(terminalId, "inlet");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (true)
            {
                string pred = null;
                foreach (string p in net.Predecessors(node))
                    if (p.IndexOf(':') > 0) { pred = p; break; }
                if (pred == null) return null;
                string cid = pred.Substring(0, pred.IndexOf(':'));
                if (!seen.Add(cid)) return null;
                Component c = net.Components[cid];
                if (c is RigidDuct) return cid;
                if (!(c is TwoPortFitting)) return null;
                ComponentMeta m;
                if (meta != null && meta.TryGetValue(cid, out m) && m != null && m.Role == Role)
                {
                    Attenuator a = catalog.Find(m.CatalogEntry);
                    if (a != null) attenuatorLossDb += a.InsertionLossDb;
                    if (attenuatorId == null) attenuatorId = cid;
                }
                node = Network.PortNodeId(cid, "inlet");
            }
        }

        // A new network with an attenuator between each chosen terminal and
        // the component feeding it, components in the original order (each
        // attenuator just before its terminal).
        private static Network Splice(Network net, Dictionary<string, ComponentMeta> meta,
            Dictionary<string, Attenuator> chosen, AttenuatorCatalog catalog, List<RoomAttenuation> rooms)
        {
            var links = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, Component> kv in net.Components)
                foreach (Port p in kv.Value.Ports)
                {
                    if (!p.IsIn) continue;
                    string target = Network.PortNodeId(kv.Key, p.Name);
                    foreach (string pred in net.Predecessors(target))
                        if (pred.IndexOf(':') > 0) links.Add(new KeyValuePair<string, string>(pred, target));
                }

            var outNet = new Network { Name = net.Name };
            var newId = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Component> kv in net.Components)
            {
                Attenuator a;
                if (chosen.TryGetValue(kv.Key, out a))
                {
                    string id = "atten_" + kv.Key;
                    for (int n = 2; net.Components.ContainsKey(id) || outNet.Components.ContainsKey(id); n++)
                        id = "atten_" + kv.Key + "_" + n.ToString(CultureInfo.InvariantCulture);
                    outNet.Add(id, new TwoPortFitting(a.Id, new Round(a.DiameterM), a.Zeta));
                    ComponentMeta tm;
                    meta.TryGetValue(kv.Key, out tm);
                    meta[id] = new ComponentMeta
                    {
                        Guid = System.Guid.NewGuid().ToString("D"),
                        DrawingScope = tm != null ? tm.DrawingScope : null,
                        CatalogEntry = a.Id,
                        Provenance = (catalog.Name ?? "attenuator catalog") + (a.Source != null ? " (" + a.Source + ")" : ""),
                        Role = Role,
                    };
                    newId[kv.Key] = id;
                }
                outNet.Add(kv.Key, kv.Value);
            }

            foreach (KeyValuePair<string, string> link in links)
            {
                string targetCid = link.Value.Substring(0, link.Value.IndexOf(':'));
                string att;
                if (newId.TryGetValue(targetCid, out att))
                {
                    outNet.Connect(Ref(link.Key), att + ".inlet");
                    outNet.Connect(att + ".outlet", Ref(link.Value));
                    meta[att].AtMm = PlaceNear(meta, link.Key.Substring(0, link.Key.IndexOf(':')), targetCid);
                }
                else outNet.Connect(Ref(link.Key), Ref(link.Value));
            }
            foreach (RoomAttenuation r in rooms)
            {
                string id;
                if (r.Picked != null && newId.TryGetValue(r.TerminalId, out id)) r.InsertedId = id;
            }
            return outNet;
        }

        // "cid:port" node id -> the "cid.port" reference Network.Connect takes.
        private static string Ref(string nodeId)
        {
            int colon = nodeId.LastIndexOf(':');
            return nodeId.Substring(0, colon) + "." + nodeId.Substring(colon + 1);
        }

        // On the feeding run's last segment, OffsetMm (or half the segment)
        // back from its end; else at the terminal; null without a layout.
        private static Point2? PlaceNear(Dictionary<string, ComponentMeta> meta, string upstreamId, string terminalId)
        {
            ComponentMeta up, tm;
            if (meta.TryGetValue(upstreamId, out up) && up != null && up.LayoutMm != null && up.LayoutMm.Count >= 2)
            {
                Point2 end = up.LayoutMm[up.LayoutMm.Count - 1];
                Point2 prev = up.LayoutMm[up.LayoutMm.Count - 2];
                double dx = prev.X - end.X, dy = prev.Y - end.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len > 0.0)
                {
                    double off = Math.Min(OffsetMm, len / 2.0);
                    return new Point2(end.X + dx / len * off, end.Y + dy / len * off);
                }
                return end;
            }
            if (up != null && up.AtMm.HasValue) return up.AtMm;
            if (meta.TryGetValue(terminalId, out tm) && tm != null && tm.AtMm.HasValue) return tm.AtMm;
            return null;
        }
    }
}
