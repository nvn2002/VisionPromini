using System;
using System.Diagnostics;
using OpenCvSharp;

namespace TunVision.Core.Abstractions;

public interface IVisionTool
{
    string Name { get; set; }
    bool Enabled { get; set; }
    IVisionResult? LastResult { get; }
    
    IVisionResult Run(Mat image);
    void SaveModel(string path);
    void LoadModel(string path);
}

public abstract class VisionToolBase : IVisionTool
{
    public string Name { get; set; }
    public bool Enabled { get; set; } = true;
    public IVisionResult? LastResult { get; protected set; }

    protected VisionToolBase(string name)
    {
        Name = name;
    }

    public IVisionResult Run(Mat image)
    {
        if (!Enabled)
        {
            LastResult = new BaseVisionResult
            {
                Success = true,
                Message = "Tool disabled",
                ExecutionTimeMs = 0
            };
            return LastResult;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var res = Process(image);
            sw.Stop();
            if (res is BaseVisionResult baseRes)
            {
                baseRes.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
            }
            LastResult = res;
            return res;
        }
        catch (Exception ex)
        {
            sw.Stop();
            LastResult = new BaseVisionResult
            {
                Success = false,
                Message = $"Error in {Name}: {ex.Message}",
                ExecutionTimeMs = sw.Elapsed.TotalMilliseconds
            };
            return LastResult;
        }
    }

    protected abstract IVisionResult Process(Mat image);
    public virtual void SaveModel(string path) { }
    public virtual void LoadModel(string path) { }
}
