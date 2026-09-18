using System;
using System.Collections.Generic;
using System.Text;

namespace Wenta
{
    /// <summary>Room ventilation air balance — per-room supply vs. exhaust,
    /// imbalance and air-change rate (bilans powietrza w pomieszczeniach).
    /// Port of `venti/src/room.rs` (Phase 4 vertical: the "balancing" family).
    ///
    /// Every room's supply (nawiew) and exhaust (wywiew) flows are reconciled:
    /// bathrooms/kitchens are net exhaust, cleanrooms/corridors net supply,
    /// mechanical rooms target near-zero net flow. SI units: flows in m³/s,
    /// volumes in m³.</summary>
    public sealed class RoomBalance
    {
        public readonly double SupplyM3s;
        public readonly double ExhaustM3s;

        public RoomBalance(double supplyM3s, double exhaustM3s)
        {
            if (supplyM3s < 0.0)
                throw new WentaException("supply_m3s must be non-negative");
            if (exhaustM3s < 0.0)
                throw new WentaException("exhaust_m3s must be non-negative");
            SupplyM3s = supplyM3s;
            ExhaustM3s = exhaustM3s;
        }

        /// <summary>Net room flow, m³/s — positive = excess supply
        /// pressurising the room; negative = excess exhaust.</summary>
        public double NetM3s() { return SupplyM3s - ExhaustM3s; }

        /// <summary>True when |net| ≤ tolerance. Negative tolerance never balanced.</summary>
        public bool IsBalanced(double tolerance)
        {
            return tolerance >= 0.0 && Math.Abs(NetM3s()) <= tolerance;
        }

        /// <summary>Dimensionless imbalance net/max(supply,exhaust) ∈ [-1, 1].
        /// Returns null (undefined) when both flows are zero.</summary>
        public double? ImbalanceFraction()
        {
            double peak = Math.Max(SupplyM3s, ExhaustM3s);
            if (peak <= 0.0) return null;
            return NetM3s() / peak;
        }
    }

    /// <summary>A named collection of room balances with totals and CSV.</summary>
    public sealed class RoomBalanceSet
    {
        private readonly List<string> _order = new List<string>();
        private readonly List<RoomBalance> _rooms = new List<RoomBalance>();
        private readonly Dictionary<string, double> _volumes =
            new Dictionary<string, double>();

        public void Add(string name, RoomBalance bal)
        {
            _order.Add(name);
            _rooms.Add(bal);
        }

        /// <summary>Add a room with its volume (m³) so its ACH can be computed.
        /// Non-positive volume is ignored (ach column renders empty).</summary>
        public void AddWithVolume(string name, RoomBalance bal, double volumeM3)
        {
            if (volumeM3 > 0.0) _volumes[name] = volumeM3;
            Add(name, bal);
        }

        public double TotalSupplyM3s()
        {
            double t = 0.0;
            foreach (RoomBalance b in _rooms) t += b.SupplyM3s;
            return t;
        }

        public double TotalExhaustM3s()
        {
            double t = 0.0;
            foreach (RoomBalance b in _rooms) t += b.ExhaustM3s;
            return t;
        }

        public double OverallNetM3s() { return TotalSupplyM3s() - TotalExhaustM3s(); }

        /// <summary>True when |overall net| ≤ tolerance.</summary>
        public bool IsBalanced(double tolerance)
        {
            return tolerance >= 0.0 && Math.Abs(OverallNetM3s()) <= tolerance;
        }

        /// <summary>CSV: header `name,supply_m3s,exhaust_m3s,net_m3s,ach`.
        /// ACH column = supply converted via AirChangesPerHour for rooms with a
        /// recorded volume; empty otherwise. Numbers trimmed to 6 decimals.</summary>
        public string CsvRender()
        {
            var sb = new StringBuilder();
            sb.AppendLine("name,supply_m3s,exhaust_m3s,net_m3s,ach");
            for (int i = 0; i < _order.Count; i++)
            {
                string name = _order[i];
                RoomBalance bal = _rooms[i];
                string ach = "";
                if (_volumes.ContainsKey(name) && _volumes[name] > 0.0)
                    ach = FmtNum(Units.AirChangesPerHour(bal.SupplyM3s, _volumes[name]));
                sb.Append(name).Append(',')
                  .Append(FmtNum(bal.SupplyM3s)).Append(',')
                  .Append(FmtNum(bal.ExhaustM3s)).Append(',')
                  .Append(FmtNum(bal.NetM3s())).Append(',')
                  .Append(ach);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>Format with up to 6 decimals, trimming trailing zeros:
        /// -0.03125 → "-0.03125", 14.4 → "14.4", 0.0 → "0". Shared with
        /// <see cref="ClashDetection.ClashesAsCsv"/>.</summary>
        internal static string FmtNum(double v)
        {
            return v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}