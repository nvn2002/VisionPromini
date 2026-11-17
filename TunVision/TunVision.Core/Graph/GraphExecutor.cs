using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using OpenCvSharp;

namespace TunVision.Core.Graph;

public class GraphExecutionResult
{
    public bool Success { get; set; } = true;
    public double TotalExecutionTimeMs { get; set; }
    public VerdictResult? OverallVerdict { get; set; }
    public List<GraphNode> ExecutedNodes { get; set; } = new();
    public Mat? FinalPreviewMat { get; set; }
    public Mat? CompositePreviewMat { get; set; }
    public Mat? RootSceneMat { get; set; }

    /// <summary>NodeId -> id of the SourceImageNode feeding it (directly or transitively).</summary>
    public Dictionary<string, string> NodeSourceIds { get; } = new();
    /// <summary>SourceImageNode id -> its raw scene image.</summary>
    public Dictionary<string, Mat> SourceScenes { get; } = new();
    /// <summary>SourceImageNode id -> scene with overlays of only its downstream nodes.</summary>
    public Dictionary<string, Mat> SourceComposites { get; } = new();
    public string SummaryText { get; set; } = string.Empty;
}

public class GraphExecutor
{
    public async Task<GraphExecutionResult> ExecuteAsync(
        PipelineGraph graph,
        GraphExecutionContext? context = null,
        IEnumerable<GraphNode>? targetNodes = null)
    {
        return await Task.Run(async () =>
        {
            context ??= new GraphExecutionContext();
            context.GlobalVariables["__graph"] = graph;

            bool prevExecuting = graph.IsExecuting;
            graph.IsExecuting = true;

            try
            {
                var result = new GraphExecutionResult();
                var swTotal = Stopwatch.StartNew();

                var fullOrder = graph.GetExecutionOrder();
                foreach (var n in fullOrder) n.Graph = graph;

                var executionOrder = targetNodes != null
                    ? fullOrder.Where(n => targetNodes.Any(t => t.Id == n.Id)).ToList()
                    : fullOrder;

                var nodeOutputs = new Dictionary<string, Dictionary<string, object?>>();

                Mat? rootSceneMat = null;
                Mat? latestMat = null;
                VerdictResult? latestVerdict = null;

                foreach (var node in executionOrder)
                {
                    var nodeInputs = new Dictionary<string, object?>();

                    // Collect inputs from incoming connections
                    var incomingWires = graph.Connections.Where(c => c.ToNodeId == node.Id).ToList();
                    foreach (var wire in incomingWires)
                    {
                        if (nodeOutputs.TryGetValue(wire.FromNodeId, out var parentOutputs))
                        {
                            if (parentOutputs.TryGetValue(wire.FromPortId, out var val))
                            {
                                nodeInputs[wire.ToPortId] = val;
                            }

                            // Smart ShapeModel resolution: if target port is "model", prefer trained ShapeModel from parent
                            // even if the user accidentally connected from the "image" (Contour Img) output pin!
                            if (wire.ToPortId == "model" && parentOutputs.TryGetValue("model", out var pModel) && pModel != null)
                            {
                                nodeInputs["model"] = pModel;
                            }
                        }
                        else
                        {
                            // Upstream node not in current execution batch: use its preserved outputs
                            var parentNode = graph.Nodes.FirstOrDefault(n => n.Id == wire.FromNodeId);
                            if (parentNode != null)
                            {
                                if (parentNode.LastOutputs.TryGetValue(wire.FromPortId, out var val) && val != null)
                                {
                                    nodeInputs[wire.ToPortId] = val;
                                }
                                else if (wire.FromPortId == "image" && parentNode.LastOutputMat != null)
                                {
                                    nodeInputs[wire.ToPortId] = parentNode.LastOutputMat;
                                }
                                else if (parentNode.LastResult != null)
                                {
                                    nodeInputs[wire.ToPortId] = parentNode.LastResult;
                                }

                                if (wire.ToPortId == "model" && parentNode.LastOutputs.TryGetValue("model", out var pModel) && pModel != null)
                                {
                                    nodeInputs["model"] = pModel;
                                }
                            }
                        }
                    }

                    var swNode = Stopwatch.StartNew();
                    try
                    {
                        var outputs = await node.ExecuteAsync(nodeInputs, context);
                        swNode.Stop();
                        node.ExecutionTimeMs = swNode.Elapsed.TotalMilliseconds;
                        node.LastOutputs.Clear();
                        if (outputs != null)
                        {
                            foreach (var kv in outputs) node.LastOutputs[kv.Key] = kv.Value;
                        }
                        if (node is SourceImageNode src && src.LastOutputMat != null && !src.LastOutputMat.Empty())
                            node.Status = "OK";
                        else if (string.IsNullOrEmpty(node.Status) || node.Status == "Ready")
                            node.Status = "OK";
                        nodeOutputs[node.Id] = outputs ?? new Dictionary<string, object?>();

                        if (rootSceneMat == null && node is SourceImageNode && node.LastOutputMat != null && !node.LastOutputMat.Empty())
                        {
                            rootSceneMat = node.LastOutputMat;
                        }

                        if (node.LastOutputMat != null && !node.LastOutputMat.Empty())
                        {
                            latestMat = node.LastOutputMat;
                        }

                        if (outputs != null && outputs.Values.OfType<VerdictResult>().FirstOrDefault() is { } v)
                        {
                            latestVerdict = v;
                        }
                    }
                    catch (Exception ex)
                    {
                        swNode.Stop();
                        node.ExecutionTimeMs = swNode.Elapsed.TotalMilliseconds;
                        node.Status = $"Error: {ex.Message}";
                        node.LastResult = $"Error: {ex.Message}";
                        result.Success = false;
                    }

                    result.ExecutedNodes.Add(node);
                }

                // Per-source scenes: each SourceImageNode gets its own scene + composite containing only the nodes wired downstream of it.
                foreach (var node in executionOrder)
                {
                    var srcId = FindUpstreamSourceId(graph, node, new HashSet<string>());
                    if (srcId != null) result.NodeSourceIds[node.Id] = srcId;
                }

                foreach (var srcNode in executionOrder.OfType<SourceImageNode>())
                {
                    if (srcNode.LastOutputMat == null || srcNode.LastOutputMat.Empty()) continue;

                    // If an ImageFilterNode is downstream of this source, use its filtered output as the base scene for downstream overlays
                    var filterNode = executionOrder
                        .OfType<ImageFilterNode>()
                        .LastOrDefault(fn => result.NodeSourceIds.TryGetValue(fn.Id, out var sid) && sid == srcNode.Id && fn.LastOutputMat != null && !fn.LastOutputMat.Empty());

                    Mat baseScene = filterNode?.LastOutputMat ?? srcNode.LastOutputMat;
                    result.SourceScenes[srcNode.Id] = baseScene;
                    if (rootSceneMat == srcNode.LastOutputMat && filterNode?.LastOutputMat != null)
                    {
                        rootSceneMat = filterNode.LastOutputMat;
                    }

                    var comp = baseScene.Channels() == 1
                        ? baseScene.CvtColor(ColorConversionCodes.GRAY2BGR)
                        : baseScene.Clone();
                    foreach (var node in executionOrder)
                    {
                        if (node is TrainShapeModelNode or ShapeMatchNode) continue;
                        if (!result.NodeSourceIds.TryGetValue(node.Id, out var nid) || nid != srcNode.Id) continue;
                        try { node.DrawOverlay(comp); } catch { }
                    }
                    result.SourceComposites[srcNode.Id] = comp;
                }

                // Build composite multi-node overlay on top of rootSceneMat (matching Smart Vision video)
                Mat? compositeMat = null;
                if (rootSceneMat != null && !rootSceneMat.Empty())
                {
                    compositeMat = rootSceneMat.Channels() == 1
                        ? rootSceneMat.CvtColor(ColorConversionCodes.GRAY2BGR)
                        : rootSceneMat.Clone();

                    foreach (var node in executionOrder)
                    {
                        if (node is TrainShapeModelNode or ShapeMatchNode) continue; // Train & Match overlays are rendered vectorially in VisionCanvas
                        try
                        {
                            node.DrawOverlay(compositeMat);
                        }
                        catch { }
                    }
                }

                swTotal.Stop();
                result.TotalExecutionTimeMs = swTotal.Elapsed.TotalMilliseconds;
                result.OverallVerdict = latestVerdict;
                result.RootSceneMat = rootSceneMat;
                result.CompositePreviewMat = compositeMat ?? latestMat;
                result.FinalPreviewMat = compositeMat ?? latestMat;

                result.SummaryText = $"Executed {executionOrder.Count} nodes in {result.TotalExecutionTimeMs:F1}ms. " +
                                     $"Verdict: {(latestVerdict?.State == VerdictState.Pass ? "PASS" : latestVerdict?.State == VerdictState.Defect ? "DEFECT" : "N/A")}";

                return result;
            }
            finally
            {
                graph.IsExecuting = prevExecuting;
            }
        });
    }

    private static string? FindUpstreamSourceId(PipelineGraph graph, GraphNode node, HashSet<string> visited)
    {
        if (node is SourceImageNode) return node.Id;
        if (!visited.Add(node.Id)) return null;

        // Prefer image-wired parents first, then any other wire
        var wires = graph.Connections.Where(c => c.ToNodeId == node.Id)
            .OrderBy(c => c.ToPortId == "image" ? 0 : 1).ToList();
        foreach (var w in wires)
        {
            var parent = graph.Nodes.FirstOrDefault(n => n.Id == w.FromNodeId);
            if (parent == null) continue;
            var found = FindUpstreamSourceId(graph, parent, visited);
            if (found != null) return found;
        }
        return null;
    }
}
