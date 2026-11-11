using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace Wenta
{
    // Editing a drawn network (#23) and re-fitting it after an edit (#27):
    // the host-agnostic half of WENTAPROPS, WENTAMOVE, WENTASTRETCH and
    // WENTAREFIT. Every edit returns a new network (components are
    // immutable) plus the updated layout in the metadata, so the plugin
    // redraws the records in place (same GUIDs) and a later WENTAREAD sees
    // no length edits. Sizes go through StandardSizes, flows and pressure
    // drops through the solver, balancing through Balancing; nothing here
    // computes engineering values of its own.

    public sealed class EditResult
    {
        public Network Network;
        public Dictionary<string, ComponentMeta> Meta;
        // Components whose record or layout changed.
        public readonly List<string> Changed = new List<string>();
        public double CriticalBeforePa;
        public double CriticalAfterPa;
        public string Note;
    }

    public sealed class DamperSettingResult
    {
        public string DamperId;
        public string TerminalId;
        public double Zeta;
        public double OpenPercent;
    }

    public sealed class ReFlowResult
    {
        public Network Network;
        public Dictionary<string, ComponentMeta> Meta;
        public double OldFlowM3s;
        public double NewFlowM3s;
        public List<ReFitChange> Resized = new List<ReFitChange>();
        public List<BalancingHint> Hints = new List<BalancingHint>();
        public List<DamperSettingResult> Dampers = new List<DamperSettingResult>();
        public double CriticalBeforePa;
        public double CriticalAfterPa;
        // Largest remaining surplus over the terminals that have a damper
        // to absorb it (0 when they are all balanced).
        public double ResidualSurplusPa;
        public double ElapsedMs;
    }

    public static class NetworkEdit
    {
        public const string RoleDamper = "damper";

        public static readonly string[] Properties = { "diameter", "width", "height", "length", "flow", "zeta" };

        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        // ------------------------------------------------------------ copies

        public static Component Copy(Component c)
        {
            var rd = c as RigidDuct;
            if (rd != null) return new RigidDuct(rd.Name, rd.CrossSection, rd.Length, rd.AbsoluteRoughness);
            var fd = c as FlexDuct;
            if (fd != null) return new FlexDuct(fd.Name, fd.Diameter, fd.Length, fd.PressureDropPerMeter, fd.StretchPercentage);
            var s = c as Source;
            if (s != null) return new Source(s.Name);
            var t = c as Terminal;
            if (t != null) return new Terminal(t.Name, t.Flowrate, t.CrossSection, t.Zeta);
            var f = c as TwoPortFitting;
            if (f != null) return new TwoPortFitting(f.Name, f.CrossSection, f.Zeta);
            var tee = c as Tee;
            if (tee != null) return new Tee(tee.Name, tee.CrossSection, tee.ZetaStraight, tee.ZetaBranch);
            throw new WentaException("NetworkEdit: unsupported component type " + c.GetType().Name);
        }

        // A copy of the network with fresh components (the replacements
        // where given) and the same connections, in the same order.
        public static Network Rebuild(Network net, IDictionary<string, Component> replace)
        {
            var outNet = new Network { Name = net.Name };
            foreach (KeyValuePair<string, Component> kv in net.Components)
            {
                Component r;
                outNet.Add(kv.Key, replace != null && replace.TryGetValue(kv.Key, out r) ? r : Copy(kv.Value));
            }
            foreach (KeyValuePair<string, Component> kv in net.Components)
                foreach (Port p in kv.Value.Ports)
                {
                    if (!p.IsIn) continue;
                    foreach (string pred in net.Predecessors(Network.PortNodeId(kv.Key, p.Name)))
                    {
                        int colon = pred.LastIndexOf(':');
                        if (colon <= 0) continue;
                        outNet.Connect(pred.Substring(0, colon) + "." + pred.Substring(colon + 1), kv.Key + "." + p.Name);
                    }
                }
            return outNet;
        }

        public static Dictionary<string, ComponentMeta> CopyMeta(IDictionary<string, ComponentMeta> meta)
        {
            var copy = new Dictionary<string, ComponentMeta>(StringComparer.Ordinal);
            if (meta == null) return copy;
            foreach (KeyValuePair<string, ComponentMeta> kv in meta)
            {
                ComponentMeta m = kv.Value;
                if (m == null) continue;
                copy[kv.Key] = new ComponentMeta
                {
                    Guid = m.Guid,
                    DrawingScope = m.DrawingScope,
                    CatalogEntry = m.CatalogEntry,
                    Provenance = m.Provenance,
                    Role = m.Role,
                    LayoutMm = m.LayoutMm == null ? null : new List<Point2>(m.LayoutMm),
                    AtMm = m.AtMm,
                };
            }
            return copy;
        }

        // ------------------------------------------------------- one value

        public static Component WithSection(Component c, CrossSection cs)
        {
            var rd = c as RigidDuct;
            if (rd != null) return new RigidDuct(rd.Name, cs, rd.Length, rd.AbsoluteRoughness);
            var fd = c as FlexDuct;
            if (fd != null)
            {
                var r = cs as Round;
                if (r == null) throw new WentaException("a flexible duct is round; it takes a diameter only");
                return new FlexDuct(fd.Name, r.Diameter, fd.Length, fd.PressureDropPerMeter, fd.StretchPercentage);
            }
            var t = c as Terminal;
            if (t != null) return new Terminal(t.Name, t.Flowrate, cs, t.Zeta);
            var f = c as TwoPortFitting;
            if (f != null) return new TwoPortFitting(f.Name, cs, f.Zeta);
            var tee = c as Tee;
            if (tee != null) return new Tee(tee.Name, cs, tee.ZetaStraight, tee.ZetaBranch);
            throw new WentaException("'" + c.Name + "' (" + c.GetType().Name + ") has no cross-section");
        }

        public static CrossSection SectionOf(Component c)
        {
            if (c is RigidDuct) return ((RigidDuct)c).CrossSection;
            if (c is FlexDuct) return new Round(((FlexDuct)c).Diameter);
            if (c is Terminal) return ((Terminal)c).CrossSection;
            if (c is TwoPortFitting) return ((TwoPortFitting)c).CrossSection;
            if (c is Tee) return ((Tee)c).CrossSection;
            return null;
        }

        public static Component WithLength(Component c, double lengthM)
        {
            var rd = c as RigidDuct;
            if (rd != null) return new RigidDuct(rd.Name, rd.CrossSection, lengthM, rd.AbsoluteRoughness);
            var fd = c as FlexDuct;
            if (fd != null) return new FlexDuct(fd.Name, fd.Diameter, lengthM, fd.PressureDropPerMeter, fd.StretchPercentage);
            throw new WentaException("'" + c.Name + "' is not a duct; only ducts have a length");
        }

        public static Component WithFlow(Component c, double flowM3s)
        {
            var t = c as Terminal;
            if (t == null) throw new WentaException("'" + c.Name + "' is not a terminal; flows are set on terminals and propagate upstream");
            return new Terminal(t.Name, flowM3s, t.CrossSection, t.Zeta);
        }

        public static Component WithZeta(Component c, double zeta)
        {
            if (zeta < 0.0) throw new WentaException("zeta must not be negative, got " + zeta.ToString("R", C));
            var t = c as Terminal;
            if (t != null) return new Terminal(t.Name, t.Flowrate, t.CrossSection, zeta);
            var f = c as TwoPortFitting;
            if (f != null) return new TwoPortFitting(f.Name, f.CrossSection, zeta);
            throw new WentaException("'" + c.Name + "' has no single ζ to edit (fittings, dampers and terminals do)");
        }

        // ------------------------------------------------------ properties

        // The value WENTAPROPS shows for a property, or null when the
        // component does not have it. Diameters and widths in mm.
        public static double? Get(Component c, string property)
        {
            CrossSection cs = SectionOf(c);
            switch (property)
            {
                case "diameter": return cs is Round ? ((Round)cs).Diameter * 1000.0 : (double?)null;
                case "width": return cs is Rectangular ? ((Rectangular)cs).Width * 1000.0 : (double?)null;
                case "height": return cs is Rectangular ? ((Rectangular)cs).Height * 1000.0 : (double?)null;
                case "length":
                    if (c is RigidDuct) return ((RigidDuct)c).Length;
                    if (c is FlexDuct) return ((FlexDuct)c).Length;
                    return null;
                case "flow": return c is Terminal ? ((Terminal)c).Flowrate : (double?)null;
                case "zeta":
                    if (c is Terminal) return ((Terminal)c).Zeta;
                    if (c is TwoPortFitting) return ((TwoPortFitting)c).Zeta;
                    return null;
            }
            throw new WentaException("unknown property '" + property + "' (" + string.Join(", ", Properties) + ")");
        }

        // Sets one property and re-solves. A diameter is snapped up to the
        // EN 1506 round series (StandardSizes.NearestRoundSize); widths and
        // heights are taken as given (mm). A length stretches the duct
        // (Stretch). Flows are set on terminals and propagate upstream.
        public static EditResult Set(Network net, IDictionary<string, ComponentMeta> meta, string cid,
            string property, double value)
        {
            Component c = Find(net, cid);
            if (Get(c, property) == null)
                throw new WentaException("'" + cid + "' (" + NetworkJson.WentaClassOf(c) + ") has no " + property);
            if (property == "length") return Stretch(net, meta, cid, value);
            if (!(value >= 0.0) || ((property == "diameter" || property == "width" || property == "height") && value <= 0.0))
                throw new WentaException(property + " must be positive, got " + value.ToString("R", C));

            Component repl;
            string note = null;
            switch (property)
            {
                case "diameter":
                    int d = StandardSizes.NearestRoundSize(value);
                    if (Math.Abs(d - value) > 1e-9)
                        note = "Ø" + value.ToString("0.#", C) + " snapped to the EN 1506 size Ø" + d.ToString(C);
                    repl = WithSection(c, new Round(d / 1000.0));
                    break;
                case "width":
                case "height":
                    var q = (Rectangular)SectionOf(c);
                    repl = WithSection(c, property == "width"
                        ? new Rectangular(value / 1000.0, q.Height) : new Rectangular(q.Width, value / 1000.0));
                    break;
                case "flow": repl = WithFlow(c, value); break;
                default: repl = WithZeta(c, value); break;
            }
            var res = new EditResult { CriticalBeforePa = net.Solve(), Meta = CopyMeta(meta), Note = note };
            res.Network = Rebuild(net, new Dictionary<string, Component> { { cid, repl } });
            res.Changed.Add(cid);
            res.CriticalAfterPa = res.Network.Solve();
            return res;
        }

        // ---------------------------------------------------------- geometry

        // Every component downstream of `cid` (through its out ports), not
        // including `cid` itself.
        public static List<string> Downstream(Network net, string cid)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { cid };
            var order = new List<string>();
            var stack = new Stack<string>();
            stack.Push(cid);
            while (stack.Count > 0)
            {
                string at = stack.Pop();
                foreach (Port p in net.Components[at].Ports)
                {
                    if (p.IsIn) continue;
                    foreach (string succ in net.Successors(Network.PortNodeId(at, p.Name)))
                    {
                        int colon = succ.IndexOf(':');
                        if (colon <= 0) continue;
                        string next = succ.Substring(0, colon);
                        if (seen.Add(next)) { order.Add(next); stack.Push(next); }
                    }
                }
            }
            return order;
        }

        // Changes a duct's length: its last vertex moves along the last
        // segment and everything downstream moves with it by the same
        // vector, so the drawing stays connected and every drawn length
        // equals its record.
        public static EditResult Stretch(Network net, IDictionary<string, ComponentMeta> meta, string cid, double lengthM)
        {
            Component c = Find(net, cid);
            if (!NetworkDrawing.IsDuct(c)) throw new WentaException("'" + cid + "' is not a duct; only ducts can be stretched");
            if (!(lengthM > 0.0)) throw new WentaException("length must be positive, got " + lengthM.ToString("R", C));
            var res = new EditResult { CriticalBeforePa = net.Solve(), Meta = CopyMeta(meta) };
            ComponentMeta m = Layout(res.Meta, cid, true);
            List<Point2> pts = m.LayoutMm;
            double drawn = NetworkDrawing.PolylineLengthMm(pts);
            Point2 a = pts[pts.Count - 2], b = pts[pts.Count - 1];
            double seg = a.DistanceTo(b);
            if (seg <= 0.0) throw new WentaException("'" + cid + "' ends in a zero-length segment; it cannot be stretched");
            double delta = lengthM * 1000.0 - drawn;
            if (seg + delta <= NetworkDrawing.LengthToleranceMm)
                throw new WentaException(string.Format(C,
                    "'{0}' cannot be shortened to {1} m by moving its last vertex (the last segment is {2} mm); move a vertex with WENTAMOVE or redraw it",
                    cid, lengthM, seg.ToString("0", C)));
            double ux = (b.X - a.X) / seg, uy = (b.Y - a.Y) / seg;
            double dx = ux * delta, dy = uy * delta;
            pts[pts.Count - 1] = new Point2(b.X + dx, b.Y + dy);
            res.Changed.Add(cid);
            foreach (string down in Downstream(net, cid))
            {
                Translate(res.Meta, down, dx, dy);
                res.Changed.Add(down);
            }
            res.Network = Rebuild(net, new Dictionary<string, Component> { { cid, WithLength(c, lengthM) } });
            res.CriticalAfterPa = res.Network.Solve();
            return res;
        }

        // Moves a point component (tee, fitting, damper, terminal, source)
        // by (dx, dy) mm; the ends of the ducts joined to it follow and
        // those ducts take their new drawn length. Ducts themselves are
        // reshaped with WENTASTRETCH or by moving the components at their
        // ends.
        public static EditResult Move(Network net, IDictionary<string, ComponentMeta> meta, string cid, double dxMm, double dyMm)
        {
            Component c = Find(net, cid);
            if (NetworkDrawing.IsDuct(c))
                throw new WentaException("'" + cid + "' is a duct; move the components at its ends, or stretch it with WENTASTRETCH");
            var res = new EditResult { CriticalBeforePa = net.Solve(), Meta = CopyMeta(meta) };
            ComponentMeta m = Layout(res.Meta, cid, false);
            m.AtMm = new Point2(m.AtMm.Value.X + dxMm, m.AtMm.Value.Y + dyMm);
            res.Changed.Add(cid);

            var repl = new Dictionary<string, Component>();
            foreach (Port p in c.Ports)
            {
                List<string> neighbours = p.IsIn
                    ? net.Predecessors(Network.PortNodeId(cid, p.Name))
                    : net.Successors(Network.PortNodeId(cid, p.Name));
                foreach (string node in neighbours)
                {
                    int colon = node.IndexOf(':');
                    if (colon <= 0) continue;
                    string other = node.Substring(0, colon);
                    Component oc = net.Components[other];
                    if (!NetworkDrawing.IsDuct(oc)) continue;
                    ComponentMeta om = Layout(res.Meta, other, true);
                    int i = p.IsIn ? om.LayoutMm.Count - 1 : 0;   // the duct's outlet feeds our inlet
                    om.LayoutMm[i] = new Point2(om.LayoutMm[i].X + dxMm, om.LayoutMm[i].Y + dyMm);
                    double len = NetworkDrawing.PolylineLengthMm(om.LayoutMm);
                    if (len <= NetworkDrawing.LengthToleranceMm)
                        throw new WentaException("moving '" + cid + "' would shrink duct '" + other + "' to nothing");
                    repl[other] = WithLength(oc, len / 1000.0);
                    if (!res.Changed.Contains(other)) res.Changed.Add(other);
                }
            }
            res.Network = Rebuild(net, repl);
            res.CriticalAfterPa = res.Network.Solve();
            return res;
        }

        private static void Translate(Dictionary<string, ComponentMeta> meta, string cid, double dx, double dy)
        {
            ComponentMeta m;
            if (!meta.TryGetValue(cid, out m) || m == null) return;
            if (m.AtMm.HasValue) m.AtMm = new Point2(m.AtMm.Value.X + dx, m.AtMm.Value.Y + dy);
            if (m.LayoutMm != null)
                for (int i = 0; i < m.LayoutMm.Count; i++)
                    m.LayoutMm[i] = new Point2(m.LayoutMm[i].X + dx, m.LayoutMm[i].Y + dy);
        }

        private static ComponentMeta Layout(Dictionary<string, ComponentMeta> meta, string cid, bool duct)
        {
            ComponentMeta m;
            if (!meta.TryGetValue(cid, out m) || m == null
                || (duct ? m.LayoutMm == null || m.LayoutMm.Count < 2 : !m.AtMm.HasValue))
                throw new WentaException("'" + cid + "' has no drawn layout; draw the network with WENTAWRITE first");
            return m;
        }

        private static Component Find(Network net, string cid)
        {
            if (net == null) throw new WentaException("NetworkEdit: network is null");
            Component c;
            if (cid == null || !net.Components.TryGetValue(cid, out c))
                throw new WentaException("no component '" + cid + "' in the network");
            return c;
        }

        // -------------------------------------------------------- re-fit

        // #27: one terminal's flow changes; the ducts feeding it are re-sized for their
        // new flow (ReFit.Apply, the WENTASIZE methods), the network is
        // re-solved, and each damper (a two-port fitting with role
        // "damper") on the run feeding a terminal is set to the ζ that
        // brings that terminal's path up to the critical one
        // (Balancing.BalancingZeta at the damper's own velocity). Dampers
        // start fully open, so the critical path is the network's own.
        public static ReFlowResult ReFlow(Network net, IDictionary<string, ComponentMeta> meta, string terminalId,
            double flowM3s, ReFitOptions options)
        {
            var sw = Stopwatch.StartNew();
            Component c = Find(net, terminalId);
            var term = c as Terminal;
            if (term == null) throw new WentaException("'" + terminalId + "' is not a terminal");
            if (!(flowM3s > 0.0)) throw new WentaException("flow must be positive, got " + flowM3s.ToString("R", C));
            options = options ?? new ReFitOptions();
            Fluid fluid = options.Fluid ?? Fluid.StandardAir();
            Dictionary<string, ComponentMeta> m = CopyMeta(meta);

            var res = new ReFlowResult { Meta = m, OldFlowM3s = term.Flowrate, NewFlowM3s = flowM3s };
            res.CriticalBeforePa = net.Solve(fluid);

            // New flow, dampers open.
            var repl = new Dictionary<string, Component> { { terminalId, WithFlow(c, flowM3s) } };
            foreach (KeyValuePair<string, Component> kv in net.Components)
                if (IsDamper(kv.Key, kv.Value, m) && kv.Key != terminalId)
                    repl[kv.Key] = WithZeta(kv.Value, 0.0);
            Network edited = Rebuild(net, repl);

            // Only the affected legs, the ducts between the source and this
            // terminal, are re-sized; the other runs carry the same flow as
            // before and keep the size they were given.
            ReFitResult fit = ReFit.Apply(edited, options);
            HashSet<string> affected = Upstream(edited, terminalId);
            var resize = new Dictionary<string, Component>();
            foreach (ReFitChange ch in fit.Changes)
            {
                if (!ch.Changed || !affected.Contains(ch.ComponentId)) continue;
                res.Resized.Add(ch);
                resize[ch.ComponentId] = fit.Network.Components[ch.ComponentId];
            }
            Network sized = FollowSizes(edited, resize.Count > 0 ? Rebuild(edited, resize) : edited);
            sized.Solve(fluid);
            res.Hints = ReFit.Hints(sized, fluid);

            // Each terminal's damper: the first one upstream on the run that
            // feeds it (up to the tee or source).
            var required = 0.0;
            foreach (BalancingHint h in res.Hints) required = Math.Max(required, h.RequiredDpPa);
            var dampers = new Dictionary<string, Component>();
            foreach (BalancingHint h in res.Hints)
            {
                string damper = DamperFeeding(sized, m, h.TerminalId);
                if (damper == null) continue;
                var fit2 = (TwoPortFitting)sized.Components[damper];
                double v = (fit2.InletFlowrate() ?? 0.0) / fit2.CrossSection.Area;
                double zeta = Balancing.BalancingZeta(h.RequiredDpPa, h.AvailableDpPa, v, fluid.Density);
                dampers[damper] = WithZeta(fit2, zeta);
                res.Dampers.Add(new DamperSettingResult
                {
                    DamperId = damper,
                    TerminalId = h.TerminalId,
                    Zeta = zeta,
                    OpenPercent = Balancing.DamperOpenPercentage(zeta),
                });
            }
            res.Network = dampers.Count > 0 ? Rebuild(sized, dampers) : sized;
            res.CriticalAfterPa = res.Network.Solve(fluid);

            if (res.Dampers.Count > 0)
            {
                var after = ReFit.Hints(res.Network, fluid);
                var withDamper = new HashSet<string>();
                foreach (DamperSettingResult d in res.Dampers) withDamper.Add(d.TerminalId);
                foreach (BalancingHint h in after)
                    if (withDamper.Contains(h.TerminalId)) res.ResidualSurplusPa = Math.Max(res.ResidualSurplusPa, h.SurplusDpPa);
            }
            sw.Stop();
            res.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            return res;
        }

        // Every component upstream of `cid` (through its in ports), not
        // including `cid` itself: the path that carries its flow.
        public static HashSet<string> Upstream(Network net, string cid)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<string>();
            stack.Push(cid);
            while (stack.Count > 0)
            {
                string at = stack.Pop();
                foreach (Port p in net.Components[at].Ports)
                {
                    if (!p.IsIn) continue;
                    foreach (string pred in net.Predecessors(Network.PortNodeId(at, p.Name)))
                    {
                        int colon = pred.IndexOf(':');
                        if (colon <= 0) continue;
                        string prev = pred.Substring(0, colon);
                        if (prev != cid && seen.Add(prev)) stack.Push(prev);
                    }
                }
            }
            return seen;
        }

        // After re-sizing: an in-line fitting (damper, attenuator, ...) or
        // tee that had the section of the duct feeding it takes that duct's
        // new section, so a damper or tee does not stay at the old size.
        public static Network FollowSizes(Network before, Network after)
        {
            var repl = new Dictionary<string, Component>();
            foreach (KeyValuePair<string, Component> kv in after.Components)
            {
                bool tee = kv.Value is Tee;
                if (!(kv.Value is TwoPortFitting) && !tee) continue;
                CrossSection own = SectionOf(kv.Value);
                string up = kv.Key;
                string inlet = tee ? "combined" : "inlet";
                CrossSection oldUp = null, newUp = null;
                for (int guard = 0; guard <= after.Components.Count && oldUp == null; guard++)
                {
                    string pred = null;
                    foreach (string n in after.Predecessors(Network.PortNodeId(up, inlet)))
                        if (n.IndexOf(':') > 0) { pred = n.Substring(0, n.IndexOf(':')); break; }
                    if (pred == null) break;
                    Component pa = after.Components[pred];
                    if (NetworkDrawing.IsDuct(pa))
                    {
                        oldUp = SectionOf(before.Components[pred]);
                        newUp = SectionOf(pa);
                    }
                    else if (pa is TwoPortFitting) { up = pred; inlet = "inlet"; }
                    else break;
                }
                if (oldUp == null || SameSection(oldUp, newUp) || !SameSection(own, oldUp)) continue;
                repl[kv.Key] = WithSection(kv.Value, newUp);
            }
            return repl.Count == 0 ? after : Rebuild(after, repl);
        }

        public static bool SameSection(CrossSection a, CrossSection b)
        {
            if (a == null || b == null) return a == b;
            var ra = a as Round; var rb = b as Round;
            if (ra != null && rb != null) return Math.Abs(ra.Diameter - rb.Diameter) < 1e-9;
            var qa = a as Rectangular; var qb = b as Rectangular;
            if (qa != null && qb != null) return Math.Abs(qa.Width - qb.Width) < 1e-9 && Math.Abs(qa.Height - qb.Height) < 1e-9;
            return false;
        }

        public static bool IsDamper(string cid, Component c, IDictionary<string, ComponentMeta> meta)
        {
            ComponentMeta m;
            return c is TwoPortFitting && meta != null && meta.TryGetValue(cid, out m) && m != null && m.Role == RoleDamper;
        }

        // Walks upstream from the terminal through single-inlet components
        // (ducts, fittings) and returns the nearest damper, stopping at a
        // tee or source.
        public static string DamperFeeding(Network net, IDictionary<string, ComponentMeta> meta, string terminalId)
        {
            string node = Network.PortNodeId(terminalId, net.Components[terminalId].Ports[0].Name);
            for (int guard = 0; guard < net.Components.Count + 1; guard++)
            {
                string up = null;
                foreach (string pred in net.Predecessors(node))
                    if (pred.IndexOf(':') > 0) { up = pred.Substring(0, pred.IndexOf(':')); break; }
                if (up == null) return null;
                Component c = net.Components[up];
                if (IsDamper(up, c, meta)) return up;
                if (!(c is RigidDuct || c is FlexDuct || c is TwoPortFitting)) return null;
                node = Network.PortNodeId(up, "inlet");
            }
            return null;
        }
    }
}
