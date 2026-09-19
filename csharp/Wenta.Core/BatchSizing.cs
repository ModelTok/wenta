using System;
using System.Collections.Generic;
using System.Globalization;

namespace Wenta
{
    /// <summary>The five duct-sizing methods in <see cref="Sizing"/>.</summary>
    public enum SizingMethod
    {
        /// <summary><see cref="Sizing.VelocityMethod"/>: velocity ≤ TargetVelocity.</summary>
        Velocity,
        /// <summary><see cref="Sizing.EqualFrictionMethod"/>: linear drop ≤ TargetPaPerM.</summary>
        EqualFriction,
        /// <summary><see cref="Sizing.PressureDropBudget"/>: drop over LengthM ≤ BudgetPa.</summary>
        PressureDropBudget,
        /// <summary><see cref="Sizing.NoiseLimitMethod"/>: velocity ≤ the SpaceType's NC limit.</summary>
        NoiseLimit,
        /// <summary><see cref="Sizing.AspectRatioMethod"/>: rectangular, velocity ≤
        /// TargetVelocity at side ratio ≥ AspectRatio (Shape is ignored).</summary>
        AspectRatio
    }

    /// <summary>One duct to size. Only the fields the chosen
    /// <see cref="Method"/> needs are read; defaults mirror the parameter
    /// defaults of the corresponding <see cref="Sizing"/> method.</summary>
    public sealed class BatchSizingRequest
    {
        /// <summary>Caller's identifier for the duct (echoed on the result).</summary>
        public string Id;
        /// <summary>Volumetric flow [m^3/s]; must be positive.</summary>
        public double FlowrateM3s;
        /// <summary><see cref="Sizing.ShapeRound"/> or
        /// <see cref="Sizing.ShapeRectangular"/> (all methods except AspectRatio,
        /// which is always rectangular).</summary>
        public string Shape = Sizing.ShapeRound;
        public SizingMethod Method = SizingMethod.Velocity;
        /// <summary>Velocity / AspectRatio: maximum velocity [m/s].</summary>
        public double TargetVelocity = 4.0;
        /// <summary>EqualFriction: maximum linear drop [Pa/m].</summary>
        public double TargetPaPerM = 1.0;
        /// <summary>PressureDropBudget: duct length [m].</summary>
        public double LengthM;
        /// <summary>PressureDropBudget: total drop allowed over LengthM [Pa].</summary>
        public double BudgetPa;
        /// <summary>NoiseLimit: key into <see cref="Sizing.NoiseLimitsMs"/>.</summary>
        public string SpaceType = "office";
        /// <summary>AspectRatio: minimum long/short side ratio (≥ 1).</summary>
        public double AspectRatio = 2.0;
        /// <summary>EqualFriction / PressureDropBudget / NoiseLimit: absolute
        /// wall roughness [m].</summary>
        public double AbsoluteRoughnessM = RigidDuct.DefaultAbsoluteRoughness;
    }

    /// <summary>Outcome for one <see cref="BatchSizingRequest"/>. Exactly one
    /// of <see cref="Result"/> / <see cref="Error"/> is set.</summary>
    public sealed class BatchSizingResult
    {
        /// <summary>Echo of <see cref="BatchSizingRequest.Id"/>.</summary>
        public string Id;
        /// <summary>The computed section; null when <see cref="Error"/> is set.</summary>
        public Sizing.SizingResult Result;
        /// <summary>Round results: the standard's smallest nominal diameter
        /// [mm] ≥ the computed diameter (clamped to the table ends).</summary>
        public int? SnappedRoundMm;
        /// <summary>Rectangular results: <c>[width_mm, height_mm]</c> of the
        /// smallest-area standard pair with width ≥ and height ≥ the computed
        /// dims; null when no standard pair is large enough (not an error).</summary>
        public int[] SnappedRectMm;
        /// <summary><see cref="WentaException"/> message when this request
        /// failed; null on success.</summary>
        public string Error;
    }

    /// <summary>Batch size-on-draw (WENTASIZE, issue #24): size many ducts in
    /// one call, each with its own method, and snap the results to a
    /// <see cref="Standard"/>'s size tables. Requests are independent — a
    /// failing request records its error and does not abort the batch.</summary>
    public static class BatchSizing
    {
        /// <summary>CSV header, matching <see cref="ToCsv"/>.</summary>
        public static readonly string[] Fields =
        {
            "id",
            "shape",
            "diameter_m",
            "width_m",
            "height_m",
            "velocity_ms",
            "dp_pa_per_m",
            "snapped_round_mm",
            "snapped_rect_w_mm",
            "snapped_rect_h_mm",
            "error",
        };

        /// <summary>Size every request independently. Order of results matches
        /// the order of requests. <paramref name="fluid"/> defaults to
        /// <see cref="Fluid.StandardAir"/>; only the friction-based methods use it.
        /// Throws <see cref="WentaException"/> only when
        /// <paramref name="requests"/> itself is null.</summary>
        public static List<BatchSizingResult> Size(IList<BatchSizingRequest> requests,
            Standard standard = Standard.En1505_1506, Fluid fluid = null)
        {
            if (requests == null)
                throw new WentaException("requests is null");
            fluid = fluid ?? Fluid.StandardAir();

            var results = new List<BatchSizingResult>(requests.Count);
            foreach (BatchSizingRequest req in requests)
            {
                var res = new BatchSizingResult { Id = req != null ? req.Id : null };
                try
                {
                    if (req == null)
                        throw new WentaException("request is null");
                    res.Result = SizeOne(req, fluid);
                    Snap(res, standard);
                }
                catch (WentaException ex)
                {
                    res.Result = null;
                    res.SnappedRoundMm = null;
                    res.SnappedRectMm = null;
                    res.Error = ex.Message;
                }
                results.Add(res);
            }
            return results;
        }

        private static Sizing.SizingResult SizeOne(BatchSizingRequest req, Fluid fluid)
        {
            switch (req.Method)
            {
                case SizingMethod.Velocity:
                    return Sizing.VelocityMethod(req.FlowrateM3s, req.Shape, req.TargetVelocity);
                case SizingMethod.EqualFriction:
                    return Sizing.EqualFrictionMethod(req.FlowrateM3s, req.TargetPaPerM,
                        req.Shape, req.AbsoluteRoughnessM, fluid);
                case SizingMethod.PressureDropBudget:
                    return Sizing.PressureDropBudget(req.FlowrateM3s, req.LengthM, req.BudgetPa,
                        req.Shape, req.AbsoluteRoughnessM, fluid);
                case SizingMethod.NoiseLimit:
                    return Sizing.NoiseLimitMethod(req.FlowrateM3s, req.SpaceType, req.Shape,
                        req.AbsoluteRoughnessM, fluid);
                case SizingMethod.AspectRatio:
                    return Sizing.AspectRatioMethod(req.FlowrateM3s, req.TargetVelocity,
                        req.AspectRatio);
                default:
                    throw new WentaException("unknown sizing method " + req.Method);
            }
        }

        /// <summary>Millimetres from metres, rounded to a micrometre so that
        /// e.g. 0.3 m compares equal to the 300 mm table entry.</summary>
        private static double ToMm(double metres)
        {
            return Math.Round(metres * 1000.0, 3);
        }

        private static void Snap(BatchSizingResult res, Standard standard)
        {
            Round round = res.Result.Section as Round;
            if (round != null)
            {
                res.SnappedRoundMm = Standards.NearestRoundSizeFor(standard, ToMm(round.Diameter), true);
                return;
            }
            Rectangular rect = res.Result.Section as Rectangular;
            if (rect != null)
                res.SnappedRectMm = SnapRectangular(standard, ToMm(rect.Width), ToMm(rect.Height));
        }

        /// <summary>Smallest-area <c>[w, h]</c> pair in the standard's
        /// rectangular table with <c>w ≥ widthMm</c> and <c>h ≥ heightMm</c>
        /// (ties keep the first table entry); null when none fits.</summary>
        public static int[] SnapRectangular(Standard standard, double widthMm, double heightMm)
        {
            int[] best = null;
            long bestArea = long.MaxValue;
            foreach (int[] wh in Standards.StandardRectangularSizes(standard))
            {
                if (wh[0] < widthMm || wh[1] < heightMm) continue;
                long area = (long)wh[0] * wh[1];
                if (area < bestArea)
                {
                    bestArea = area;
                    best = wh;
                }
            }
            return best == null ? null : new[] { best[0], best[1] };
        }

        /// <summary>CSV with header
        /// <c>id,shape,diameter_m,width_m,height_m,velocity_ms,dp_pa_per_m,snapped_round_mm,snapped_rect_w_mm,snapped_rect_h_mm,error</c>;
        /// invariant culture, empty cells for values that do not apply, no
        /// trailing newline. Free-text cells (id, error) are quoted when they
        /// contain a comma, quote or newline.</summary>
        public static string ToCsv(IList<BatchSizingResult> results)
        {
            var lines = new List<string> { string.Join(",", Fields) };
            if (results == null) return lines[0];
            foreach (BatchSizingResult r in results)
            {
                string shape = "", diameter = "", width = "", height = "", velocity = "", dpPerM = "";
                if (r.Result != null)
                {
                    Round round = r.Result.Section as Round;
                    Rectangular rect = r.Result.Section as Rectangular;
                    if (round != null)
                    {
                        shape = Sizing.ShapeRound;
                        diameter = round.Diameter.ToString(CultureInfo.InvariantCulture);
                    }
                    else if (rect != null)
                    {
                        shape = Sizing.ShapeRectangular;
                        width = rect.Width.ToString(CultureInfo.InvariantCulture);
                        height = rect.Height.ToString(CultureInfo.InvariantCulture);
                    }
                    velocity = r.Result.Velocity.ToString(CultureInfo.InvariantCulture);
                    if (r.Result.PressureDropPerMeter != 0.0)
                        dpPerM = r.Result.PressureDropPerMeter.ToString(CultureInfo.InvariantCulture);
                }
                lines.Add(string.Join(",", new[]
                {
                    Quote(r.Id),
                    shape,
                    diameter,
                    width,
                    height,
                    velocity,
                    dpPerM,
                    r.SnappedRoundMm.HasValue
                        ? r.SnappedRoundMm.Value.ToString(CultureInfo.InvariantCulture) : "",
                    r.SnappedRectMm != null
                        ? r.SnappedRectMm[0].ToString(CultureInfo.InvariantCulture) : "",
                    r.SnappedRectMm != null
                        ? r.SnappedRectMm[1].ToString(CultureInfo.InvariantCulture) : "",
                    Quote(r.Error),
                }));
            }
            return string.Join("\n", lines.ToArray());
        }

        private static string Quote(string s)
        {
            if (s == null) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
