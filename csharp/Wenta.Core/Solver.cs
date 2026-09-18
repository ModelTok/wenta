using System;
using System.Collections.Generic;

namespace Wenta
{
    /// <summary>Pure-function solver. Port of `wenta.network.solver`:
    /// 1. propagate flowrates upstream from terminals,
    /// 2. compute per-port pressure drops (each component's Compute),
    /// 3. critical-path DP (longest source→terminal path by node dp).</summary>
    public static class Solver
    {
        /// <summary>Assign a flowrate to every port: terminal demands
        /// propagated upstream (each node forwards its accumulated flow to
        /// every predecessor).</summary>
        public static void PropagateFlowrates(Network network)
        {
            List<string> topo = network.TopoOrder();

            // Reset component nodes as well as port nodes: flow is forwarded to
            // every predecessor, so a stale component-node value would be
            // re-propagated upstream and double the flows on a second solve.
            foreach (var kv in network.Components)
            {
                network.SetNodeFlow(kv.Key, 0.0);
                foreach (Port p in kv.Value.Ports)
                    network.SetNodeFlow(p.NodeId, 0.0);
            }

            foreach (Terminal term in network.Terminals())
                network.SetNodeFlow(term.Ports[0].NodeId, term.Flowrate);

            // reverse topological order: forward flow to every predecessor
            for (int i = topo.Count - 1; i >= 0; i--)
            {
                string node = topo[i];
                double flow = network.GetNodeFlow(node);
                if (flow != 0.0)
                {
                    foreach (string pred in network.Predecessors(node))
                        network.SetNodeFlow(pred, network.GetNodeFlow(pred) + flow);
                }
            }

            // copy graph flowrates onto the Port objects
            foreach (var kv in network.Components)
                foreach (Port p in kv.Value.Ports)
                    p.Flowrate = network.GetNodeFlow(p.NodeId);
        }

        /// <summary>Call each component's Compute and scatter per-port
        /// pressure drops / velocities onto the graph nodes.</summary>
        public static void ComputePressureDrops(Network network, Fluid fluid)
        {
            foreach (var kv in network.Components)
                kv.Value.Compute(fluid);

            foreach (var kv in network.Components)
                foreach (Port p in kv.Value.Ports)
                {
                    network.SetNodeDp(p.NodeId, p.PressureDrop);
                }
        }

        /// <summary>Longest path (by node pressure drop) from any Source to
        /// any Terminal — single-pass DP over the topological order.</summary>
        public static List<string> CriticalPath(Network network)
        {
            List<string> topo = network.TopoOrder();
            var dist = new Dictionary<string, double>(topo.Count);
            var prev = new Dictionary<string, string>(topo.Count);
            foreach (string n in topo)
            {
                List<string> preds = network.Predecessors(n);
                string bestP = null;
                double bestD = 0.0;
                if (preds.Count == 1)
                {
                    bestP = preds[0];
                    bestD = dist[bestP];
                }
                else if (preds.Count > 1)
                {
                    foreach (string p in preds)
                    {
                        double d = dist[p];
                        if (bestP == null || d > bestD) { bestP = p; bestD = d; }
                    }
                }
                prev[n] = bestP;
                dist[n] = bestD + network.GetNodeDp(n);
            }
            string end = null;
            double endD = double.NegativeInfinity;
            foreach (var kv in dist)
                if (kv.Value > endD) { endD = kv.Value; end = kv.Key; }
            if (end == null) return new List<string>();
            var path = new List<string>();
            string cur = end;
            while (cur != null)
            {
                path.Add(cur);
                cur = prev[cur];
            }
            path.Reverse();
            return path;
        }

        public static double CriticalPathPressureDrop(Network network)
        {
            List<string> path = CriticalPath(network);
            double sum = 0.0;
            foreach (string nodeId in path)
                sum += network.GetNodeDp(nodeId);
            return sum;
        }

        /// <summary>Full pipeline; returns the critical-path pressure drop [Pa].</summary>
        public static double Solve(Network network, Fluid fluid)
        {
            PropagateFlowrates(network);
            ComputePressureDrops(network, fluid);
            return CriticalPathPressureDrop(network);
        }
    }
}
