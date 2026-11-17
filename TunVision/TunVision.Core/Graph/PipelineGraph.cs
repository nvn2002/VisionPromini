using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TunVision.Core.Graph;

public class PipelineGraph
{
    public string Name { get; set; } = "Untitled Pipeline";
    public List<GraphNode> Nodes { get; set; } = new();
    public List<NodeConnection> Connections { get; set; } = new();

    /// <summary>
    /// When true, changing node properties (such as SourceImageNode image) automatically
    /// triggers re-execution of downstream inspection nodes. Default is true.
    /// </summary>
    public bool AutoExecute { get; set; } = true;

    [JsonIgnore]
    public bool IsExecuting { get; set; } = false;

    [JsonIgnore]
    public string? ProjectFilePath { get; set; }

    [JsonIgnore]
    public string? ProjectDirectory => !string.IsNullOrEmpty(ProjectFilePath) ? Path.GetDirectoryName(ProjectFilePath) : null;

    public event EventHandler<GraphExecutionResult>? GraphExecuted;

    [JsonIgnore]
    public Func<PipelineGraph, GraphExecutionContext?, Task<GraphExecutionResult>>? CustomAsyncExecutor { get; set; }

    public void AddNode(GraphNode node)
    {
        if (Nodes.Any(n => n.Id == node.Id))
            node.Id = Guid.NewGuid().ToString("N")[..8];
        node.Graph = this;
        node.UpdatePortNodeIds();
        Nodes.Add(node);
    }

    public void RemoveNode(string nodeId)
    {
        var removed = Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (removed != null) removed.Graph = null;
        Nodes.RemoveAll(n => n.Id == nodeId);
        Connections.RemoveAll(c => c.FromNodeId == nodeId || c.ToNodeId == nodeId);
    }

    public bool CanConnect(string fromNodeId, string fromPortId, string toNodeId, string toPortId)
    {
        if (fromNodeId == toNodeId) return false;
        var fromNode = Nodes.FirstOrDefault(n => n.Id == fromNodeId);
        var toNode = Nodes.FirstOrDefault(n => n.Id == toNodeId);
        if (fromNode == null || toNode == null) return false;

        var fromPort = fromNode.Outputs.FirstOrDefault(p => p.Id == fromPortId);
        var toPort = toNode.Inputs.FirstOrDefault(p => p.Id == toPortId);
        if (fromPort == null || toPort == null) return false;

        if (!fromPort.IsCompatibleWith(toPort)) return false;

        // Don't allow duplicate wire to same input port
        if (Connections.Any(c => c.ToNodeId == toNodeId && c.ToPortId == toPortId))
            return false;

        return true;
    }

    public bool Connect(string fromNodeId, string fromPortId, string toNodeId, string toPortId)
    {
        if (!CanConnect(fromNodeId, fromPortId, toNodeId, toPortId)) return false;
        Connections.Add(new NodeConnection(fromNodeId, fromPortId, toNodeId, toPortId));
        return true;
    }

    public void Disconnect(string fromNodeId, string fromPortId, string toNodeId, string toPortId)
    {
        Connections.RemoveAll(c => c.FromNodeId == fromNodeId && c.FromPortId == fromPortId &&
                                   c.ToNodeId == toNodeId && c.ToPortId == toPortId);
    }

    private readonly object _autoExecLock = new();
    private bool _pendingAutoExecute = false;

    /// <summary>
    /// Triggers automatic pipeline re-execution when a node changes state (e.g. source image switch).
    /// In UI context (CustomAsyncExecutor != null), dispatches via custom executor.
    /// In direct / test context, executes downstream nodes synchronously so callers observe updated state immediately.
    /// </summary>
    public void TriggerAutoExecute(GraphNode? triggerNode = null)
    {
        if (!AutoExecute) return;

        if (CustomAsyncExecutor != null)
        {
            // Custom executor (e.g. MainViewModel) handles UI synchronization and debounced queuing
            _ = CustomAsyncExecutor(this, null);
            return;
        }

        lock (_autoExecLock)
        {
            if (IsExecuting)
            {
                _pendingAutoExecute = true;
                return;
            }
            IsExecuting = true;
        }

        try
        {
            do
            {
                lock (_autoExecLock)
                {
                    _pendingAutoExecute = false;
                }
                if (triggerNode != null)
                {
                    ExecuteDownstream(triggerNode);
                }
                else
                {
                    Execute();
                }
            } while (_pendingAutoExecute);
        }
        finally
        {
            lock (_autoExecLock)
            {
                IsExecuting = false;
            }
        }
    }

    /// <summary>
    /// Synchronously executes the pipeline DAG using GraphExecutor.
    /// </summary>
    public GraphExecutionResult Execute(GraphExecutionContext? context = null)
    {
        var executor = new GraphExecutor();
        var res = executor.ExecuteAsync(this, context).GetAwaiter().GetResult();
        GraphExecuted?.Invoke(this, res);
        return res;
    }

    /// <summary>
    /// Asynchronously executes the pipeline DAG using GraphExecutor.
    /// </summary>
    public async Task<GraphExecutionResult> ExecuteAsync(GraphExecutionContext? context = null)
    {
        var executor = new GraphExecutor();
        var res = await executor.ExecuteAsync(this, context);
        GraphExecuted?.Invoke(this, res);
        return res;
    }

    /// <summary>
    /// Returns all downstream nodes connected directly or transitively from startNode,
    /// ordered by topological execution order.
    /// </summary>
    public List<GraphNode> GetDownstreamNodes(GraphNode startNode)
    {
        var visited = new HashSet<string> { startNode.Id };
        var queue = new Queue<string>();
        queue.Enqueue(startNode.Id);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var conn in Connections.Where(c => c.FromNodeId == cur))
            {
                if (visited.Add(conn.ToNodeId))
                {
                    queue.Enqueue(conn.ToNodeId);
                }
            }
        }

        var fullOrder = GetExecutionOrder();
        return fullOrder.Where(n => visited.Contains(n.Id)).ToList();
    }

    /// <summary>
    /// Re-executes the downstream nodes originating from startNode.
    /// </summary>
    public GraphExecutionResult ExecuteDownstream(GraphNode startNode, GraphExecutionContext? context = null)
    {
        var executor = new GraphExecutor();
        var downstream = GetDownstreamNodes(startNode);
        var res = executor.ExecuteAsync(this, context, downstream).GetAwaiter().GetResult();
        GraphExecuted?.Invoke(this, res);
        return res;
    }

    /// <summary>
    /// Asynchronously re-executes the downstream nodes originating from startNode.
    /// </summary>
    public async Task<GraphExecutionResult> ExecuteDownstreamAsync(GraphNode startNode, GraphExecutionContext? context = null)
    {
        var executor = new GraphExecutor();
        var downstream = GetDownstreamNodes(startNode);
        var res = await executor.ExecuteAsync(this, context, downstream);
        GraphExecuted?.Invoke(this, res);
        return res;
    }

    /// <summary>
    /// Topological Sort (Kahn's Algorithm) to determine the exact execution sequence.
    /// </summary>
    public List<GraphNode> GetExecutionOrder()
    {
        foreach (var n in Nodes) n.Graph = this;

        var nodeMap = Nodes.ToDictionary(n => n.Id);
        var inDegree = Nodes.ToDictionary(n => n.Id, _ => 0);
        var adjList = Nodes.ToDictionary(n => n.Id, _ => new List<string>());

        foreach (var c in Connections)
        {
            if (nodeMap.ContainsKey(c.FromNodeId) && nodeMap.ContainsKey(c.ToNodeId))
            {
                adjList[c.FromNodeId].Add(c.ToNodeId);
                inDegree[c.ToNodeId]++;
            }
        }

        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var order = new List<GraphNode>();

        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            if (nodeMap.TryGetValue(u, out var n))
                order.Add(n);

            foreach (var v in adjList[u])
            {
                inDegree[v]--;
                if (inDegree[v] == 0)
                    queue.Enqueue(v);
            }
        }

        // If cycle or disconnected nodes, append any remaining nodes
        foreach (var n in Nodes)
        {
            if (!order.Contains(n))
                order.Add(n);
        }

        return order;
    }

    /// <summary>
    /// Builds a pre-configured turnkey inspection pipeline (ForgeStation DAG style with MWWZ Shape Matching).
    /// </summary>
    public static PipelineGraph CreateDefaultInspectionPipeline()
    {
        var graph = new PipelineGraph { Name = "Retainer & Bearing Inspection" };

        var srcNode = new SourceImageNode
        {
            Id = "src_01",
            PositionX = 40,
            PositionY = 120
        };

        var matchNode = new ShapeMatchNode
        {
            Id = "match_01",
            MinScore = 0.5f,
            ModelPath = "retainers_model.shm",
            TemplateImagePath = "retainers_01.png",
            PositionX = 280,
            PositionY = 120
        };

        var fixNode = new FixtureNode
        {
            Id = "fix_01",
            PositionX = 520,
            PositionY = 120
        };

        var circleNode = new CaliperCircleNode
        {
            Id = "circle_01",
            PositionX = 740,
            PositionY = 120
        };

        var verdictNode = new ToleranceVerdictNode
        {
            Id = "verdict_01",
            Nominal = 50.0,
            MinTolerance = 45.0,
            MaxTolerance = 55.0,
            PositionX = 960,
            PositionY = 120
        };

        var displayNode = new DisplayOutputNode
        {
            Id = "out_01",
            PositionX = 1180,
            PositionY = 120
        };

        graph.AddNode(srcNode);
        graph.AddNode(matchNode);
        graph.AddNode(fixNode);
        graph.AddNode(circleNode);
        graph.AddNode(verdictNode);
        graph.AddNode(displayNode);

        // Wire connections:
        // Source -> Match (image)
        graph.Connect("src_01", "image", "match_01", "image");
        // Match -> Fixture (pose)
        graph.Connect("match_01", "pose", "fix_01", "pose");
        // Source -> Caliper (image)
        graph.Connect("src_01", "image", "circle_01", "image");
        // Fixture -> Caliper (fixture)
        graph.Connect("fix_01", "fixture", "circle_01", "fixture");
        // Caliper -> Verdict (measurement/score)
        graph.Connect("circle_01", "measurement", "verdict_01", "score");
        // Verdict -> Display (verdict)
        graph.Connect("verdict_01", "verdict", "out_01", "value");

        return graph;
    }

    /// <summary>
    /// Creates an empty blank pipeline canvas for building custom inspection workflows from scratch.
    /// </summary>
    public static PipelineGraph CreateBlankPipeline()
    {
        return new PipelineGraph { Name = "Blank Pipeline" };
    }

    /// <summary>
    /// Creates a pre-configured multi-instance Shape Detection workflow (Template -> Train Shape Model -> Shape Match -> Count Verdict -> Display).
    /// </summary>
    public static PipelineGraph CreateShapeDetectionPipeline()
    {
        var graph = new PipelineGraph { Name = "Shape Detection & Counting Tool" };

        var sceneSrc = new SourceImageNode
        {
            Id = "src_scene",
            PositionX = 40,
            PositionY = 60
        };
        sceneSrc.SelectSampleByFileName("cocoa_packages_05.png");

        var tplSrc = new SourceImageNode
        {
            Id = "src_tpl",
            PositionX = 40,
            PositionY = 260
        };
        tplSrc.SelectSampleByFileName("cocoa_package_model.png");

        var trainNode = new TrainShapeModelNode
        {
            Id = "train_01",
            PositionX = 280,
            PositionY = 260
        };

        var matchNode = new ShapeMatchNode
        {
            Id = "match_01",
            MinScore = 0.5f,
            MaxInstances = 5,
            ScaleMin = 0.9f,
            ScaleMax = 1.1f,
            PositionX = 530,
            PositionY = 100
        };

        var verdictNode = new ToleranceVerdictNode
        {
            Id = "verdict_01",
            Nominal = 3.0,
            MinTolerance = 1.0,
            MaxTolerance = 10.0,
            PositionX = 790,
            PositionY = 100
        };

        var displayNode = new DisplayOutputNode
        {
            Id = "out_01",
            PositionX = 1030,
            PositionY = 100
        };

        graph.AddNode(sceneSrc);
        graph.AddNode(tplSrc);
        graph.AddNode(trainNode);
        graph.AddNode(matchNode);
        graph.AddNode(verdictNode);
        graph.AddNode(displayNode);

        // Wire template -> train -> match
        graph.Connect("src_tpl", "image", "train_01", "image");
        graph.Connect("train_01", "model", "match_01", "model");
        // Wire scene -> match -> verdict & display
        graph.Connect("src_scene", "image", "match_01", "image");
        graph.Connect("match_01", "count", "verdict_01", "score");
        graph.Connect("match_01", "image", "out_01", "value");

        return graph;
    }

    /// <summary>
    /// Creates a pre-configured Blob Detection & Counting workflow.
    /// </summary>
    public static PipelineGraph CreateBlobInspectionPipeline()
    {
        var graph = new PipelineGraph { Name = "Blob Analysis & Counting Tool" };

        var srcNode = new SourceImageNode
        {
            Id = "src_01",
            PositionX = 60,
            PositionY = 140
        };
        srcNode.SelectSampleByFileName("rings_01.png");

        var blobNode = new BlobDetectNode
        {
            Id = "blob_01",
            PositionX = 320,
            PositionY = 140
        };

        var verdictNode = new ToleranceVerdictNode
        {
            Id = "verdict_01",
            Nominal = 3.0,
            MinTolerance = 1.0,
            MaxTolerance = 10.0,
            PositionX = 580,
            PositionY = 140
        };

        var displayNode = new DisplayOutputNode
        {
            Id = "out_01",
            PositionX = 840,
            PositionY = 140
        };

        graph.AddNode(srcNode);
        graph.AddNode(blobNode);
        graph.AddNode(verdictNode);
        graph.AddNode(displayNode);

        graph.Connect("src_01", "image", "blob_01", "image");
        graph.Connect("blob_01", "count", "verdict_01", "score");
        graph.Connect("blob_01", "image", "out_01", "value");

        return graph;
    }

    /// <summary>
    /// Creates a pre-configured 1D/2D Code Reading workflow (ZXing QR & Barcode).
    /// </summary>
    public static PipelineGraph CreateCodeReadingPipeline()
    {
        var graph = new PipelineGraph { Name = "1D & 2D Code Reading (ZXing)" };

        var srcNode = new SourceImageNode
        {
            Id = "src_01",
            PositionX = 60,
            PositionY = 140
        };
        srcNode.SelectSampleByFileName("sample_qr.png");

        var qrNode = new ScanQrNode
        {
            Id = "qr_01",
            PositionX = 320,
            PositionY = 140
        };

        var verdictNode = new ToleranceVerdictNode
        {
            Id = "verdict_01",
            Nominal = 1.0,
            MinTolerance = 0.5,
            MaxTolerance = 1.5,
            PositionX = 580,
            PositionY = 140
        };

        var displayNode = new DisplayOutputNode
        {
            Id = "out_01",
            PositionX = 840,
            PositionY = 140
        };

        graph.AddNode(srcNode);
        graph.AddNode(qrNode);
        graph.AddNode(verdictNode);
        graph.AddNode(displayNode);

        graph.Connect("src_01", "image", "qr_01", "image");
        graph.Connect("qr_01", "score", "verdict_01", "score");
        graph.Connect("qr_01", "image", "out_01", "value");

        return graph;
    }

    /// <summary>
    /// Creates a comprehensive Smart Vision Metrology workflow (matching the reference video:
    /// Source -> Contour Match [Frame] -> Search Circle + Search Corner -> Point-to-Point Distance -> Multi-Tolerance Verdict).
    /// </summary>
    public static PipelineGraph CreateCompleteMetrologyPipeline()
    {
        var graph = new PipelineGraph { Name = "Smart Vision Metrology (Circle + Corner + Distance)" };

        var srcNode = new SourceImageNode
        {
            Id = "src_01",
            PositionX = 40,
            PositionY = 140
        };
        srcNode.SelectSampleByFileName("retainers_01.png");

        var matchNode = new ShapeMatchNode
        {
            Id = "match_01",
            MinScore = 0.5f,
            ModelPath = "retainers_model.shm",
            TemplateImagePath = "retainers_01.png",
            ReferenceOriginX = 272f,
            ReferenceOriginY = 242f,
            PositionX = 270,
            PositionY = 100
        };

        var circleNode = new CaliperCircleNode
        {
            Id = "circle_01",
            CenterX = 272f,
            CenterY = 242f,
            ExpectedRadius = 56f,
            SearchRange = 22f,
            NumSpokes = 36,
            PositionX = 530,
            PositionY = 40
        };

        var cornerNode = new SearchCornerNode
        {
            Id = "corner_01",
            Line1StartX = 200f,
            Line1StartY = 175f,
            Line1EndX = 340f,
            Line1EndY = 175f,
            Line2StartX = 205f,
            Line2StartY = 180f,
            Line2EndX = 205f,
            Line2EndY = 305f,
            SearchHalfWidth = 30f,
            PositionX = 530,
            PositionY = 250
        };

        var p2pNode = new PointToPointNode
        {
            Id = "p2p_01",
            PositionX = 790,
            PositionY = 140
        };

        var verdictNode = new ToleranceVerdictNode
        {
            Id = "verdict_01",
            Nominal = 95.0,
            MinTolerance = 40.0,
            MaxTolerance = 180.0,
            PositionX = 1040,
            PositionY = 140
        };

        graph.AddNode(srcNode);
        graph.AddNode(matchNode);
        graph.AddNode(circleNode);
        graph.AddNode(cornerNode);
        graph.AddNode(p2pNode);
        graph.AddNode(verdictNode);

        // Source -> Match, Circle, Corner, P2P
        graph.Connect("src_01", "image", "match_01", "image");
        graph.Connect("src_01", "image", "circle_01", "image");
        graph.Connect("src_01", "image", "corner_01", "image");
        graph.Connect("src_01", "image", "p2p_01", "image");

        // Match Frame -> Circle & Corner (automatic coordinate tracking!)
        graph.Connect("match_01", "frame", "circle_01", "fixture");
        graph.Connect("match_01", "frame", "corner_01", "fixture");

        // Circle Center & Corner Point -> Point-to-Point Distance
        graph.Connect("circle_01", "center", "p2p_01", "point_a");
        graph.Connect("corner_01", "corner", "p2p_01", "point_b");

        // Distance -> Verdict
        graph.Connect("p2p_01", "dist", "verdict_01", "score");

        return graph;
    }

    private sealed class PipelineSaveModel
    {
        public string Name { get; set; } = string.Empty;
        public List<SavedNodeEntry> Nodes { get; set; } = new();
        public List<NodeConnection> Connections { get; set; } = new();
    }

    private sealed class SavedNodeEntry
    {
        public string TypeName { get; set; } = string.Empty;
        public string JsonPayload { get; set; } = string.Empty;
    }

    public void SaveToFile(string filePath)
    {
        ProjectFilePath = filePath;
        var dto = new PipelineSaveModel
        {
            Name = Name,
            Connections = Connections.ToList()
        };

        var opts = new JsonSerializerOptions { WriteIndented = true };
        foreach (var node in Nodes)
        {
            dto.Nodes.Add(new SavedNodeEntry
            {
                TypeName = node.GetType().FullName ?? node.GetType().Name,
                JsonPayload = JsonSerializer.Serialize(node, node.GetType(), opts)
            });
        }

        File.WriteAllText(filePath, JsonSerializer.Serialize(dto, opts));
    }

    public static PipelineGraph LoadFromFile(string filePath)
    {
        string json = File.ReadAllText(filePath);
        var dto = JsonSerializer.Deserialize<PipelineSaveModel>(json)
            ?? throw new InvalidDataException("Invalid pipeline file.");

        var graph = new PipelineGraph
        {
            Name = dto.Name,
            ProjectFilePath = filePath
        };
        var asm = typeof(PipelineGraph).Assembly;

        // Directory containing the .tvproj file — use for relative path resolution
        string projDir = Path.GetDirectoryName(filePath) ?? string.Empty;

        foreach (var entry in dto.Nodes)
        {
            var type = asm.GetType(entry.TypeName);
            if (type != null && JsonSerializer.Deserialize(entry.JsonPayload, type) is GraphNode node)
            {
                // Restore default ports from constructor while keeping serialized properties & Id
                if (Activator.CreateInstance(type) is GraphNode templateNode)
                {
                    node.Inputs = templateNode.Inputs;
                    node.Outputs = templateNode.Outputs;
                }
                node.UpdatePortNodeIds();
                node.Graph = graph;

                // Resolve relative/broken ImagePath for SourceImageNode
                if (node is SourceImageNode srcNode)
                {
                    string ip = srcNode.ImagePath;
                    string? resolved = PathResolver.ResolveImagePath(ip, projDir, srcNode.FolderPath);
                    if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
                    {
                        srcNode.SetImageFile(resolved);
                    }
                    else
                    {
                        // Also resolve FolderPath if it no longer exists
                        string fp = srcNode.FolderPath;
                        if (!string.IsNullOrEmpty(fp) && !Directory.Exists(fp))
                        {
                            string imgSubDir = Path.Combine(projDir, "image");
                            if (Directory.Exists(imgSubDir))
                                srcNode.SetFolder(imgSubDir);
                            else if (Directory.Exists(projDir))
                                srcNode.SetFolder(projDir);
                        }
                    }
                }

                graph.Nodes.Add(node);
            }
        }

        graph.Connections = dto.Connections ?? new List<NodeConnection>();
        return graph;
    }
}
