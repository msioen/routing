using System.Collections.Generic;
using Itinero.Algorithms.Collections;
using Itinero.Algorithms.Contracted.Dual.Witness;
using Itinero.Algorithms.PriorityQueues;
using Itinero.Algorithms.Weights;
using Itinero.Data.Contracted.Edges;
using Itinero.Graphs.Directed;
using Itinero.Logging;

namespace Itinero.Algorithms.Contracted.Dual
{
    public class FastHierarchyBuilder<T> : AlgorithmBase
    where T : struct
    {
        protected readonly DirectedMetaGraph _graph;
        private readonly static Logger _logger = Logger.Create("HierarchyBuilder");
        protected readonly WeightHandler<T> _weightHandler;
        private readonly Dictionary<uint, int> _contractionCount;
        private readonly Dictionary<long, int> _depth;
        protected VertexInfo<T> _vertexInfo;
        public const float E = 0.1f;

        /// <summary>
        /// Creates a new hierarchy builder.
        /// </summary>
        public FastHierarchyBuilder(DirectedMetaGraph graph,
            WeightHandler<T> weightHandler)
        {
            weightHandler.CheckCanUse(graph);

            _graph = graph;
            _weightHandler = weightHandler;

            _vertexInfo = new VertexInfo<T>();
            _depth = new Dictionary<long, int>();
            _contractionCount = new Dictionary<uint, int>();

            this.DifferenceFactor = 5;
            this.DepthFactor = 5;
            this.ContractedFactor = 5;
        }

        private BinaryHeap<uint> _queue; // the vertex-queue.
        private DirectedGraph _witnessGraph; // the graph with all the witnesses.
        protected BitArray32 _contractedFlags; // contains flags for contracted vertices.
        private int _k = 80; // The amount of queue 'misses' before recalculation of queue.
        private int _misses; // Holds a counter of all misses.
        private Queue<bool> _missesQueue; // Holds the misses queue.

        /// <summary>
        /// Gets or sets the difference factor.
        /// </summary>
        public int DifferenceFactor { get; set; }

        /// <summary>
        /// Gets or sets the depth factor.
        /// </summary>
        public int DepthFactor { get; set; }

        /// <summary>
        /// Gets or sets the contracted factor.
        /// </summary>
        public int ContractedFactor { get; set; }

        private Itinero.Algorithms.Contracted.Dual.Witness.NeighbourWitnessCalculator _witnessCalculator = null;

        private void InitializeWitnessGraph()
        {
            _logger.Log(TraceEventType.Information, "Initializing witness graph...");

            _witnessGraph = new DirectedGraph(1, _graph.VertexCount);
            _witnessCalculator = new Itinero.Algorithms.Contracted.Dual.Witness.NeighbourWitnessCalculator(
                _graph.Graph);
            var witnessCount = 0;
            for (uint v = 0; v < _graph.VertexCount; v++)
            {
                _witnessCalculator.Run(v, null, (v1, v2, w) =>
                {
                    if (w.Forward != float.MaxValue)
                    {
                        _witnessGraph.AddOrUpdateEdge(v1, v2, w.Forward);
                    }
                    if (w.Backward != float.MaxValue)
                    {
                        _witnessGraph.AddOrUpdateEdge(v2, v1, w.Backward);
                    }
                    witnessCount++;
                });
            }
        }

        /// <summary>
        /// Updates the vertex info object with the given vertex.
        /// </summary>
        /// <returns>True if witness paths have been found.</returns>
        private bool UpdateVertexInfo(uint v)
        {
            var contracted = 0;
            var depth = 0;

            // update vertex info.
            _vertexInfo.Clear();
            _vertexInfo.Vertex = v;
            _contractionCount.TryGetValue(v, out contracted);
            _vertexInfo.ContractedNeighbours = contracted;
            _depth.TryGetValue(v, out depth);
            _vertexInfo.Depth = depth;

            // calculate shortcuts and witnesses.
            _vertexInfo.AddRelevantEdges(_graph.GetEdgeEnumerator());
            _vertexInfo.BuildShortcuts(_weightHandler);

            // check if any of neighbours are in witness queue.
            if (_witnessQueue.Count > 0)
            {
                var c = 0;
                for (var i = 0; i < _vertexInfo.Count; i++)
                {
                    var m = _vertexInfo[i];
                    if (_witnessQueue.Contains(m.Neighbour))
                    {
                        c++;
                        if (c > 1)
                        {
                            this.DoWitnessQueue();
                            break;
                        }
                    }
                }
            }

            if (_vertexInfo.RemoveShortcuts(_witnessGraph, _weightHandler))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Excutes the actual run.
        /// </summary>
        protected override void DoRun()
        {
            _queue = new BinaryHeap<uint>((uint) _graph.VertexCount);
            _contractedFlags = new BitArray32(_graph.VertexCount); // is this strictly needed?
            _missesQueue = new Queue<bool>();

            this.InitializeWitnessGraph();

            this.CalculateQueue((uint) _graph.VertexCount);

            _logger.Log(TraceEventType.Information, "Started contraction...");
            this.SelectNext((uint) _graph.VertexCount);
            var latestProgress = 0f;
            var current = 0;
            var total = _graph.VertexCount;
            var toDoCount = total;
            while (_queue.Count > 0 ||
                toDoCount > 0)
            {
                // contract...
                this.Contract();

                // ... and select next.
                this.SelectNext((uint) _graph.VertexCount);

                // calculate and log progress.
                var progress = (float) (System.Math.Floor(((double) current / (double) total) * 10000) / 100.0);
                if (progress < 99)
                {
                    progress = (float) (System.Math.Floor(((double) current / (double) total) * 100) / 1.0);
                }
                if (progress != latestProgress)
                {
                    latestProgress = progress;

                    int totaEdges = 0;
                    int totalUncontracted = 0;
                    int maxCardinality = 0;
                    var neighbourCount = new Dictionary<uint, int>();
                    for (uint v = 0; v < _graph.VertexCount; v++)
                    {
                        if (!_contractedFlags[v])
                        {
                            neighbourCount.Clear();
                            var edges = _graph.GetEdgeEnumerator(v);
                            if (edges != null)
                            {
                                var edgesCount = edges.Count;
                                totaEdges = edgesCount + totaEdges;
                                if (maxCardinality < edgesCount)
                                {
                                    maxCardinality = edgesCount;
                                }
                            }
                            totalUncontracted++;
                        }
                    }

                    var density = (double) totaEdges / (double) totalUncontracted;
                    _logger.Log(TraceEventType.Information, "Preprocessing... {0}% [{1}/{2}] {3}q #{4} max {5}",
                        progress, current, total, _queue.Count, density, maxCardinality);
                }
                current++;
            }
        }

        /// <summary>
        /// Calculates the entire queue.
        /// </summary>
        private void CalculateQueue(uint size)
        {
            _logger.Log(TraceEventType.Information, "Calculating queue...");

            long witnessed = 0;
            long total = 0;
            _queue.Clear();
            for (uint v = 0; v < _graph.VertexCount; v++)
            {
                if (!_contractedFlags[v])
                {
                    // update vertex info.
                    if (this.UpdateVertexInfo(v))
                    {
                        witnessed++;
                    }
                    total++;

                    // calculate priority.
                    var priority = _vertexInfo.Priority(_graph, _weightHandler, this.DifferenceFactor, this.ContractedFactor, this.DepthFactor);

                    // queue vertex.
                    _queue.Push(v, priority);

                    if (_queue.Count >= size)
                    {
                        break;
                    }
                }
            }
            _logger.Log(TraceEventType.Information, "Queue calculated: {0}/{1} have witnesses.",
                witnessed, total);
        }

        /// <summary>
        /// Select the next vertex to contract.
        /// </summary>
        /// <returns></returns>
        protected virtual void SelectNext(uint queueSize)
        {
            // first check the first of the current queue.
            while (_queue.Count > 0)
            { // get the first vertex and check.
                var first = _queue.Peek();
                if (_contractedFlags[first])
                { // already contracted, priority was updated.
                    _queue.Pop();
                    continue;
                }
                var queuedPriority = _queue.PeekWeight();

                // the lazy updating part!
                // calculate priority
                this.UpdateVertexInfo(first);
                var priority = _vertexInfo.Priority(_graph, _weightHandler, this.DifferenceFactor, this.ContractedFactor,
                    this.DepthFactor);
                if (priority != queuedPriority)
                { // a succesfull update.
                    _missesQueue.Enqueue(true);
                    _misses++;
                }
                else
                { // an unsuccessfull update.
                    _missesQueue.Enqueue(false);
                }
                if (_missesQueue.Count > _k)
                { // dequeue and update the misses.
                    if (_missesQueue.Dequeue())
                    {
                        _misses--;
                    }
                }

                // if the misses are _k
                if (_misses == _k)
                { // recalculation.
                    this.CalculateQueue(queueSize);

                    // clear misses.
                    _missesQueue.Clear();
                    _misses = 0;
                }
                else
                { // recalculation.
                    if (priority != queuedPriority)
                    { // re-enqueue.
                        _queue.Pop();
                        _queue.Push(first, priority);
                    }
                    else
                    { // selection succeeded.
                        _queue.Pop();
                        return;
                    }
                }
            }
            return; // all nodes have been contracted.
        }

        private HashSet<uint> _witnessQueue = new HashSet<uint>();

        /// <summary>
        /// Contracts the given vertex.
        /// </summary>
        protected virtual void Contract()
        {
            var vertex = _vertexInfo.Vertex;

            // remove 'downward' edge to vertex.
            var i = 0;
            while (i < _vertexInfo.Count)
            {
                var edge = _vertexInfo[i];

                _graph.RemoveEdge(edge.Neighbour, vertex);
                i++;

                // TOOD: what to do when stuff is only removed, is nothing ok?
            }

            // add shortcuts.
            foreach (var s in _vertexInfo.Shortcuts)
            {
                var shortcut = s.Value;
                var edge = s.Key;

                if (edge.Vertex1 == edge.Vertex2)
                { // TODO: figure out how this is possible, it shouldn't!
                    continue;
                }

                var forwardMetric = _weightHandler.GetMetric(shortcut.Forward);
                var backwardMetric = _weightHandler.GetMetric(shortcut.Backward);

                if (forwardMetric > 0 && forwardMetric < float.MaxValue &&
                    backwardMetric > 0 && backwardMetric < float.MaxValue &&
                    System.Math.Abs(backwardMetric - forwardMetric) < HierarchyBuilder<float>.E)
                { // forward and backward and identical weights.
                    _weightHandler.AddOrUpdateEdge(_graph, edge.Vertex1, edge.Vertex2,
                        vertex, null, shortcut.Forward);
                    _weightHandler.AddOrUpdateEdge(_graph, edge.Vertex2, edge.Vertex1,
                        vertex, null, shortcut.Backward);
                    _witnessQueue.Add(edge.Vertex1);
                    _witnessQueue.Add(edge.Vertex2);
                }
                else
                {
                    if (forwardMetric > 0 && forwardMetric < float.MaxValue)
                    {
                        _weightHandler.AddOrUpdateEdge(_graph, edge.Vertex1, edge.Vertex2,
                            vertex, true, shortcut.Forward);
                        _weightHandler.AddOrUpdateEdge(_graph, edge.Vertex2, edge.Vertex1,
                            vertex, false, shortcut.Forward);
                        _witnessQueue.Add(edge.Vertex1);
                        _witnessQueue.Add(edge.Vertex2);
                    }
                    if (backwardMetric > 0 && backwardMetric < float.MaxValue)
                    {
                        _weightHandler.AddOrUpdateEdge(_graph, edge.Vertex1, edge.Vertex2,
                            vertex, false, shortcut.Backward);
                        _weightHandler.AddOrUpdateEdge(_graph, edge.Vertex2, edge.Vertex1,
                            vertex, true, shortcut.Backward);
                        _witnessQueue.Add(edge.Vertex1);
                        _witnessQueue.Add(edge.Vertex2);
                    }
                }
            }

            _contractedFlags[vertex] = true;
            this.NotifyContracted(vertex);
        }

        private void DoWitnessQueue()
        {
            if (_witnessQueue.Count > 0)
            {
                foreach (var v in _witnessQueue)
                {
                    _witnessCalculator.Run(v, _witnessQueue, (v1, v2, w) =>
                    {
                        if (w.Forward != float.MaxValue)
                        {
                            _witnessGraph.AddOrUpdateEdge(v1, v2, w.Forward);
                        }
                        if (w.Backward != float.MaxValue)
                        {
                            _witnessGraph.AddOrUpdateEdge(v2, v1, w.Backward);
                        }
                    });
                }
                _witnessQueue.Clear();
                if (_witnessGraph.EdgeSpaceCount > _witnessGraph.EdgeCount * 4)
                {
                    _witnessGraph.Compress();
                    _logger.Log(TraceEventType.Information, "Witnessgraph size: {0}", _witnessGraph.EdgeCount);
                }
            }
        }

        private DirectedMetaGraph.EdgeEnumerator _edgeEnumerator = null;

        /// <summary>
        /// Notifies this calculator that the given vertex was contracted.
        /// </summary>
        public void NotifyContracted(uint vertex)
        {
            // removes the contractions count.
            _contractionCount.Remove(vertex);

            // loop over all neighbours.
            if (_edgeEnumerator == null)
            {
                _edgeEnumerator = _graph.GetEdgeEnumerator();
            }
            _edgeEnumerator.MoveTo(vertex);

            int vertexDepth = 0;
            _depth.TryGetValue(vertex, out vertexDepth);
            _depth.Remove(vertex);
            vertexDepth++;

            // store the depth.
            _edgeEnumerator.Reset();
            while (_edgeEnumerator.MoveNext())
            {
                var neighbour = _edgeEnumerator.Neighbour;

                int depth = 0;
                _depth.TryGetValue(neighbour, out depth);
                if (vertexDepth >= depth)
                {
                    _depth[neighbour] = vertexDepth;
                }

                int count;
                if (!_contractionCount.TryGetValue(neighbour, out count))
                {
                    _contractionCount[neighbour] = 1;
                }
                else
                {
                    count++;
                    _contractionCount[neighbour] = count;
                }

                if (_witnessGraph.VertexCount > vertex &&
                    _witnessGraph.VertexCount > neighbour)
                {
                    _witnessGraph.RemoveEdge(neighbour, vertex);
                }
            }

            if (_witnessGraph.VertexCount > vertex)
            {
                _witnessGraph.RemoveEdges(vertex);
            }
        }
    }

    public static class DirectedGraphExtensions
    {
        public static void AddOrUpdateEdge(this DirectedGraph graph, uint vertex1, uint vertex2, float weight)
        {
            var data = ContractedEdgeDataSerializer.SerializeDistance(weight);
            if (graph.UpdateEdge(vertex1, vertex2, (d) =>
                {
                    var existingWeight = ContractedEdgeDataSerializer.DeserializeDistance(d[0]);
                    if (existingWeight > weight)
                    {
                        d[0] = data;
                        return true;
                    }
                    return false;
                }, data) == Constants.NO_EDGE)
            { // was not updated.
                graph.AddEdge(vertex1, vertex2, data);
            }
        }
    }
}