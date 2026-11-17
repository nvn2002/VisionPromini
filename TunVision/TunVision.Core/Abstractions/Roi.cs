using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace TunVision.Core.Abstractions;

public enum RoiType
{
    None,
    Rectangle,
    RotatedRectangle,
    Circle,
    Polygon
}

public abstract class BaseRoi
{
    public abstract RoiType Type { get; }
    public abstract Rect GetBoundingBox();
    public abstract Mat CreateMask(int width, int height);
}

public class RectRoi : BaseRoi
{
    public override RoiType Type => RoiType.Rectangle;
    public Rect Rect { get; set; }

    public RectRoi(Rect rect)
    {
        Rect = rect;
    }

    public RectRoi(int x, int y, int width, int height)
    {
        Rect = new Rect(x, y, width, height);
    }

    public override Rect GetBoundingBox() => Rect;

    public override Mat CreateMask(int width, int height)
    {
        var mask = new Mat(height, width, MatType.CV_8UC1, Scalar.All(0));
        var validRect = Rect.Intersect(new Rect(0, 0, width, height));
        if (validRect.Width > 0 && validRect.Height > 0)
        {
            mask[validRect].SetTo(Scalar.All(255));
        }
        return mask;
    }
}

public class RotatedRectRoi : BaseRoi
{
    public override RoiType Type => RoiType.RotatedRectangle;
    public RotatedRect RotatedRect { get; set; }

    public RotatedRectRoi(RotatedRect rect)
    {
        RotatedRect = rect;
    }

    public RotatedRectRoi(Point2f center, Size2f size, float angleDeg)
    {
        RotatedRect = new RotatedRect(center, size, angleDeg);
    }

    public override Rect GetBoundingBox() => RotatedRect.BoundingRect();

    public override Mat CreateMask(int width, int height)
    {
        var mask = new Mat(height, width, MatType.CV_8UC1, Scalar.All(0));
        var pts = RotatedRect.Points().Select(p => new Point((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToArray();
        Cv2.FillConvexPoly(mask, pts, Scalar.All(255));
        return mask;
    }
}

public class CircleRoi : BaseRoi
{
    public override RoiType Type => RoiType.Circle;
    public Point2f Center { get; set; }
    public float Radius { get; set; }

    public CircleRoi(Point2f center, float radius)
    {
        Center = center;
        Radius = radius;
    }

    public override Rect GetBoundingBox()
    {
        return new Rect(
            (int)Math.Floor(Center.X - Radius),
            (int)Math.Floor(Center.Y - Radius),
            (int)Math.Ceiling(Radius * 2),
            (int)Math.Ceiling(Radius * 2));
    }

    public override Mat CreateMask(int width, int height)
    {
        var mask = new Mat(height, width, MatType.CV_8UC1, Scalar.All(0));
        Cv2.Circle(mask, new Point((int)Math.Round(Center.X), (int)Math.Round(Center.Y)), (int)Math.Round(Radius), Scalar.All(255), -1);
        return mask;
    }
}

public class PolygonRoi : BaseRoi
{
    public override RoiType Type => RoiType.Polygon;
    public List<Point2f> Points { get; set; } = new();

    public PolygonRoi(IEnumerable<Point2f> points)
    {
        Points = new List<Point2f>(points);
    }

    public override Rect GetBoundingBox()
    {
        if (Points.Count == 0) return new Rect(0, 0, 0, 0);
        float minX = Points.Min(p => p.X);
        float maxX = Points.Max(p => p.X);
        float minY = Points.Min(p => p.Y);
        float maxY = Points.Max(p => p.Y);
        return new Rect((int)Math.Floor(minX), (int)Math.Floor(minY), (int)Math.Ceiling(maxX - minX), (int)Math.Ceiling(maxY - minY));
    }

    public override Mat CreateMask(int width, int height)
    {
        var mask = new Mat(height, width, MatType.CV_8UC1, Scalar.All(0));
        if (Points.Count >= 3)
        {
            var pts = Points.Select(p => new Point((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToArray();
            Cv2.FillPoly(mask, new[] { pts }, Scalar.All(255));
        }
        return mask;
    }
}
