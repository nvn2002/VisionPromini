using System.Collections.Generic;
using TunVision.Core.Abstractions;

namespace TunVision.Core.Models;

public class CompositeVisionResult : BaseVisionResult
{
    public Dictionary<string, IVisionResult> ToolResults { get; set; } = new();

    public CompositeVisionResult()
    {
        Data = ToolResults;
    }
}
