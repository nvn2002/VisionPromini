using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenCvSharp;
using TunVision.Core.Abstractions;
using TunVision.Core.Models;

namespace TunVision.Core.Engine;

public class VisionEngine
{
    private readonly List<IVisionTool> _tools = new();

    public IReadOnlyList<IVisionTool> Tools => _tools;

    public void AddTool(IVisionTool tool)
    {
        _tools.Add(tool);
    }

    public void RemoveTool(IVisionTool tool)
    {
        _tools.Remove(tool);
    }

    public void ClearTools()
    {
        _tools.Clear();
    }

    public CompositeVisionResult Execute(Mat image)
    {
        var sw = Stopwatch.StartNew();
        var result = new CompositeVisionResult();
        bool overallSuccess = true;

        foreach (var tool in _tools)
        {
            if (!tool.Enabled) continue;

            var toolRes = tool.Run(image);
            result.ToolResults[tool.Name] = toolRes;

            if (!toolRes.Success)
            {
                overallSuccess = false;
            }
        }

        sw.Stop();
        result.Success = overallSuccess;
        result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
        result.Message = $"Engine executed {_tools.Count} tool(s) in {result.ExecutionTimeMs:F1} ms";

        return result;
    }
}
