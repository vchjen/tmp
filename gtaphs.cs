using System;
using System.Collections.Generic;

namespace GraphShortestPathDemo
{
    /// <summary>
    /// Simple directed graph with adjacency list.
    /// Supports both unweighted (BFS) and weighted (Dijkstra) shortest path.
    /// Vertices are 0..(VertexCount-1).
    /// </summary>
    public sealed class Graph
    {
        private readonly List<(int To, int Weight)>[] _adj;

        public int VertexCount { get; }

        public Graph(int vertexCount)
        {
            if (vertexCount <= 0) throw new ArgumentOutOfRangeException(nameof(vertexCount));
            VertexCount = vertexCount;
            _adj = new List<(int To, int Weight)>[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                _adj[i] = new List<(int To, int Weight)>();
            }
        }

        /// <summary>
        /// Add a directed edge u -> v with given weight (default 1).
        /// </summary>
        public void AddEdge(int u, int v, int weight = 1)
        {
            if (u < 0 || u >= VertexCount) throw new ArgumentOutOfRangeException(nameof(u));
            if (v < 0 || v >= VertexCount) throw new ArgumentOutOfRangeException(nameof(v));
            if (weight < 0) throw new ArgumentOutOfRangeException(nameof(weight), "Dijkstra requires non-negative weights.");

            _adj[u].Add((v, weight));
        }

        /// <summary>
        /// Shortest path on an unweighted graph (or all weights=1).
        /// Uses BFS. Returns list of vertices from src to dest (inclusive),
        /// or null if no path.
        /// </summary>
        public List<int>? ShortestPathBfs(int src, int dest)
        {
            if (src < 0 || src >= VertexCount) throw new ArgumentOutOfRangeException(nameof(src));
            if (dest < 0 || dest >= VertexCount) throw new ArgumentOutOfRangeException(nameof(dest));

            var queue = new Queue<int>();
            var visited = new bool[VertexCount];
            var prev = new int[VertexCount];

            for (int i = 0; i < prev.Length; i++)
                prev[i] = -1;

            visited[src] = true;
            queue.Enqueue(src);

            while (queue.Count > 0)
            {
                int u = queue.Dequeue();
                if (u == dest)
                    break;

                foreach (var edge in _adj[u])
                {
                    int v = edge.To; // weight ignored for BFS
                    if (!visited[v])
                    {
                        visited[v] = true;
                        prev[v] = u;
                        queue.Enqueue(v);
                    }
                }
            }

            if (!visited[dest])
                return null; // no path

            return ReconstructPath(src, dest, prev);
        }

        /// <summary>
        /// Dijkstra's algorithm for non-negative weighted graphs.
        /// Returns (distance, path) or (int.MaxValue, null) if unreachable.
        /// </summary>
        public (int distance, List<int>? path) ShortestPathDijkstra(int src, int dest)
        {
            if (src < 0 || src >= VertexCount) throw new ArgumentOutOfRangeException(nameof(src));
            if (dest < 0 || dest >= VertexCount) throw new ArgumentOutOfRangeException(nameof(dest));

            var dist = new int[VertexCount];
            var prev = new int[VertexCount];
            var visited = new bool[VertexCount];

            for (int i = 0; i < VertexCount; i++)
            {
                dist[i] = int.MaxValue;
                prev[i] = -1;
            }

            dist[src] = 0;

            // .NET 6+ PriorityQueue<TElement, TPriority>
            var pq = new PriorityQueue<int, int>();
            pq.Enqueue(src, 0);

            while (pq.Count > 0)
            {
                pq.TryDequeue(out int u, out _);

                if (visited[u]) continue;
                visited[u] = true;

                if (u == dest)
                    break;

                foreach (var (v, w) in _adj[u])
                {
                    if (visited[v]) continue;

                    if (dist[u] != int.MaxValue && dist[u] + w < dist[v])
                    {
                        dist[v] = dist[u] + w;
                        prev[v] = u;
                        pq.Enqueue(v, dist[v]);
                    }
                }
            }

            if (dist[dest] == int.MaxValue)
                return (int.MaxValue, null);

            var path = ReconstructPath(src, dest, prev);
            return (dist[dest], path);
        }

        private static List<int> ReconstructPath(int src, int dest, int[] prev)
        {
            var path = new List<int>();
            int curr = dest;

            while (curr != -1)
            {
                path.Add(curr);
                if (curr == src)
                    break;
                curr = prev[curr];
            }

            path.Reverse();
            return path;
        }
    }

    internal static class Program
    {
        private static void Main()
        {
            // Example graph:
            // 0 -> 1 (1)
            // 0 -> 2 (4)
            // 1 -> 2 (2)
            // 1 -> 3 (5)
            // 2 -> 3 (1)

            var g = new Graph(4);
            g.AddEdge(0, 1, 1);
            g.AddEdge(0, 2, 4);
            g.AddEdge(1, 2, 2);
            g.AddEdge(1, 3, 5);
            g.AddEdge(2, 3, 1);

            Console.WriteLine("Unweighted shortest path (BFS) from 0 to 3:");
            var bfsPath = g.ShortestPathBfs(0, 3);
            if (bfsPath == null)
            {
                Console.WriteLine("No path.");
            }
            else
            {
                Console.WriteLine("Path: " + string.Join(" -> ", bfsPath));
                Console.WriteLine("Length (edges): " + (bfsPath.Count - 1));
            }

            Console.WriteLine();
            Console.WriteLine("Weighted shortest path (Dijkstra) from 0 to 3:");
            var (dist, dijkstraPath) = g.ShortestPathDijkstra(0, 3);
            if (dijkstraPath == null)
            {
                Console.WriteLine("No path.");
            }
            else
            {
                Console.WriteLine("Path: " + string.Join(" -> ", dijkstraPath));
                Console.WriteLine("Total weight: " + dist);
            }
        }
    }
}
