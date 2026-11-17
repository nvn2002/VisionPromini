using System;
using OpenCvSharp;

namespace TunVision.Core.Graph;

public enum PortKind
{
    Image,
    Roi,
    Fixture,
    Pose,
    Point,
    BlobList,
    Measurement,
    Score,
    Verdict,
    Model,
    Any
}

public enum PortDirection
{
    Input,
    Output
}

public enum VerdictState
{
    None,
    Pass,
    Defect
}

public record VerdictResult(VerdictState State, string Message, double Value = 0, double MinLimit = 0, double MaxLimit = 0)
{
    public override string ToString() => State switch
    {
        VerdictState.Pass => $"PASS: {Message}",
        VerdictState.Defect => $"DEFECT: {Message}",
        _ => "NO VERDICT"
    };
}

public record MeasurementResult(string Name, double Value, string Unit = "px", double CenterX = 0, double CenterY = 0, double Extra = 0)
{
    public override string ToString() => $"{Name}: {Value:F2} {Unit}";
}

public record FixtureTransform(
    float OffsetX = 0,
    float OffsetY = 0,
    float RotationDeg = 0,
    float Scale = 1.0f,
    float ReferenceX = 0,
    float ReferenceY = 0)
{
    public static FixtureTransform Identity => new();

    public Point2f Transform(Point2f pt)
    {
        float dx = pt.X - ReferenceX;
        float dy = pt.Y - ReferenceY;
        float rad = RotationDeg * (MathF.PI / 180f);
        float cosA = MathF.Cos(rad) * Scale;
        float sinA = MathF.Sin(rad) * Scale;
        return new Point2f(
            OffsetX + dx * cosA - dy * sinA,
            OffsetY + dx * sinA + dy * cosA);
    }

    public Point2f InverseTransform(Point2f scenePt)
    {
        float ux = scenePt.X - OffsetX;
        float uy = scenePt.Y - OffsetY;
        float rad = -RotationDeg * (MathF.PI / 180f);
        float invScale = Scale > 1e-4f ? (1.0f / Scale) : 1.0f;
        float cosA = MathF.Cos(rad) * invScale;
        float sinA = MathF.Sin(rad) * invScale;
        return new Point2f(
            ReferenceX + ux * cosA - uy * sinA,
            ReferenceY + ux * sinA + uy * cosA);
    }
}

public class NodePort
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public PortKind Kind { get; set; } = PortKind.Any;
    public PortDirection Direction { get; set; } = PortDirection.Input;
    public string NodeId { get; set; } = string.Empty;

    public NodePort() { }

    public NodePort(string id, string name, PortKind kind, PortDirection direction, string nodeId = "")
    {
        Id = id;
        Name = name;
        Kind = kind;
        Direction = direction;
        NodeId = nodeId;
    }

    public bool IsCompatibleWith(NodePort other)
    {
        if (Direction == other.Direction) return false;
        if (Kind == PortKind.Any || other.Kind == PortKind.Any) return true;
        if (Kind == other.Kind) return true;
        // Compatible pairs
        if ((Kind == PortKind.Score && other.Kind == PortKind.Measurement) ||
            (Kind == PortKind.Measurement && other.Kind == PortKind.Score)) return true;
        if ((Kind == PortKind.Pose && other.Kind == PortKind.Fixture) ||
            (Kind == PortKind.Fixture && other.Kind == PortKind.Pose)) return true;
        if ((Kind == PortKind.Model && (other.Kind == PortKind.Model || other.Kind == PortKind.Image)) ||
            (other.Kind == PortKind.Model && (Kind == PortKind.Model || Kind == PortKind.Image))) return true;
        if ((Kind == PortKind.Point && (other.Kind == PortKind.Pose || other.Kind == PortKind.Measurement)) ||
            (other.Kind == PortKind.Point && (Kind == PortKind.Pose || Kind == PortKind.Measurement))) return true;
        return false;
    }
}

public class NodeConnection
{
    public string FromNodeId { get; set; } = string.Empty;
    public string FromPortId { get; set; } = string.Empty;
    public string ToNodeId { get; set; } = string.Empty;
    public string ToPortId { get; set; } = string.Empty;

    public NodeConnection() { }

    public NodeConnection(string fromNodeId, string fromPortId, string toNodeId, string toPortId)
    {
        FromNodeId = fromNodeId;
        FromPortId = fromPortId;
        ToNodeId = toNodeId;
        ToPortId = toPortId;
    }

    public override bool Equals(object? obj) =>
        obj is NodeConnection o &&
        FromNodeId == o.FromNodeId && FromPortId == o.FromPortId &&
        ToNodeId == o.ToNodeId && ToPortId == o.ToPortId;

    public override int GetHashCode() =>
        HashCode.Combine(FromNodeId, FromPortId, ToNodeId, ToPortId);
}
