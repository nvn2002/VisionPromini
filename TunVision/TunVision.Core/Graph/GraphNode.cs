using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using OpenCvSharp;

namespace TunVision.Core.Graph;

public class GraphExecutionContext
{
    public Dictionary<string, object?> GlobalVariables { get; } = new();
    public string RootImagePath { get; set; } = string.Empty;
    public doubleMmScale PxToMmScale { get; set; } = new(1.0, "px");
}

public record doubleMmScale(double MmPerPixel, string UnitName = "px");

public record NodeMeasurementEntry(string Key, string DisplayValue, double? NumericValue = null);

/// <summary>
/// Base class for all nodes in the DAG inspection pipeline.
/// Also accessible as PipelineNode for pipeline graph compatibility.
/// </summary>
public abstract class GraphNode : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    [JsonIgnore]
    public PipelineGraph? Graph { get; set; }

    [JsonIgnore]
    public PipelineGraph? ParentGraph
    {
        get => Graph;
        set => Graph = value;
    }

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "Node";
    public string Category { get; set; } = "General";
    public double PositionX { get; set; } = 100;
    public double PositionY { get; set; } = 100;

    public List<NodePort> Inputs { get; set; } = new();
    public List<NodePort> Outputs { get; set; } = new();

    private double _executionTimeMs;
    [JsonIgnore]
    public double ExecutionTimeMs
    {
        get => _executionTimeMs;
        set
        {
            if (Math.Abs(_executionTimeMs - value) > 0.001)
            {
                _executionTimeMs = value;
                OnPropertyChanged();
            }
        }
    }

    private string _status = "Ready";
    [JsonIgnore]
    public string Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
            }
        }
    }

    private object? _lastResult;
    [JsonIgnore]
    public object? LastResult
    {
        get => _lastResult;
        set
        {
            if (!Equals(_lastResult, value))
            {
                _lastResult = value;
                OnPropertyChanged();
            }
        }
    }

    private Mat? _lastOutputMat;
    [JsonIgnore]
    public Mat? LastOutputMat
    {
        get => _lastOutputMat;
        set
        {
            if (_lastOutputMat != value)
            {
                _lastOutputMat = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Preserved dictionary of all output pin values produced by the latest execution.
    /// Enables downstream nodes to retrieve typed values (e.g. FixtureTransform, ShapeModel, double score).
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, object?> LastOutputs { get; } = new();

    [JsonIgnore]
    public List<NodeMeasurementEntry> LastMeasurements { get; } = new();

    [JsonIgnore]
    public Dictionary<string, double> NumericMeasurements { get; } = new();

    protected void ClearMeasurements()
    {
        LastMeasurements.Clear();
        NumericMeasurements.Clear();
    }

    protected void RecordMeasurement(string key, double val, string format = "F2", string suffix = "")
    {
        NumericMeasurements[key] = val;
        string txt = string.IsNullOrEmpty(suffix) ? val.ToString(format) : $"{val.ToString(format)} {suffix}";
        LastMeasurements.Add(new NodeMeasurementEntry(key, txt, val));
    }

    protected void RecordTextMeasurement(string key, string displayVal)
    {
        LastMeasurements.Add(new NodeMeasurementEntry(key, displayVal, null));
    }

    /// <summary>
    /// Draws this node's visual overlay onto a shared composite canvas image.
    /// </summary>
    public virtual void DrawOverlay(Mat canvas)
    {
    }

    protected GraphNode(string title, string category)
    {
        Title = title;
        Category = category;
    }

    protected NodePort AddInput(string id, string name, PortKind kind)
    {
        var port = new NodePort(id, name, kind, PortDirection.Input, Id);
        Inputs.Add(port);
        return port;
    }

    protected NodePort AddOutput(string id, string name, PortKind kind)
    {
        var port = new NodePort(id, name, kind, PortDirection.Output, Id);
        Outputs.Add(port);
        return port;
    }

    public void UpdatePortNodeIds()
    {
        foreach (var p in Inputs) p.NodeId = Id;
        foreach (var p in Outputs) p.NodeId = Id;
    }

    public abstract Task<Dictionary<string, object?>> ExecuteAsync(
        Dictionary<string, object?> inputs,
        GraphExecutionContext context);

    /// <summary>
    /// Synchronously executes this node with the provided inputs.
    /// </summary>
    public virtual Dictionary<string, object?> Execute(
        Dictionary<string, object?> inputs,
        GraphExecutionContext? context = null)
    {
        var outputs = ExecuteAsync(inputs, context ?? new GraphExecutionContext()).GetAwaiter().GetResult();
        SaveOutputs(outputs);
        return outputs;
    }

    /// <summary>
    /// Asynchronously executes this node using inputs collected from its parent PipelineGraph.
    /// </summary>
    public virtual async Task<Dictionary<string, object?>> ExecuteAsync(GraphExecutionContext? context = null)
    {
        var inputs = CollectInputsFromGraph();
        var outputs = await ExecuteAsync(inputs, context ?? new GraphExecutionContext());
        SaveOutputs(outputs);
        return outputs;
    }

    /// <summary>
    /// Synchronously executes this node using inputs collected from its parent PipelineGraph.
    /// </summary>
    public virtual Dictionary<string, object?> Execute(GraphExecutionContext? context = null)
    {
        return ExecuteAsync(context).GetAwaiter().GetResult();
    }

    private void SaveOutputs(Dictionary<string, object?>? outputs)
    {
        LastOutputs.Clear();
        if (outputs != null)
        {
            foreach (var kv in outputs) LastOutputs[kv.Key] = kv.Value;
        }
    }

    /// <summary>
    /// Helper to resolve input pins from connected upstream nodes in the pipeline graph.
    /// </summary>
    protected virtual Dictionary<string, object?> CollectInputsFromGraph()
    {
        var nodeInputs = new Dictionary<string, object?>();
        if (Graph != null)
        {
            var incomingWires = Graph.Connections.Where(c => c.ToNodeId == Id).ToList();
            foreach (var wire in incomingWires)
            {
                var parent = Graph.Nodes.FirstOrDefault(n => n.Id == wire.FromNodeId);
                if (parent != null)
                {
                    if (parent.LastOutputs.TryGetValue(wire.FromPortId, out var val) && val != null)
                    {
                        nodeInputs[wire.ToPortId] = val;
                    }
                    else if (wire.FromPortId == "image" && parent.LastOutputMat != null)
                    {
                        nodeInputs[wire.ToPortId] = parent.LastOutputMat;
                    }
                    else if (parent.LastResult != null)
                    {
                        nodeInputs[wire.ToPortId] = parent.LastResult;
                    }

                    // Smart ShapeModel resolution: if target port is "model", prefer trained ShapeModel from parent
                    if (wire.ToPortId == "model" && parent.LastOutputs.TryGetValue("model", out var pModel) && pModel != null)
                    {
                        nodeInputs["model"] = pModel;
                    }
                }
            }
        }
        return nodeInputs;
    }
}

/// <summary>
/// Semantic alias for GraphNode in DAG inspection pipelines.
/// </summary>
public abstract class PipelineNode : GraphNode
{
    protected PipelineNode(string title, string category) : base(title, category) { }
}

