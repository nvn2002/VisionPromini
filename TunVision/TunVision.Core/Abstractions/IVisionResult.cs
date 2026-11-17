using System;

namespace TunVision.Core.Abstractions;

public interface IVisionResult
{
    bool Success { get; }
    double ExecutionTimeMs { get; }
    string Message { get; }
    object? Data { get; }
}

public class BaseVisionResult : IVisionResult
{
    public bool Success { get; set; }
    public double ExecutionTimeMs { get; set; }
    public string Message { get; set; } = string.Empty;
    public object? Data { get; set; }
}
