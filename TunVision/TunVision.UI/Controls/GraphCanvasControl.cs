using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TunVision.Core.Graph;
using TunVision.UI.Common;
using TunVision.UI.ViewModels;

namespace TunVision.UI.Controls;

public class GraphCanvasControl : FrameworkElement
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(MainViewModel), typeof(GraphCanvasControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnViewModelChanged));

    public MainViewModel? ViewModel
    {
        get => (MainViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GraphCanvasControl ctrl)
        {
            if (e.OldValue is MainViewModel oldVm)
            {
                oldVm.GraphNodes.CollectionChanged -= ctrl.OnCollectionChanged;
                oldVm.GraphConnections.CollectionChanged -= ctrl.OnCollectionChanged;
                oldVm.GraphResetRequested -= ctrl.OnGraphResetRequested;
                oldVm.PipelineRunCompleted -= ctrl.OnPipelineRunCompleted;
                oldVm.PropertyChanged -= ctrl.OnVmPropertyChanged;
            }
            if (e.NewValue is MainViewModel newVm)
            {
                newVm.GraphNodes.CollectionChanged += ctrl.OnCollectionChanged;
                newVm.GraphConnections.CollectionChanged += ctrl.OnCollectionChanged;
                newVm.GraphResetRequested += ctrl.OnGraphResetRequested;
                newVm.PipelineRunCompleted += ctrl.OnPipelineRunCompleted;
                newVm.PropertyChanged += ctrl.OnVmPropertyChanged;
                ctrl.FitGraphToScreen();
            }
            ctrl.InvalidateVisual();
        }
    }

    private void OnPipelineRunCompleted(object? sender, EventArgs e)
    {
        InvalidateVisual();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.CurrentSourceNode) or
                             nameof(MainViewModel.SelectedSourceFileItem) or
                             nameof(MainViewModel.OverallVerdict) or
                             nameof(MainViewModel.TotalExecutionTimeMs))
        {
            InvalidateVisual();
        }
    }

    private void OnGraphResetRequested(object? sender, EventArgs e)
    {
        FitGraphToScreen();
        InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }

    // Canvas navigation
    private double _zoom = 1.0;
    private Point _pan = new Point(40, 40);
    private Point _lastMousePos;
    private bool _isPanning = false;

    // Interaction states
    private NodeViewModel? _draggedNode = null;
    private Point _lastDragWorldPt;
    private PortViewModel? _wireStartPort = null;
    private Point _wireCurrentMouse;
    private Point _rightDownScreenPt;

    // Marquee / Box Selection states (Ctrl + Drag)
    private bool _isBoxSelecting = false;
    private Point _boxSelectStartScreen;
    private Point _boxSelectCurrentScreen;

    // Visual resources
    private readonly Typeface _typeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private readonly Typeface _typefaceBold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Brush _gridDotBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
    private readonly Brush _cardBgBrush = new SolidColorBrush(Color.FromArgb(240, 34, 34, 38));
    private readonly Pen _cardBorderPen = new(new SolidColorBrush(Color.FromRgb(60, 60, 66)), 1.2);
    private readonly Pen _selectedBorderPen = new(new SolidColorBrush(Color.FromRgb(0, 180, 255)), 2.4);
    private static readonly Pen _blackPinPen = new(Brushes.Black, 1.2);
    private static readonly Brush _bgBrush = new SolidColorBrush(Color.FromRgb(24, 24, 27));
    private static readonly Brush _marqueeFillBrush = new SolidColorBrush(Color.FromArgb(45, 0, 180, 255));
    private static readonly Pen _marqueeBorderPen = new(new SolidColorBrush(Color.FromArgb(230, 0, 229, 255)), 1.5) { DashStyle = DashStyles.Dash };

    private static readonly Brush _subtitleBrush = new SolidColorBrush(Color.FromRgb(0, 229, 255));
    private static readonly Brush _portLabelBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200));

    private static readonly Pen _ngBorderPen = new(new SolidColorBrush(Color.FromRgb(255, 68, 68)), 2.0);
    private static readonly Pen _errBorderPen = new(new SolidColorBrush(Color.FromRgb(255, 140, 0)), 2.0);
    private static readonly Brush _badgeBgNg = new SolidColorBrush(Color.FromArgb(220, 180, 20, 20));
    private static readonly Brush _badgeBgErr = new SolidColorBrush(Color.FromArgb(220, 200, 100, 0));
    private static readonly Brush _badgeBgOk = new SolidColorBrush(Color.FromArgb(220, 46, 125, 50));
    private static readonly SolidColorBrush _wireSelectHighlightBrush = new(Color.FromArgb(230, 255, 209, 102));
    private static readonly SolidColorBrush _wireDeleteBgBrush = new(Color.FromRgb(231, 76, 60));
    private static readonly Pen _wireDeleteBorderPen = new(Brushes.White, 1.5);
    private static readonly Pen _wireDeleteCrossPen = new(Brushes.White, 1.8)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round
    };

    private readonly Dictionary<(Color Color, int Thickness10, bool IsDashed), Pen> _penCache = new();
    private readonly Dictionary<(string Text, int Size10, bool IsBold, Color TextColor), FormattedText> _textCache = new();

    static GraphCanvasControl()
    {
        _gridDotBrush.Freeze();
        _blackPinPen.Freeze();
        _bgBrush.Freeze();
        _marqueeFillBrush.Freeze();
        _marqueeBorderPen.Freeze();
        _ngBorderPen.Freeze();
        _errBorderPen.Freeze();
        ((SolidColorBrush)_badgeBgNg).Freeze();
        ((SolidColorBrush)_badgeBgErr).Freeze();
        ((SolidColorBrush)_badgeBgOk).Freeze();
        ((SolidColorBrush)_subtitleBrush).Freeze();
        ((SolidColorBrush)_portLabelBrush).Freeze();
        _wireSelectHighlightBrush.Freeze();
        _wireDeleteBgBrush.Freeze();
        _wireDeleteBorderPen.Freeze();
        _wireDeleteCrossPen.Freeze();
    }

    public GraphCanvasControl()
    {
        ClipToBounds = true;
        Focusable = true;
        _cardBgBrush.Freeze();
        _cardBorderPen.Freeze();
        _selectedBorderPen.Freeze();

        LocalizationManager.Instance.LanguageChanged += (_, _) =>
        {
            _textCache.Clear();
            InvalidateVisual();
        };
    }

    private Pen GetOrCreatePen(Brush brush, double thickness, bool isDashed = false)
    {
        var color = brush is SolidColorBrush scb ? scb.Color : Colors.White;
        var key = (color, (int)(thickness * 10), isDashed);
        if (!_penCache.TryGetValue(key, out var pen))
        {
            pen = new Pen(brush, thickness);
            if (isDashed) pen.DashStyle = DashStyles.Dash;
            pen.Freeze();
            _penCache[key] = pen;
        }
        return pen;
    }

    private Pen GetOrCreateGlowPen(Brush brush, double thickness)
    {
        var color = brush is SolidColorBrush scb ? scb.Color : Colors.White;
        var glowColor = Color.FromArgb((byte)(color.A * 0.25), color.R, color.G, color.B);
        var key = (glowColor, (int)((thickness + 3) * 10), false);
        if (!_penCache.TryGetValue(key, out var pen))
        {
            var glowBrush = new SolidColorBrush(glowColor);
            glowBrush.Freeze();
            pen = new Pen(glowBrush, thickness + 3);
            pen.Freeze();
            _penCache[key] = pen;
        }
        return pen;
    }

    private FormattedText GetOrCreateText(string text, Typeface tf, double emSize, Brush brush)
    {
        var color = brush is SolidColorBrush scb ? scb.Color : Colors.White;
        bool isBold = tf == _typefaceBold;
        var key = (text, (int)(emSize * 10), isBold, color);
        if (!_textCache.TryGetValue(key, out var ft))
        {
            ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, emSize, brush, 1.25);
            if (_textCache.Count < 500)
                _textCache[key] = ft;
        }
        return ft;
    }

    public void FitGraphToScreen()
    {
        if (ViewModel == null || ViewModel.GraphNodes.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        double minX = ViewModel.GraphNodes.Min(n => n.X);
        double minY = ViewModel.GraphNodes.Min(n => n.Y);
        double maxX = ViewModel.GraphNodes.Max(n => n.X + n.Width);
        double maxY = ViewModel.GraphNodes.Max(n => n.Y + n.Height);

        double graphW = Math.Max(200, maxX - minX + 100);
        double graphH = Math.Max(200, maxY - minY + 100);

        double sx = (ActualWidth - 60) / graphW;
        double sy = (ActualHeight - 60) / graphH;
        _zoom = Math.Clamp(Math.Min(sx, sy), 0.5, 1.4);

        _pan.X = (ActualWidth - (maxX + minX) * _zoom) * 0.5;
        _pan.Y = (ActualHeight - (maxY + minY) * _zoom) * 0.5;
        InvalidateVisual();
    }

    private Point ScreenToWorld(Point p) =>
        new((p.X - _pan.X) / _zoom, (p.Y - _pan.Y) / _zoom);

    private Point WorldToScreen(Point p) =>
        new(p.X * _zoom + _pan.X, p.Y * _zoom + _pan.Y);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        // 1. Draw sleek dark dotted background
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(24, 24, 27)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        DrawGrid(dc);

        if (ViewModel == null) return;

        // 2. Draw Connection Wires (Cubic Bezier curves; unselected first, selected on top)
        foreach (var conn in ViewModel.GraphConnections)
        {
            if (conn.IsSelected) continue;
            var p1 = WorldToScreen(conn.FromPort.SocketPosition);
            var p2 = WorldToScreen(conn.ToPort.SocketPosition);
            DrawBezierWire(dc, p1, p2, conn.WireBrush, 2.5 * _zoom, isDashed: false, isSelected: false);
        }
        foreach (var conn in ViewModel.GraphConnections)
        {
            if (!conn.IsSelected) continue;
            var p1 = WorldToScreen(conn.FromPort.SocketPosition);
            var p2 = WorldToScreen(conn.ToPort.SocketPosition);
            DrawBezierWire(dc, p1, p2, conn.WireBrush, 2.5 * _zoom, isDashed: false, isSelected: true);
        }

        // 3. Draw in-progress interactive wire (if user is dragging from an output pin)
        if (_wireStartPort != null)
        {
            var p1 = WorldToScreen(_wireStartPort.SocketPosition);
            DrawBezierWire(dc, p1, _wireCurrentMouse, _wireStartPort.PortBrush, 2.5, isDashed: true);
        }

        // 4. Draw Nodes
        foreach (var node in ViewModel.GraphNodes)
        {
            DrawNode(dc, node);
        }

        // 5. Draw Marquee / Rubber-band Selection Box (Ctrl + Drag)
        if (_isBoxSelecting)
        {
            var selRect = new Rect(_boxSelectStartScreen, _boxSelectCurrentScreen);
            dc.DrawRectangle(_marqueeFillBrush, _marqueeBorderPen, selRect);
        }

        // 6. Draw Bottom-Right Minimap Overview
        DrawMinimap(dc);
    }

    private static readonly SolidColorBrush _minimapBgBrush = new(Color.FromArgb(200, 18, 18, 22));
    private static readonly Pen _minimapBorderPen = new(new SolidColorBrush(Color.FromRgb(70, 70, 78)), 1.0);
    private static readonly Pen _minimapViewPen = new(new SolidColorBrush(Color.FromArgb(180, 0, 229, 255)), 1.2);

    private void DrawMinimap(DrawingContext dc)
    {
        if (ViewModel == null || ViewModel.GraphNodes.Count == 0 || ActualWidth < 300 || ActualHeight < 180)
            return;

        double mapW = 135;
        double mapH = 82;
        var mapRect = new Rect(ActualWidth - mapW - 10, ActualHeight - mapH - 10, mapW, mapH);
        dc.DrawRoundedRectangle(_minimapBgBrush, _minimapBorderPen, mapRect, 4, 4);

        double minX = ViewModel.GraphNodes.Min(n => n.X) - 40;
        double minY = ViewModel.GraphNodes.Min(n => n.Y) - 40;
        double maxX = ViewModel.GraphNodes.Max(n => n.X + n.Width) + 40;
        double maxY = ViewModel.GraphNodes.Max(n => n.Y + n.Height) + 40;

        double worldW = Math.Max(400, maxX - minX);
        double worldH = Math.Max(250, maxY - minY);
        double scale = Math.Min((mapW - 12) / worldW, (mapH - 12) / worldH);

        double ox = mapRect.X + (mapW - worldW * scale) * 0.5;
        double oy = mapRect.Y + (mapH - worldH * scale) * 0.5;

        dc.PushClip(new RectangleGeometry(mapRect, 4, 4));
        foreach (var n in ViewModel.GraphNodes)
        {
            var nr = new Rect(
                ox + (n.X - minX) * scale,
                oy + (n.Y - minY) * scale,
                Math.Max(6, n.Width * scale),
                Math.Max(4, n.Height * scale));
            dc.DrawRoundedRectangle(n.CategoryBrush, n.IsSelected ? _minimapViewPen : null, nr, 1.5, 1.5);
        }

        var vTL = ScreenToWorld(new Point(0, 0));
        var vBR = ScreenToWorld(new Point(ActualWidth, ActualHeight));
        var viewRect = new Rect(
            ox + (vTL.X - minX) * scale,
            oy + (vTL.Y - minY) * scale,
            Math.Max(10, (vBR.X - vTL.X) * scale),
            Math.Max(8, (vBR.Y - vTL.Y) * scale));
        dc.DrawRectangle(null, _minimapViewPen, viewRect);
        dc.Pop();
    }



    private void DrawGrid(DrawingContext dc)
    {
        double spacing = 28 * _zoom;
        if (spacing < 14) spacing *= 2;

        var brush = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(_pan.X % spacing, _pan.Y % spacing, spacing, spacing),
            ViewportUnits = BrushMappingMode.Absolute,
            Drawing = new GeometryDrawing(_gridDotBrush, null, new RectangleGeometry(new Rect(0, 0, 1.5, 1.5)))
        };
        dc.DrawRectangle(brush, null, new Rect(0, 0, ActualWidth, ActualHeight));
    }

    private static void GetBezierControlPoints(Point p1, Point p2, double zoom, out Point c1, out Point c2)
    {
        double dx = Math.Abs(p2.X - p1.X) * 0.5;
        dx = Math.Max(30 * zoom, dx);
        c1 = new Point(p1.X + dx, p1.Y);
        c2 = new Point(p2.X - dx, p2.Y);
    }

    private static Point EvaluateCubicBezier(Point p1, Point c1, Point c2, Point p2, double t)
    {
        double u = 1.0 - t;
        double u2 = u * u;
        double t2 = t * t;
        double w0 = u2 * u;
        double w1 = 3.0 * u2 * t;
        double w2 = 3.0 * u * t2;
        double w3 = t2 * t;
        return new Point(
            w0 * p1.X + w1 * c1.X + w2 * c2.X + w3 * p2.X,
            w0 * p1.Y + w1 * c1.Y + w2 * c2.Y + w3 * p2.Y);
    }

    private static Point GetWireMidPoint(Point p1, Point p2, double zoom)
    {
        GetBezierControlPoints(p1, p2, zoom, out var c1, out var c2);
        return EvaluateCubicBezier(p1, c1, c2, p2, 0.5);
    }

    private static double DistancePointToSegment(Point p, Point a, Point b)
    {
        double vx = b.X - a.X;
        double vy = b.Y - a.Y;
        double wx = p.X - a.X;
        double wy = p.Y - a.Y;
        double lenSq = vx * vx + vy * vy;
        if (lenSq < 1e-6) return (p - a).Length;
        double t = Math.Clamp((wx * vx + wy * vy) / lenSq, 0.0, 1.0);
        double projX = a.X + t * vx;
        double projY = a.Y + t * vy;
        double dx = p.X - projX;
        double dy = p.Y - projY;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private ConnectionViewModel? HitTestWire(Point mouseScreen, out bool hitDeleteBadge)
    {
        hitDeleteBadge = false;
        if (ViewModel == null) return null;

        // First prioritize clicking the '×' delete badge on an already-selected wire
        for (int i = ViewModel.GraphConnections.Count - 1; i >= 0; i--)
        {
            var conn = ViewModel.GraphConnections[i];
            if (!conn.IsSelected) continue;
            var p1 = WorldToScreen(conn.FromPort.SocketPosition);
            var p2 = WorldToScreen(conn.ToPort.SocketPosition);
            var midPt = GetWireMidPoint(p1, p2, _zoom);
            if ((mouseScreen - midPt).Length <= 11.0)
            {
                hitDeleteBadge = true;
                return conn;
            }
        }

        // Next hit-test wire curves (sampled along 24 segments)
        for (int i = ViewModel.GraphConnections.Count - 1; i >= 0; i--)
        {
            var conn = ViewModel.GraphConnections[i];
            var p1 = WorldToScreen(conn.FromPort.SocketPosition);
            var p2 = WorldToScreen(conn.ToPort.SocketPosition);
            GetBezierControlPoints(p1, p2, _zoom, out var c1, out var c2);

            Point prev = p1;
            const int samples = 24;
            for (int s = 1; s <= samples; s++)
            {
                double t = s / (double)samples;
                Point curr = EvaluateCubicBezier(p1, c1, c2, p2, t);
                if (DistancePointToSegment(mouseScreen, prev, curr) <= 8.0)
                {
                    return conn;
                }
                prev = curr;
            }
        }

        return null;
    }

    private void DrawBezierWire(DrawingContext dc, Point p1, Point p2, Brush brush, double thickness, bool isDashed = false, bool isSelected = false)
    {
        GetBezierControlPoints(p1, p2, _zoom, out var c1, out var c2);

        var geom = new PathGeometry();
        var fig = new PathFigure { StartPoint = p1, IsFilled = false };
        fig.Segments.Add(new BezierSegment(c1, c2, p2, true));
        geom.Figures.Add(fig);
        geom.Freeze();

        if (isSelected)
        {
            var highlightPen = GetOrCreatePen(_wireSelectHighlightBrush, Math.Max(4.0, thickness + 3.5));
            dc.DrawGeometry(null, highlightPen, geom);
        }

        // Subtle glow background using cached frozen pen
        var glowPen = GetOrCreateGlowPen(brush, thickness);
        dc.DrawGeometry(null, glowPen, geom);

        // Main wire using cached frozen pen
        var wirePen = GetOrCreatePen(brush, thickness, isDashed);
        dc.DrawGeometry(null, wirePen, geom);

        // Curve midpoint marker (t = 0.5)
        var midPoint = EvaluateCubicBezier(p1, c1, c2, p2, 0.5);
        if (isSelected)
        {
            const double badgeR = 9.0;
            dc.DrawEllipse(_wireDeleteBgBrush, _wireDeleteBorderPen, midPoint, badgeR, badgeR);
            const double crossArm = 3.5;
            dc.DrawLine(_wireDeleteCrossPen,
                new Point(midPoint.X - crossArm, midPoint.Y - crossArm),
                new Point(midPoint.X + crossArm, midPoint.Y + crossArm));
            dc.DrawLine(_wireDeleteCrossPen,
                new Point(midPoint.X + crossArm, midPoint.Y - crossArm),
                new Point(midPoint.X - crossArm, midPoint.Y + crossArm));
        }
        else
        {
            dc.DrawEllipse(brush, null, midPoint, 3 * _zoom, 3 * _zoom);
        }
    }

    private void DrawNode(DrawingContext dc, NodeViewModel node)
    {
        var sp = WorldToScreen(new Point(node.X, node.Y));
        double nw = node.Width * _zoom;
        double nh = node.Height * _zoom;
        double hdrH = node.HeaderHeight * _zoom;

        var cardRect = new Rect(sp.X, sp.Y, nw, nh);

        // Card Shadow & Body - Highlight border on NG or ERR
        Pen borderPen = node.IsSelected ? _selectedBorderPen : _cardBorderPen;
        if (!node.IsSelected)
        {
            if (node.StatusShort == "NG") borderPen = _ngBorderPen;
            else if (node.StatusShort == "ERR") borderPen = _errBorderPen;
        }

        dc.DrawRoundedRectangle(_cardBgBrush, borderPen, cardRect, 6 * _zoom, 6 * _zoom);

        // Category Header Strip
        var hdrRect = new Rect(sp.X, sp.Y, nw, hdrH);
        dc.PushClip(new RectangleGeometry(cardRect, 6 * _zoom, 6 * _zoom));
        dc.DrawRectangle(node.CategoryBrush, null, hdrRect);
        dc.Pop();

        // Node Title Text (cached)
        var titleText = GetOrCreateText(node.Title, _typefaceBold, 11 * _zoom, Brushes.White);
        dc.DrawText(titleText, new Point(sp.X + 8 * _zoom, sp.Y + (hdrH - titleText.Height) * 0.5));

        // Execution Time & Status Badge (top-right of header)
        if (node.StatusShort == "NG")
        {
            var badgeText = GetOrCreateText("NG", _typefaceBold, 9 * _zoom, Brushes.White);
            double bw = badgeText.Width + 8 * _zoom;
            double bh = 14 * _zoom;
            double bx = sp.X + nw - bw - 6 * _zoom;
            double by = sp.Y + (hdrH - bh) * 0.5;
            dc.DrawRoundedRectangle(_badgeBgNg, null, new Rect(bx, by, bw, bh), 3 * _zoom, 3 * _zoom);
            dc.DrawText(badgeText, new Point(bx + 4 * _zoom, by + (bh - badgeText.Height) * 0.5));
        }
        else if (node.StatusShort == "ERR")
        {
            var badgeText = GetOrCreateText("ERR", _typefaceBold, 9 * _zoom, Brushes.White);
            double bw = badgeText.Width + 8 * _zoom;
            double bh = 14 * _zoom;
            double bx = sp.X + nw - bw - 6 * _zoom;
            double by = sp.Y + (hdrH - bh) * 0.5;
            dc.DrawRoundedRectangle(_badgeBgErr, null, new Rect(bx, by, bw, bh), 3 * _zoom, 3 * _zoom);
            dc.DrawText(badgeText, new Point(bx + 4 * _zoom, by + (bh - badgeText.Height) * 0.5));
        }
        else if (node.Node is SourceImageNode && node.StatusShort == "OK")
        {
            var badgeText = GetOrCreateText("OK", _typefaceBold, 9 * _zoom, Brushes.White);
            double bw = badgeText.Width + 8 * _zoom;
            double bh = 14 * _zoom;
            double bx = sp.X + nw - bw - 6 * _zoom;
            double by = sp.Y + (hdrH - bh) * 0.5;
            dc.DrawRoundedRectangle(_badgeBgOk, null, new Rect(bx, by, bw, bh), 3 * _zoom, 3 * _zoom);
            dc.DrawText(badgeText, new Point(bx + 4 * _zoom, by + (bh - badgeText.Height) * 0.5));
        }
        else if (node.ExecutionTimeMs > 0)
        {
            var msText = GetOrCreateText($"{node.ExecutionTimeMs:F1}ms", _typeface, 9 * _zoom, Brushes.LightGreen);
            dc.DrawText(msText, new Point(sp.X + nw - msText.Width - 6 * _zoom, sp.Y + (hdrH - msText.Height) * 0.5));
        }

        // Subtitle (Image file name, model status, measurements, or verdict)
        if (!string.IsNullOrEmpty(node.Subtitle))
        {
            string sub = node.Subtitle;
            if (sub.Length > 28) sub = sub[..26] + "..";
            var subBrush = node.StatusShort == "NG" ? Brushes.Tomato : (node.StatusShort == "ERR" ? Brushes.Orange : _subtitleBrush);
            var subText = GetOrCreateText(sub, _typeface, 9.5 * _zoom, subBrush);
            dc.DrawText(subText, new Point(sp.X + 8 * _zoom, sp.Y + hdrH + 2 * _zoom));
        }

        // Draw Input Ports (Left)
        for (int i = 0; i < node.InputPorts.Count; i++)
        {
            var port = node.InputPorts[i];
            var sockPt = WorldToScreen(port.SocketPosition);

            // Socket Pin (Circle)
            dc.DrawEllipse(port.PortBrush, _blackPinPen, sockPt, 4.5 * _zoom, 4.5 * _zoom);

            // Port Name (cached)
            var pText = GetOrCreateText(port.Name, _typeface, 10 * _zoom, _portLabelBrush);
            dc.DrawText(pText, new Point(sockPt.X + 8 * _zoom, sockPt.Y - pText.Height * 0.5));
        }

        // Draw Output Ports (Right)
        for (int i = 0; i < node.OutputPorts.Count; i++)
        {
            var port = node.OutputPorts[i];
            var sockPt = WorldToScreen(port.SocketPosition);

            // Socket Pin (Circle)
            dc.DrawEllipse(port.PortBrush, _blackPinPen, sockPt, 4.5 * _zoom, 4.5 * _zoom);

            // Port Name (cached)
            var pText = GetOrCreateText(port.Name, _typeface, 10 * _zoom, _portLabelBrush);
            dc.DrawText(pText, new Point(sockPt.X - pText.Width - 8 * _zoom, sockPt.Y - pText.Height * 0.5));
        }
    }

    #region Mouse Interactions

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var mousePt = e.GetPosition(this);
        var worldPt = ScreenToWorld(mousePt);
        _lastMousePos = mousePt;

        // Double click on Node -> Open separate NodeSettingsWindow!
        // Double click on empty canvas background -> Fit to Screen!
        if (e.ClickCount == 2)
        {
            NodeViewModel? dblHitNode = null;
            if (ViewModel != null)
            {
                for (int i = ViewModel.GraphNodes.Count - 1; i >= 0; i--)
                {
                    var n = ViewModel.GraphNodes[i];
                    if (worldPt.X >= n.X && worldPt.X <= n.X + n.Width &&
                        worldPt.Y >= n.Y && worldPt.Y <= n.Y + n.Height)
                    {
                        dblHitNode = n;
                        break;
                    }
                }
            }

            if (dblHitNode != null && ViewModel != null)
            {
                foreach (var n in ViewModel.GraphNodes) n.IsSelected = false;
                dblHitNode.IsSelected = true;
                ViewModel.SelectedNode = dblHitNode;
                _draggedNode = null;
                ReleaseMouseCapture();
                InvalidateVisual();
                ViewModel.RequestOpenNodeSettings();
                e.Handled = true;
                return;
            }
            else
            {
                FitGraphToScreen();
                e.Handled = true;
                return;
            }
        }

        // Middle or Right button drag -> Pan canvas (Right click at same spot deletes hit wire!)
        if (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right)
        {
            if (e.ChangedButton == MouseButton.Right)
            {
                _rightDownScreenPt = mousePt;
            }
            _isPanning = true;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && ViewModel != null)
        {
            bool isCtrlHeld = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

            // 1. Check if clicked on an Output Socket Pin (to start dragging a wire)
            if (!isCtrlHeld)
            {
                foreach (var node in ViewModel.GraphNodes)
                {
                    foreach (var outPort in node.OutputPorts)
                    {
                        var sockScreen = WorldToScreen(outPort.SocketPosition);
                        if ((mousePt - sockScreen).Length <= 10)
                        {
                            _wireStartPort = outPort;
                            _wireCurrentMouse = mousePt;
                            CaptureMouse();
                            InvalidateVisual();
                            e.Handled = true;
                            return;
                        }
                    }
                }

                // 1b. Check if clicked the red '×' quick-delete badge on a selected wire
                var badgeHit = HitTestWire(mousePt, out bool hitDeleteBadge);
                if (badgeHit != null && hitDeleteBadge)
                {
                    ViewModel.DeleteConnection(badgeHit);
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }
            }

            // 2. If Ctrl is held -> Start Marquee Box Selection (or toggle node/wire on click)
            if (isCtrlHeld)
            {
                _isBoxSelecting = true;
                _boxSelectStartScreen = mousePt;
                _boxSelectCurrentScreen = mousePt;
                CaptureMouse();
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            // 3. Check if clicked on a Node (to select and drag it, or drag all selected nodes together)
            NodeViewModel? hitNode = null;
            for (int i = ViewModel.GraphNodes.Count - 1; i >= 0; i--)
            {
                var n = ViewModel.GraphNodes[i];
                if (worldPt.X >= n.X && worldPt.X <= n.X + n.Width &&
                    worldPt.Y >= n.Y && worldPt.Y <= n.Y + n.Height)
                {
                    hitNode = n;
                    break;
                }
            }

            if (hitNode != null)
            {
                // If clicking an unselected node, select only this node and clear wire selections.
                foreach (var c in ViewModel.GraphConnections) c.IsSelected = false;
                if (!hitNode.IsSelected)
                {
                    foreach (var n in ViewModel.GraphNodes) n.IsSelected = false;
                    hitNode.IsSelected = true;
                }
                ViewModel.SelectedNode = hitNode;

                _draggedNode = hitNode;
                _lastDragWorldPt = worldPt;
                CaptureMouse();
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            // 4. Check if clicked on a Wire curve (select wire for quick delete or Delete key)
            var hitWire = HitTestWire(mousePt, out _);
            if (hitWire != null)
            {
                foreach (var n in ViewModel.GraphNodes) n.IsSelected = false;
                ViewModel.SelectedNode = null;
                foreach (var c in ViewModel.GraphConnections) c.IsSelected = false;
                hitWire.IsSelected = true;
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            // Clicked empty background without Ctrl: deselect all nodes/wires and start PAN!
            foreach (var n in ViewModel.GraphNodes) n.IsSelected = false;
            foreach (var c in ViewModel.GraphConnections) c.IsSelected = false;
            ViewModel.SelectedNode = null;
            _isPanning = true;
            CaptureMouse();
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var mousePt = e.GetPosition(this);
        var worldPt = ScreenToWorld(mousePt);

        if (ViewModel != null)
        {
            ViewModel.GraphCursorText = $"X: {(int)Math.Round(worldPt.X)} - Y: {(int)Math.Round(worldPt.Y)}";
        }

        if (_isBoxSelecting && ViewModel != null)
        {
            _boxSelectCurrentScreen = mousePt;
            if ((mousePt - _boxSelectStartScreen).Length >= 4.0)
            {
                var worldRect = new Rect(ScreenToWorld(_boxSelectStartScreen), worldPt);
                var screenRect = new Rect(_boxSelectStartScreen, mousePt);
                foreach (var n in ViewModel.GraphNodes)
                {
                    var nodeRect = new Rect(n.X, n.Y, n.Width, n.Height);
                    n.IsSelected = worldRect.IntersectsWith(nodeRect);
                }
                foreach (var c in ViewModel.GraphConnections)
                {
                    var p1 = WorldToScreen(c.FromPort.SocketPosition);
                    var p2 = WorldToScreen(c.ToPort.SocketPosition);
                    c.IsSelected = screenRect.Contains(GetWireMidPoint(p1, p2, _zoom));
                }
                ViewModel.SelectedNode = ViewModel.GraphNodes.FirstOrDefault(n => n.IsSelected);
                InvalidateVisual();
            }
            return;
        }

        if (_isPanning)
        {
            _pan.X += (mousePt.X - _lastMousePos.X);
            _pan.Y += (mousePt.Y - _lastMousePos.Y);
            _lastMousePos = mousePt;
            InvalidateVisual();
            return;
        }

        if (_wireStartPort != null)
        {
            _wireCurrentMouse = mousePt;
            InvalidateVisual();
            return;
        }

        if (_draggedNode != null && ViewModel != null)
        {
            double dx = worldPt.X - _lastDragWorldPt.X;
            double dy = worldPt.Y - _lastDragWorldPt.Y;
            _lastDragWorldPt = worldPt;

            // Move all selected nodes together
            foreach (var selNode in ViewModel.GraphNodes.Where(n => n.IsSelected))
            {
                selNode.X += dx;
                selNode.Y += dy;
            }
            InvalidateVisual();
            return;
        }

        _lastMousePos = mousePt;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        var mousePt = e.GetPosition(this);

        if (_isBoxSelecting && ViewModel != null)
        {
            // If user did a quick Ctrl+Click (< 4px drag), toggle the clicked node or wire
            if ((mousePt - _boxSelectStartScreen).Length < 4.0)
            {
                var worldPt = ScreenToWorld(mousePt);
                bool toggledNode = false;
                for (int i = ViewModel.GraphNodes.Count - 1; i >= 0; i--)
                {
                    var n = ViewModel.GraphNodes[i];
                    if (worldPt.X >= n.X && worldPt.X <= n.X + n.Width &&
                        worldPt.Y >= n.Y && worldPt.Y <= n.Y + n.Height)
                    {
                        n.IsSelected = !n.IsSelected;
                        if (n.IsSelected) ViewModel.SelectedNode = n;
                        toggledNode = true;
                        break;
                    }
                }
                if (!toggledNode)
                {
                    var hitWire = HitTestWire(mousePt, out _);
                    if (hitWire != null)
                    {
                        hitWire.IsSelected = !hitWire.IsSelected;
                    }
                }
            }

            _isBoxSelecting = false;
            ReleaseMouseCapture();
            InvalidateVisual();
            return;
        }

        if (_isPanning)
        {
            _isPanning = false;
            ReleaseMouseCapture();

            // Right-click in place (< 5px movement) deletes hit wire or disconnects hit Input socket pin
            if (e.ChangedButton == MouseButton.Right && ViewModel != null && (mousePt - _rightDownScreenPt).Length < 5.0)
            {
                var hitWire = HitTestWire(mousePt, out _);
                if (hitWire != null)
                {
                    ViewModel.DeleteConnection(hitWire);
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }

                foreach (var node in ViewModel.GraphNodes)
                {
                    foreach (var inPort in node.InputPorts)
                    {
                        var sockScreen = WorldToScreen(inPort.SocketPosition);
                        if ((mousePt - sockScreen).Length <= 12.0)
                        {
                            var attachedWire = ViewModel.GraphConnections.FirstOrDefault(c =>
                                c.ToPort == inPort ||
                                (c.Connection.ToNodeId == node.Id && c.Connection.ToPortId == inPort.Id));
                            if (attachedWire != null)
                            {
                                ViewModel.DeleteConnection(attachedWire);
                                InvalidateVisual();
                                e.Handled = true;
                                return;
                            }
                        }
                    }
                }
            }
        }

        if (_draggedNode != null)
        {
            _draggedNode = null;
            ReleaseMouseCapture();
        }

        if (_wireStartPort != null && ViewModel != null)
        {
            // Check if dropped on a compatible Input Socket
            PortViewModel? targetInputPort = null;
            foreach (var node in ViewModel.GraphNodes)
            {
                if (node.Id == _wireStartPort.ParentNode.Id) continue;
                foreach (var inPort in node.InputPorts)
                {
                    var sockScreen = WorldToScreen(inPort.SocketPosition);
                    if ((mousePt - sockScreen).Length <= 14)
                    {
                        if (_wireStartPort.Port.IsCompatibleWith(inPort.Port))
                        {
                            targetInputPort = inPort;
                            break;
                        }
                    }
                }
                if (targetInputPort != null) break;
            }

            if (targetInputPort != null)
            {
                ViewModel.ConnectPorts(_wireStartPort, targetInputPort);
            }

            _wireStartPort = null;
            ReleaseMouseCapture();
            InvalidateVisual();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (ViewModel == null) return;

        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            ViewModel.DeleteSelectedNode();
            InvalidateVisual();
            e.Handled = true;
        }
        else if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            foreach (var n in ViewModel.GraphNodes) n.IsSelected = true;
            ViewModel.SelectedNode = ViewModel.GraphNodes.FirstOrDefault();
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var mousePos = e.GetPosition(this);
        double zoomDelta = e.Delta > 0 ? 1.12 : (1.0 / 1.12);
        double newZoom = Math.Clamp(_zoom * zoomDelta, 0.25, 3.0);

        // Zoom relative to mouse cursor
        _pan.X = mousePos.X - (mousePos.X - _pan.X) * (newZoom / _zoom);
        _pan.Y = mousePos.Y - (mousePos.Y - _pan.Y) * (newZoom / _zoom);
        _zoom = newZoom;
        _textCache.Clear();

        InvalidateVisual();
    }

    #endregion
}
