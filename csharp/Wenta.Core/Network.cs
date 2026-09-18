using System;
using System.Collections.Generic;

namespace Wenta
{
    /// <summary>A directed graph of ductwork components + ports.
    /// Port of `wenta.network.network` (NetworkX-free implementation):
    /// nodes are component ids and "componentId:portName" port ids;
    /// in-ports point at their component, components point at out-ports,
    /// connections go out-port -> in-port.</summary>
    public sealed class Network
    {
        public string Name = "";
        public readonly Dictionary<string, Component> Components =
            new Dictionary<string, Component>();

        // graph: node -> (predecessors, successors). Node flow/dp per solve.
        private readonly Dictionary<string, List<string>> _succ =
            new Dictionary<string, List<string>>();
        private readonly Dictionary<string, List<string>> _pred =
            new Dictionary<string, List<string>>();
        private readonly Dictionary<string, double> _nodeFlow =
            new Dictionary<string, double>();
        private readonly Dictionary<string, double> _nodeDp =
            new Dictionary<string, double>();
        private List<string> _topo; // cached

        public static string PortNodeId(string componentId, string portName)
        {
            return componentId + ":" + portName;
        }

        public Component Add(string componentId, Component component)
        {
            if (Components.ContainsKey(componentId))
                throw new WentaException("duplicate component id: '" + componentId + "'");
            Components[componentId] = component;
            AddNode(componentId);
            foreach (Port p in component.Ports)
            {
                string pid = PortNodeId(componentId, p.Name);
                p.NodeId = pid;
                AddNode(pid);
                if (p.IsIn) AddEdge(pid, componentId);      // port -> component
                else        AddEdge(componentId, pid);      // component -> port
            }
            return component;
        }

        /// <summary>Connect source -> target. Each endpoint is
        /// "&lt;componentId&gt;" or "&lt;componentId&gt;.&lt;portName&gt;".</summary>
        public void Connect(string source, string target)
        {
            string srcCid; Port srcPort;
            Resolve(source, false, out srcCid, out srcPort);
            string dstCid; Port dstPort;
            Resolve(target, true, out dstCid, out dstPort);
            AddEdge(PortNodeId(srcCid, srcPort.Name), PortNodeId(dstCid, dstPort.Name));
        }

        public double NodeFlow(string nodeId) { return _nodeFlow[nodeId]; }
        public double NodeDp(string nodeId) { return _nodeDp[nodeId]; }

        /// <summary>Topological order of all nodes (cached).</summary>
        public List<string> TopoOrder()
        {
            if (_topo != null) return _topo;
            // Kahn's algorithm, insertion-order tiebreak.
            var order = new List<string>(_succ.Count);
            var indeg = new Dictionary<string, int>(_succ.Count);
            foreach (var kv in _succ)
            {
                if (!indeg.ContainsKey(kv.Key)) indeg[kv.Key] = 0;
                foreach (string s in kv.Value)
                    indeg[s] = indeg.ContainsKey(s) ? indeg[s] + 1 : 1;
            }
            var ready = new Queue<string>();
            foreach (var kv in _succ)
                if (indeg[kv.Key] == 0) ready.Enqueue(kv.Key);
            while (ready.Count > 0)
            {
                string n = ready.Dequeue();
                order.Add(n);
                foreach (string s in _succ[n])
                {
                    if (--indeg[s] == 0) ready.Enqueue(s);
                }
            }
            if (order.Count != _succ.Count)
                throw new WentaException("network graph contains a cycle");
            _topo = order;
            return order;
        }

        public List<string> Predecessors(string nodeId) { return _pred[nodeId]; }
        public List<string> Successors(string nodeId) { return _succ[nodeId]; }

        public IList<Terminal> Terminals()
        {
            var list = new List<Terminal>();
            foreach (Component c in Components.Values)
                if (c is Terminal) list.Add((Terminal)c);
            return list;
        }

        public IList<Source> Sources()
        {
            var list = new List<Source>();
            foreach (Component c in Components.Values)
                if (c is Source) list.Add((Source)c);
            return list;
        }

        /// <summary>Structural problems; empty list = healthy.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            if (Sources().Count == 0) problems.Add("no Source component");
            if (Terminals().Count == 0) problems.Add("no Terminal component");
            foreach (var kv in Components)
            {
                bool connected = false;
                foreach (Port p in kv.Value.Ports)
                {
                    List<string> neighbours = p.IsIn ? _pred[p.NodeId] : _succ[p.NodeId];
                    foreach (string nId in neighbours)
                        if (nId.Contains(":")) { connected = true; break; } // port nodes
                    if (connected) break;
                }
                if (!connected)
                    problems.Add("component '" + kv.Key + "' is not connected");
            }
            return problems;
        }

        /// <summary>Run the full solver; returns critical-path pressure drop [Pa].</summary>
        public double Solve(Fluid fluid = null)
        {
            return Solver.Solve(this, fluid ?? Fluid.StandardAir());
        }

        // ---- internals ----------------------------------------------------

        private void AddNode(string id)
        {
            if (!_succ.ContainsKey(id))
            {
                _succ[id] = new List<string>();
                _pred[id] = new List<string>();
                _nodeFlow[id] = 0.0;
                _nodeDp[id] = 0.0;
            }
        }

        private void AddEdge(string from, string to)
        {
            if (!_succ[from].Contains(to)) _succ[from].Add(to);
            if (!_pred[to].Contains(from)) _pred[to].Add(from);
            _topo = null;
        }

        internal void SetNodeFlow(string id, double v) { _nodeFlow[id] = v; }
        internal void SetNodeDp(string id, double v) { _nodeDp[id] = v; }
        internal double GetNodeFlow(string id) { return _nodeFlow[id]; }
        internal double GetNodeDp(string id) { return _nodeDp[id]; }

        private void Resolve(string reference, bool expectedIsIn,
                             out string componentId, out Port port)
        {
            string cid, pname;
            int dot = reference.IndexOf('.');
            if (dot >= 0)
            {
                cid = reference.Substring(0, dot);
                pname = reference.Substring(dot + 1);
            }
            else
            {
                cid = reference;
                pname = null;
            }
            Component component;
            if (!Components.TryGetValue(cid, out component))
                throw new WentaException("unknown component id: '" + cid + "'");
            if (pname != null)
            {
                port = component.Port_(pname);
            }
            else
            {
                Port match = null;
                int nMatch = 0;
                foreach (Port p in component.Ports)
                    if (p.IsIn == expectedIsIn) { match = p; nMatch++; }
                if (nMatch == 0)
                    throw new WentaException("component '" + cid + "' has no "
                        + (expectedIsIn ? "'in'" : "'out'") + " ports");
                if (nMatch > 1)
                    throw new WentaException("component '" + cid + "' has multiple "
                        + (expectedIsIn ? "'in'" : "'out'") + " ports; specify one with '"
                        + cid + ".<port_name>'");
                port = match;
            }
            if (port.IsIn != expectedIsIn)
                throw new WentaException("port " + cid + "." + port.Name + " is '"
                    + (port.IsIn ? "in" : "out") + "', expected '"
                    + (expectedIsIn ? "in" : "out") + "'");
            componentId = cid;
        }
    }
}
