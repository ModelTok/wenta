using System;
using System.Collections.Generic;
using System.Globalization;

namespace Wenta
{
    /// <summary>What a single piece of a quick-connect chain is.</summary>
    public enum ConnectorKind
    {
        /// <summary>Identical sections butted straight together (ζ 0, zero length).</summary>
        Direct,
        /// <summary>Same-shape size change, outlet smaller than inlet.</summary>
        Reducer,
        /// <summary>Same-shape size change, outlet larger than inlet.</summary>
        Expander,
        /// <summary>Shape change (round ↔ rectangular).</summary>
        Transition,
        /// <summary>Flexible connection bridging a gap (capped at
        /// <see cref="QuickConnectOptions.MaxFlexLengthM"/>).</summary>
        Flex,
        /// <summary>Straight filler length of plain duct.</summary>
        Spacer,
    }

    /// <summary>One fitting in a quick-connect chain.</summary>
    public sealed class ConnectorPiece
    {
        /// <summary>Which kind of fitting this piece is.</summary>
        public ConnectorKind Kind;
        /// <summary>Section at the upstream face.</summary>
        public CrossSection From;
        /// <summary>Section at the downstream face.</summary>
        public CrossSection To;
        /// <summary>Laying length of the piece [m].</summary>
        public double LengthM;
        /// <summary>Loss coefficient, referenced to the velocity in the
        /// <i>smaller</i> of <see cref="From"/> / <see cref="To"/>.</summary>
        public double Zeta;
        /// <summary>ζ · ρ·v²/2 [Pa] with v taken at the smaller section.</summary>
        public double PressureDropPa;
        /// <summary>Free text: where ζ came from, and any caveat about the
        /// piece's length.</summary>
        public string Note;
    }

    /// <summary>Inputs for <see cref="QuickConnect.Plan"/>.</summary>
    public sealed class QuickConnectOptions
    {
        /// <summary>Maximum <i>included</i> taper angle of a reducer / expander /
        /// transition [°]. The piece is laid out at exactly this angle, i.e. at
        /// its shortest permitted length:
        ///
        /// <code>L_min = |d1 − d2| / (2 · tan(angle / 2))</code>
        ///
        /// which is the same as saying each wall slopes at half the included
        /// angle. For a round ↔ rectangular transition the numerator is the
        /// largest face offset rather than a diameter difference — see
        /// <see cref="QuickConnect.TaperLengthM"/>.
        ///
        /// <b>This is a design rule of thumb, not a quoted clause.</b> Nothing
        /// in this repository states a normative maximum taper angle; 15°
        /// included is the usual workshop default for a low-loss transformation
        /// piece. Raise it for a tighter (and lossier) fitting. Must be in
        /// (0, 90).</summary>
        public double MaxTaperAngleDeg = 15.0;

        /// <summary>Clear distance between the two duct ends that the chain has
        /// to span [m]. Must be ≥ 0. With a gap given, the pieces returned by
        /// <see cref="QuickConnect.Plan"/> sum to the gap — unless a taper is
        /// needed whose minimum length already exceeds it, in which case the
        /// chain is longer than the gap and the taper's
        /// <see cref="ConnectorPiece.Note"/> says so.</summary>
        public double GapM = 0.0;

        /// <summary>Volumetric flow through the joint [m³/s]. Must be &gt; 0.</summary>
        public double FlowrateM3s;

        /// <summary>Air properties; null means <see cref="Fluid.StandardAir"/>.</summary>
        public Fluid Fluid;

        /// <summary>Longest single flexible connection allowed [m]. A longer
        /// gap gets a flex of this length plus a <see cref="ConnectorKind.Spacer"/>
        /// for the remainder. 0 disables flexes; must be ≥ 0.</summary>
        public double MaxFlexLengthM = 1.5;

        /// <summary>Bridge a gap with a flexible connection rather than a rigid
        /// spacer.</summary>
        public bool PreferFlex = false;

        /// <summary>Optional manufacturer ζ data. Entries are matched by type
        /// (<c>reducer</c>, <c>expander</c>, <c>transition</c>, <c>flex</c>) and
        /// by the size window <c>[d_from_mm, d_to_mm]</c>, where the sizes are
        /// the characteristic diameters of the two sections. A match wins over
        /// the built-in correlation; null means "always use the correlation".</summary>
        public ZetaCatalog Catalog;
    }

    /// <summary>WENTACONNECT (issue #57): given two duct ends that must be
    /// joined, pick the chain of fittings that connects them and cost it.
    ///
    /// <para><b>Reference velocity.</b> Every piece's ζ — and therefore its
    /// <see cref="ConnectorPiece.PressureDropPa"/> — is referenced to the
    /// velocity in the <i>smaller</i> (smaller-area) of the two sections. That
    /// is deliberate and it is also what the correlations want:
    /// <see cref="FittingsLibrary.ReducerRound"/> is referenced to the outlet
    /// velocity (the smaller end of a reducer) and
    /// <see cref="FittingsLibrary.ExpanderRound"/> to the inlet velocity (the
    /// smaller end of an expander), so "smaller section" is the correct
    /// reference for both without any re-referencing.</para>
    ///
    /// <para><b>Where ζ comes from.</b> The catalog is consulted first via
    /// <see cref="ZetaCatalog.Match"/> and the built-in correlation is used
    /// otherwise. <see cref="ZetaCatalog.ZetaFor"/> is deliberately <i>not</i>
    /// used: its correlation fallback has no case for reducer / expander /
    /// transition and would throw. Each piece's
    /// <see cref="ConnectorPiece.Note"/> records which of the two supplied the
    /// number.</para>
    ///
    /// <para><b>What is not costed here.</b> A <see cref="ConnectorKind.Spacer"/>
    /// carries ζ 0: its loss is skin friction over a straight length, which is
    /// the duct's job, not the fitting's — model it with a
    /// <see cref="RigidDuct"/> of <see cref="ConnectorPiece.LengthM"/>. A
    /// <see cref="ConnectorKind.Flex"/> likewise carries ζ 0 unless the catalog
    /// has a <c>flex</c> entry, because <see cref="FittingsLibrary"/> has no
    /// flexible-connection ζ correlation and this class does not invent one;
    /// model it with a <see cref="FlexDuct"/> over the same length (its
    /// manufacturer Δp/m and <see cref="Flex.StretchCorrectionFactor"/> are the
    /// honest way to price a flex).</para></summary>
    public static class QuickConnect
    {
        /// <summary>Length/diameter comparisons closer than this are treated as
        /// equal [m].</summary>
        private const double Tol = 1e-9;

        /// <summary>CSV header, matching <see cref="ToCsv"/>.</summary>
        public static readonly string[] Fields =
        {
            "kind",
            "from",
            "to",
            "length_m",
            "zeta",
            "pressure_drop_pa",
            "note",
        };

        /// <summary>Plan the ordered fitting chain from <paramref name="from"/>
        /// to <paramref name="to"/>.
        ///
        /// <list type="bullet">
        /// <item>identical sections and no gap → one <see cref="ConnectorKind.Direct"/>,
        /// ζ 0, length 0;</item>
        /// <item>same-shape size change → <see cref="ConnectorKind.Reducer"/> or
        /// <see cref="ConnectorKind.Expander"/> laid out at
        /// <see cref="QuickConnectOptions.MaxTaperAngleDeg"/>;</item>
        /// <item>shape change (round ↔ rectangular) →
        /// <see cref="ConnectorKind.Transition"/>, same taper rule;</item>
        /// <item>then, for whatever of the gap the taper does not fill:
        /// a <see cref="ConnectorKind.Flex"/> (capped at
        /// <see cref="QuickConnectOptions.MaxFlexLengthM"/>) when
        /// <see cref="QuickConnectOptions.PreferFlex"/> is set or when a taper
        /// could not fill the gap on its own, and a
        /// <see cref="ConnectorKind.Spacer"/> for anything still left.</item>
        /// </list>
        ///
        /// The taper, when there is one, comes first — it sits against the
        /// <paramref name="from"/> end, and the flex/spacer that follow it run
        /// at the <paramref name="to"/> section.
        ///
        /// Throws <see cref="WentaException"/> for a null section or options, a
        /// non-positive flowrate, a negative gap, a negative maximum flex
        /// length, or a taper angle outside (0, 90).</summary>
        public static List<ConnectorPiece> Plan(CrossSection from, CrossSection to,
                                                QuickConnectOptions options)
        {
            if (from == null) throw new WentaException("from cross-section is null");
            if (to == null) throw new WentaException("to cross-section is null");
            if (options == null) throw new WentaException("options is null");
            if (options.FlowrateM3s <= 0.0)
                throw new WentaException("flowrate must be positive, got "
                    + options.FlowrateM3s.ToString(CultureInfo.InvariantCulture));
            if (options.GapM < 0.0)
                throw new WentaException("gap_m must be non-negative, got "
                    + options.GapM.ToString(CultureInfo.InvariantCulture));
            if (options.MaxFlexLengthM < 0.0)
                throw new WentaException("max_flex_length_m must be non-negative, got "
                    + options.MaxFlexLengthM.ToString(CultureInfo.InvariantCulture));
            if (options.MaxTaperAngleDeg <= 0.0 || options.MaxTaperAngleDeg >= 90.0)
                throw new WentaException("max_taper_angle_deg must be in (0, 90), got "
                    + options.MaxTaperAngleDeg.ToString(CultureInfo.InvariantCulture));

            Fluid fluid = options.Fluid ?? Fluid.StandardAir();
            double rho = fluid.Density;
            // Reference velocity: the smaller section (see the class remarks).
            double smallestArea = from.Area <= to.Area ? from.Area : to.Area;
            double velocity = options.FlowrateM3s / smallestArea;

            var chain = new List<ConnectorPiece>();
            bool same = SameSection(from, to);
            double gapLeft = options.GapM;

            if (!same)
            {
                ConnectorPiece taper = BuildTaper(from, to, options, velocity, rho);
                chain.Add(taper);
                gapLeft = options.GapM - taper.LengthM;
                if (options.GapM > Tol && gapLeft < -Tol)
                {
                    taper.Note = taper.Note + "; minimum taper length "
                        + Fmt(taper.LengthM) + " m exceeds the requested gap "
                        + Fmt(options.GapM)
                        + " m — the chain is longer than the gap, pull the ends apart";
                }
                if (gapLeft < Tol) gapLeft = 0.0;
            }

            // Whatever the taper did not fill.
            CrossSection filler = same ? from : to;
            if (gapLeft > Tol)
            {
                bool useFlex = options.PreferFlex || !same;
                if (useFlex && options.MaxFlexLengthM > Tol)
                {
                    double flexLen = gapLeft <= options.MaxFlexLengthM
                        ? gapLeft : options.MaxFlexLengthM;
                    chain.Add(BuildFlex(filler, flexLen, options, velocity, rho));
                    gapLeft -= flexLen;
                }
                if (gapLeft > Tol)
                    chain.Add(BuildSpacer(filler, gapLeft));
            }

            if (chain.Count == 0)
            {
                chain.Add(new ConnectorPiece
                {
                    Kind = ConnectorKind.Direct,
                    From = from,
                    To = to,
                    LengthM = 0.0,
                    Zeta = 0.0,
                    PressureDropPa = 0.0,
                    Note = "identical sections, no gap: direct flanged/butt joint, zeta 0",
                });
            }
            return chain;
        }

        /// <summary>Sum of <see cref="ConnectorPiece.PressureDropPa"/> [Pa];
        /// 0 for a null or empty chain.</summary>
        public static double TotalPressureDropPa(IList<ConnectorPiece> pieces)
        {
            if (pieces == null) return 0.0;
            double total = 0.0;
            foreach (ConnectorPiece p in pieces)
                if (p != null) total += p.PressureDropPa;
            return total;
        }

        /// <summary>Sum of <see cref="ConnectorPiece.LengthM"/> [m]; 0 for a
        /// null or empty chain. Equals
        /// <see cref="QuickConnectOptions.GapM"/> whenever a gap was given and
        /// no taper needed more room than the gap.</summary>
        public static double TotalLengthM(IList<ConnectorPiece> pieces)
        {
            if (pieces == null) return 0.0;
            double total = 0.0;
            foreach (ConnectorPiece p in pieces)
                if (p != null) total += p.LengthM;
            return total;
        }

        /// <summary>CSV with the header
        /// <c>kind,from,to,length_m,zeta,pressure_drop_pa,note</c>; invariant
        /// culture, no trailing newline. Free-text cells are quoted when they
        /// contain a comma, quote or newline (same rule as
        /// <see cref="BatchSizing.ToCsv"/>).</summary>
        public static string ToCsv(IList<ConnectorPiece> pieces)
        {
            var lines = new List<string> { string.Join(",", Fields) };
            if (pieces == null) return lines[0];
            foreach (ConnectorPiece p in pieces)
            {
                if (p == null) continue;
                lines.Add(string.Join(",", new[]
                {
                    p.Kind.ToString(),
                    Quote(p.From == null ? "" : p.From.Describe()),
                    Quote(p.To == null ? "" : p.To.Describe()),
                    p.LengthM.ToString(CultureInfo.InvariantCulture),
                    p.Zeta.ToString(CultureInfo.InvariantCulture),
                    p.PressureDropPa.ToString(CultureInfo.InvariantCulture),
                    Quote(p.Note),
                }));
            }
            return string.Join("\n", lines.ToArray());
        }

        /// <summary>Shortest fitting length [m] that keeps the included taper
        /// angle at or below <paramref name="includedAngleDeg"/>:
        /// <c>L = offset / tan(angle / 2)</c>, where <c>offset</c> is how far
        /// one wall has to move sideways. For two round sections that offset is
        /// <c>|d1 − d2| / 2</c>, giving the familiar
        /// <c>|d1 − d2| / (2·tan(angle/2))</c>; for a round ↔ rectangular
        /// transition it is the largest single-wall offset over the two axes,
        /// so the steepest wall — not an average — sets the length. A design
        /// rule of thumb, not a quoted standard clause.</summary>
        public static double TaperLengthM(CrossSection a, CrossSection b,
                                          double includedAngleDeg)
        {
            if (a == null || b == null)
                throw new WentaException("cross-sections must not be null");
            if (includedAngleDeg <= 0.0 || includedAngleDeg >= 90.0)
                throw new WentaException("included angle must be in (0, 90), got "
                    + includedAngleDeg.ToString(CultureInfo.InvariantCulture));
            double offset = WallOffsetM(a, b);
            return offset / Math.Tan(includedAngleDeg * Math.PI / 360.0);
        }

        /// <summary>Diameter used to characterise a section for the ζ
        /// correlations: the round diameter for a <see cref="Round"/>, and the
        /// <i>area-equivalent</i> diameter √(4A/π) for a
        /// <see cref="Rectangular"/>. Area-equivalent, not
        /// <see cref="Geometry.EquivalentRoundDiameter"/>: the reducer/expander
        /// correlations are pure area-ratio formulas, so feeding them a
        /// friction-equivalent diameter would misstate the ratio they are built
        /// on.</summary>
        public static double CharacteristicDiameterM(CrossSection s)
        {
            if (s == null) throw new WentaException("cross-section is null");
            Round r = s as Round;
            if (r != null) return r.Diameter;
            return Math.Sqrt(4.0 * s.Area / Math.PI);
        }

        // ---- internals ------------------------------------------------------

        /// <summary>Largest distance a single wall must travel [m]: half the
        /// diameter difference for round↔round, half the biggest per-axis
        /// difference otherwise (the round section is treated as a square of
        /// side d for the purpose of pairing up faces).</summary>
        private static double WallOffsetM(CrossSection a, CrossSection b)
        {
            Round ra = a as Round, rb = b as Round;
            if (ra != null && rb != null)
                return Math.Abs(ra.Diameter - rb.Diameter) / 2.0;

            double aw, ah, bw, bh;
            FaceSizes(a, out aw, out ah);
            FaceSizes(b, out bw, out bh);
            double dw = Math.Abs(aw - bw);
            double dh = Math.Abs(ah - bh);
            return (dw >= dh ? dw : dh) / 2.0;
        }

        /// <summary>Width/height of a section's face; a round section reports
        /// its diameter on both axes.</summary>
        private static void FaceSizes(CrossSection s, out double width, out double height)
        {
            Round r = s as Round;
            if (r != null) { width = r.Diameter; height = r.Diameter; return; }
            Rectangular q = s as Rectangular;
            if (q != null) { width = q.Width; height = q.Height; return; }
            double d = CharacteristicDiameterM(s);
            width = d;
            height = d;
        }

        private static bool IsRound(CrossSection s) { return s is Round; }

        /// <summary>Same shape and same dimensions (to <see cref="Tol"/>).</summary>
        private static bool SameSection(CrossSection a, CrossSection b)
        {
            Round ra = a as Round, rb = b as Round;
            if (ra != null && rb != null)
                return Math.Abs(ra.Diameter - rb.Diameter) < Tol;
            Rectangular qa = a as Rectangular, qb = b as Rectangular;
            if (qa != null && qb != null)
                return Math.Abs(qa.Width - qb.Width) < Tol
                    && Math.Abs(qa.Height - qb.Height) < Tol;
            if ((ra == null) != (rb == null)) return false;
            return Math.Abs(a.Area - b.Area) < Tol
                && Math.Abs(a.HydraulicDiameter - b.HydraulicDiameter) < Tol;
        }

        /// <summary>Reducer / expander / transition piece.</summary>
        private static ConnectorPiece BuildTaper(CrossSection from, CrossSection to,
            QuickConnectOptions options, double velocity, double rho)
        {
            double angle = options.MaxTaperAngleDeg;
            double dFrom = CharacteristicDiameterM(from);
            double dTo = CharacteristicDiameterM(to);
            bool shapeChange = IsRound(from) != IsRound(to);
            bool contracting = to.Area < from.Area;

            ConnectorKind kind = shapeChange
                ? ConnectorKind.Transition
                : (contracting ? ConnectorKind.Reducer : ConnectorKind.Expander);
            string type = kind == ConnectorKind.Reducer ? "reducer"
                        : kind == ConnectorKind.Expander ? "expander"
                        : "transition";

            double zeta;
            string note;
            string catalogSource;
            var sizeMm = new double[] { dFrom * 1000.0, dTo * 1000.0 };
            if (TryCatalogZeta(options.Catalog, type, sizeMm, out zeta, out catalogSource))
            {
                note = "zeta from " + catalogSource;
            }
            else if (contracting)
            {
                zeta = FittingsLibrary.ReducerRound(dFrom, dTo, angle);
                note = "zeta from FittingsLibrary.ReducerRound(d_in="
                     + Fmt(dFrom) + ", d_out=" + Fmt(dTo) + ", angle="
                     + Fmt(angle) + " deg)";
            }
            else
            {
                zeta = FittingsLibrary.ExpanderRound(dFrom, dTo, angle);
                note = "zeta from FittingsLibrary.ExpanderRound(d_in="
                     + Fmt(dFrom) + ", d_out=" + Fmt(dTo) + ", angle="
                     + Fmt(angle) + " deg)";
            }
            if (shapeChange)
            {
                note = note + "; round<->rect transition costed on area-equivalent"
                     + " diameters (no dedicated transition correlation in"
                     + " FittingsLibrary)";
            }
            note = note + "; length from the "
                 + Fmt(angle) + " deg taper rule";

            return new ConnectorPiece
            {
                Kind = kind,
                From = from,
                To = to,
                LengthM = TaperLengthM(from, to, angle),
                Zeta = zeta,
                PressureDropPa = Losses.LocalPressureDrop(zeta, velocity, rho),
                Note = note,
            };
        }

        private static ConnectorPiece BuildFlex(CrossSection section, double lengthM,
            QuickConnectOptions options, double velocity, double rho)
        {
            double zeta;
            string catalogSource;
            double d = CharacteristicDiameterM(section);
            string note;
            if (TryCatalogZeta(options.Catalog, "flex",
                    new double[] { d * 1000.0, d * 1000.0 }, out zeta, out catalogSource))
            {
                note = "flexible connection; zeta from " + catalogSource;
            }
            else
            {
                zeta = 0.0;
                note = "flexible connection; zeta 0 - FittingsLibrary has no flex"
                     + " correlation and none is invented here: price its friction"
                     + " with a FlexDuct of this length (manufacturer Pa/m)";
            }
            if (lengthM >= options.MaxFlexLengthM - Tol)
                note = note + "; capped at max_flex_length_m " + Fmt(options.MaxFlexLengthM) + " m";
            return new ConnectorPiece
            {
                Kind = ConnectorKind.Flex,
                From = section,
                To = section,
                LengthM = lengthM,
                Zeta = zeta,
                PressureDropPa = Losses.LocalPressureDrop(zeta, velocity, rho),
                Note = note,
            };
        }

        private static ConnectorPiece BuildSpacer(CrossSection section, double lengthM)
        {
            return new ConnectorPiece
            {
                Kind = ConnectorKind.Spacer,
                From = section,
                To = section,
                LengthM = lengthM,
                Zeta = 0.0,
                PressureDropPa = 0.0,
                Note = "straight filler; zeta 0 - a spacer's loss is skin friction"
                     + " over its length, which is the duct's job, not the"
                     + " fitting's: model it as a RigidDuct of this length",
            };
        }

        /// <summary>Catalog lookup by (type, [d_from_mm, d_to_mm]). Returns
        /// false when there is no catalog or no matching entry, leaving the
        /// caller to use the correlation.</summary>
        private static bool TryCatalogZeta(ZetaCatalog catalog, string type, double[] sizeMm,
                                           out double zeta, out string source)
        {
            zeta = 0.0;
            source = null;
            if (catalog == null) return false;
            ZetaCatalog.CatalogEntry e = catalog.Match(type, sizeMm);
            if (e == null) return false;
            zeta = e.Zeta;
            source = "catalog entry '" + e.Id + "'"
                   + (string.IsNullOrEmpty(e.Source) ? "" : " (source " + e.Source + ")");
            return true;
        }

        private static string Fmt(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Quote(string s)
        {
            if (s == null) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
