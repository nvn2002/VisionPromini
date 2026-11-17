using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OpenCvSharp;
using TunVision.Core.Caliper;
using TunVision.Core.Graph;
using TunVision.Core.Models;
using TunVision.Core.ShapeMatching;
using TunVision.UI.ViewModels;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

namespace TunVision.UI.Controls;

public class VisionCanvas : FrameworkElement
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(MainViewModel), typeof(VisionCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnViewModelChanged));

    public MainViewModel? ViewModel
    {
        get => (MainViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty OverlayDrawModeProperty =
        DependencyProperty.Register(nameof(OverlayDrawMode), typeof(OverlayDrawMode), typeof(VisionCanvas),
            new FrameworkPropertyMetadata(OverlayDrawMode.NoScore, FrameworkPropertyMetadataOptions.AffectsRender));

    public OverlayDrawMode OverlayDrawMode
    {
        get => (OverlayDrawMode)GetValue(OverlayDrawModeProperty);
        set => SetValue(OverlayDrawModeProperty, value);
    }

    public static readonly DependencyProperty PreviewTemporaryContoursProperty =
        DependencyProperty.Register(nameof(PreviewTemporaryContours), typeof(IReadOnlyList<Point2f>), typeof(VisionCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<Point2f>? PreviewTemporaryContours
    {
        get => (IReadOnlyList<Point2f>?)GetValue(PreviewTemporaryContoursProperty);
        set => SetValue(PreviewTemporaryContoursProperty, value);
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VisionCanvas canvas)
        {
            if (e.OldValue is MainViewModel oldVm)
                oldVm.PropertyChanged -= canvas.OnVmPropertyChanged;
            if (e.NewValue is MainViewModel newVm)
            {
                newVm.PropertyChanged += canvas.OnVmPropertyChanged;
                canvas.OverlayDrawMode = newVm.OverlayDrawMode;
                canvas.FitToScreen();
            }
            canvas.InvalidateVisual();
        }
    }

    private int _lastImgW = 0;
    private int _lastImgH = 0;
    private string _hudPixelInfo = string.Empty;
    private static readonly byte[] _pixelBuffer = new byte[4];
    private static readonly SolidColorBrush _hudBgBrush = CreateFrozenBrush(Color.FromArgb(180, 20, 20, 20));
    private static readonly Typeface _hudTypeface = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentPreviewSource))
        {
            if (ViewModel?.CurrentPreviewSource is { } src)
            {
                if (src.PixelWidth != _lastImgW || src.PixelHeight != _lastImgH)
                {
                    _lastImgW = src.PixelWidth;
                    _lastImgH = src.PixelHeight;
                    FitToScreen();
                }
                UpdateHudPixelInfo(_currentMouseScreen);
            }
            else
            {
                _hudPixelInfo = string.Empty;
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.OverlayDrawMode))
        {
            if (ViewModel != null)
                OverlayDrawMode = ViewModel.OverlayDrawMode;
        }
        InvalidateVisual();
    }

    private double _zoom = 1.0;
    private Point _pan = new(0, 0);
    private Point _lastMousePos;
    private Point _currentMouseScreen;
    private bool _isMouseInside = false;
    private bool _isPanning = false;

    // Interactive Gizmo & Tool Dragging
    private enum ActiveHandle
    {
        None,
        CircleCenter,
        CircleRadius,
        CircleAnnulus,
        LineStart,
        LineEnd,
        LineWidth,
        CornerL1Start,
        CornerL1End,
        CornerL2Start,
        CornerL2End,
        RoiTopLeft,
        RoiTopRight,
        RoiBottomRight,
        RoiBottomLeft,
        RoiRectCenter,
        RoiRotation,
        RoiCircleCenter,
        RoiCircleRadius,
        RoiPolygonCenter,
        RoiPolygonVertex,
        CalibPoint1,
        CalibPoint2
    }

    private ActiveHandle _draggedHandle = ActiveHandle.None;
    private int _draggedPolygonVertexIndex = -1;
    private bool _isDraggingMarquee = false;
    private Point _marqueeStartImg;
    private Point _marqueeCurrentImg;
    private bool _isBrushErasing = false;
    private bool _brushFirstSampleInStroke = false;

    // Cached Pens & Brushes for Gizmos
    private static readonly SolidColorBrush _bgBrush = new(Color.FromRgb(16, 16, 18));
    private static readonly Pen _orangeRingPen = new(new SolidColorBrush(Color.FromRgb(255, 150, 0)), 1.6);
    private static readonly Pen _orangeDashPen = new(new SolidColorBrush(Color.FromArgb(200, 255, 165, 0)), 1.3) { DashStyle = DashStyles.Dash };
    private static readonly Pen _purpleNomPen = new(new SolidColorBrush(Color.FromRgb(200, 100, 255)), 1.6);
    private static readonly Pen _cyanArrowPen = new(new SolidColorBrush(Color.FromRgb(0, 229, 255)), 1.5);
    private static readonly Pen _handlePen = new(Brushes.Black, 1.2);
    private static readonly SolidColorBrush _eraseFillBrush = new(Color.FromArgb(65, 255, 50, 50));
    private static readonly Pen _eraseBorderPen = new(new SolidColorBrush(Color.FromRgb(255, 70, 70)), 1.8);
    private static readonly SolidColorBrush _roiFillBrush = new(Color.FromArgb(45, 0, 229, 255));
    private static readonly Pen _roiBorderPen = new(new SolidColorBrush(Color.FromRgb(0, 229, 255)), 1.8) { DashStyle = DashStyles.Dash };
    private static readonly Pen _previewContourPen = new(new SolidColorBrush(Color.FromRgb(0, 255, 100)), 1.5)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round
    };

    static VisionCanvas()
    {
        _bgBrush.Freeze();
        _orangeRingPen.Freeze();
        _orangeDashPen.Freeze();
        _purpleNomPen.Freeze();
        _cyanArrowPen.Freeze();
        _handlePen.Freeze();
        _eraseFillBrush.Freeze();
        _eraseBorderPen.Freeze();
        _roiFillBrush.Freeze();
        _roiBorderPen.Freeze();
        _featurePointBrush.Freeze();
        _calibBadgeBg.Freeze();
        _previewContourPen.Freeze();
    }

    public VisionCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    private Point ScreenToImage(Point s) =>
        new((s.X - _pan.X) / _zoom, (s.Y - _pan.Y) / _zoom);

    private Point ImageToScreen(Point img) =>
        new(img.X * _zoom + _pan.X, img.Y * _zoom + _pan.Y);

    private Point ImageToScreen(float x, float y) =>
        new(x * _zoom + _pan.X, y * _zoom + _pan.Y);

    public void FitToScreen()
    {
        if (ViewModel?.CurrentPreviewSource == null || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        double imgW = ViewModel.CurrentPreviewSource.PixelWidth;
        double imgH = ViewModel.CurrentPreviewSource.PixelHeight;

        double scaleX = (ActualWidth - 20) / imgW;
        double scaleY = (ActualHeight - 20) / imgH;
        _zoom = Math.Max(0.05, Math.Min(scaleX, scaleY));

        _pan.X = (ActualWidth - imgW * _zoom) * 0.5;
        _pan.Y = (ActualHeight - imgH * _zoom) * 0.5;
        ViewModel.VisionZoomText = $"Zoom {(int)Math.Round(_zoom * 100)}%";
        InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_lastImgW > 0 && _lastImgH > 0)
            FitToScreen();
    }

    public static double GetZoomAdaptiveStrokeThickness(double zoom)
    {
        double z = Math.Max(0.05, zoom);
        // Zoom out (z < 1.0) -> thicker screen stroke (up to 2.20 px, ~2.2/z img px)
        // Zoom in  (z > 1.0) -> thinner hairline stroke (down to 0.85 px, ~0.85/z img px)
        return Math.Clamp(1.35 * Math.Pow(z, -0.28), 0.85, 2.20);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(_bgBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (ViewModel?.CurrentPreviewSource == null) return;

        // 1. Draw Clean Base Image (NearestNeighbor at high zoom so subpixel contours cut cleanly through pixels)
        RenderOptions.SetBitmapScalingMode(
            this,
            _zoom >= 2.0 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);

        dc.PushTransform(new TranslateTransform(_pan.X, _pan.Y));
        dc.PushTransform(new ScaleTransform(_zoom, _zoom));

        var src = ViewModel.CurrentPreviewSource;
        dc.DrawImage(src, new Rect(0, 0, src.PixelWidth, src.PixelHeight));

        dc.Pop();
        dc.Pop();

        // 2. Draw Vector Shape Model & Match Overlays in Screen Space (adaptive thickness by zoom)
        bool drewShapeContours = DrawShapeModelAndMatchOverlays(dc);

        // 2a. Draw G4 Asynchronous Live Contrast Preview Contours (if active)
        var tempPts = PreviewTemporaryContours ?? ViewModel.PreviewTemporaryContours;
        if (tempPts is { Count: > 0 })
        {
            DrawPreviewTemporaryContours(dc, tempPts);
        }

        // 2b. Draw Interactive Node Gizmos in Screen Space (crisp lines regardless of zoom!)
        DrawActiveNodeGizmos(dc);

        // 2c. Draw Template Feature Points when requested or in Erase Mode
        if (ViewModel.ShowTemplateFeatures ||
            ViewModel.InteractionMode == CanvasInteractionMode.BrushErase ||
            ViewModel.InteractionMode == CanvasInteractionMode.BoxErase)
        {
            DrawTemplateFeaturePoints(dc, skipGreenContours: drewShapeContours);
        }

        // 3. Draw Marquee Box (DrawTemplateRoi or BoxErase)
        if (_isDraggingMarquee)
        {
            if (ViewModel.InteractionMode == CanvasInteractionMode.DrawTemplateRoi && ViewModel.CurrentCalibrationNode is { } calDragNode)
            {
                double dx = _marqueeCurrentImg.X - _marqueeStartImg.X;
                double dy = _marqueeCurrentImg.Y - _marqueeStartImg.Y;
                double pxLen = Math.Sqrt(dx * dx + dy * dy);
                DrawCalibrationRulerGizmo(
                    dc,
                    new Point2f((float)_marqueeStartImg.X, (float)_marqueeStartImg.Y),
                    new Point2f((float)_marqueeCurrentImg.X, (float)_marqueeCurrentImg.Y),
                    $"{pxLen:F2} px  ➔  {calDragNode.KnownRealLength:F2} {calDragNode.UnitName}");
            }
            else
            {
                var p1 = ImageToScreen(_marqueeStartImg);
                var p2 = ImageToScreen(_marqueeCurrentImg);
                var rect = new Rect(p1, p2);
                if (ViewModel.InteractionMode == CanvasInteractionMode.BoxErase)
                    dc.DrawRectangle(_eraseFillBrush, _eraseBorderPen, rect);
                else
                    dc.DrawRectangle(_roiFillBrush, _roiBorderPen, rect);
            }
        }

        // 4. Draw Brush Erase Cursor Circle
        if (_isMouseInside && ViewModel.InteractionMode == CanvasInteractionMode.BrushErase)
        {
            double rScreen = Math.Max(3.0, ViewModel.EraserBrushRadius * _zoom);
            dc.DrawEllipse(_eraseFillBrush, _eraseBorderPen, _currentMouseScreen, rScreen, rScreen);
        }

        // 5. Draw G1: HUD Pixel Inspection Bar (Screen Space)
        DrawHudPixelInspectionBar(dc);
    }

    private static readonly SolidColorBrush _inlierGreenBrush = CreateFrozenBrush(Color.FromRgb(0, 255, 0));
    private static readonly SolidColorBrush _secondaryCyanBrush = CreateFrozenBrush(Color.FromRgb(0, 230, 255));
    private static readonly SolidColorBrush _maskedPointBrush = CreateFrozenBrush(Color.FromRgb(255, 40, 40));
    private static readonly SolidColorBrush _outlierRedBrush = CreateFrozenBrush(Color.FromRgb(255, 0, 0));
    private static readonly SolidColorBrush _featurePointBrush = CreateFrozenBrush(Color.FromRgb(0, 255, 220));
    private static readonly SolidColorBrush _boxPrimaryBrush = CreateFrozenBrush(Color.FromRgb(255, 230, 0));
    private static readonly SolidColorBrush _boxSecondaryBrush = CreateFrozenBrush(Color.FromRgb(255, 165, 0));
    private static readonly SolidColorBrush _roiBoxBrush = CreateFrozenBrush(Color.FromRgb(255, 165, 0));
    private static readonly SolidColorBrush _axisXBrush = CreateFrozenBrush(Color.FromRgb(255, 0, 0));
    private static readonly SolidColorBrush _axisYBrush = CreateFrozenBrush(Color.FromRgb(0, 255, 0));
    private static readonly SolidColorBrush _originDotBrush = CreateFrozenBrush(Color.FromRgb(255, 255, 0));
    private static readonly SolidColorBrush _badgeDarkBg = CreateFrozenBrush(Color.FromArgb(220, 15, 15, 15));
    private static readonly SolidColorBrush _calibBadgeBg = CreateFrozenBrush(Color.FromArgb(215, 16, 22, 30));
    private static readonly Typeface _overlayTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private static SolidColorBrush CreateFrozenBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Pen CreateRoundPen(Brush brush, double thickness)
    {
        var p = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        p.Freeze();
        return p;
    }

    private double _cachedPenWidth = -1;
    private Pen? _penContourGreen;
    private Pen? _penContourCyan;
    private Pen? _penOutlierRed;
    private Pen? _penBoxPrimary;
    private Pen? _penBoxSecondary;
    private Pen? _penRoiOrange;
    private Pen? _penAxisX;
    private Pen? _penAxisY;
    private Pen? _penOriginOutline;
    private Pen? _penCrosshair;

    private void EnsureZoomAdaptivePens(double strokeW)
    {
        double key = Math.Round(strokeW, 3);
        if (Math.Abs(_cachedPenWidth - key) < 1e-4 && _penContourGreen != null) return;
        _cachedPenWidth = key;
        double boxW = Math.Max(0.80, key * 0.90);
        double axisOutlineW = Math.Max(0.65, key * 0.65);

        _penContourGreen = CreateRoundPen(_inlierGreenBrush, key);
        _penContourCyan = CreateRoundPen(_secondaryCyanBrush, key);
        _penOutlierRed = CreateRoundPen(_outlierRedBrush, key);
        _penBoxPrimary = CreateRoundPen(_boxPrimaryBrush, boxW);
        _penBoxSecondary = CreateRoundPen(_boxSecondaryBrush, boxW);
        _penRoiOrange = CreateRoundPen(_roiBoxBrush, boxW);
        _penAxisX = CreateRoundPen(_axisXBrush, key);
        _penAxisY = CreateRoundPen(_axisYBrush, key);
        _penOriginOutline = CreateRoundPen(Brushes.Black, axisOutlineW);
        _penCrosshair = CreateRoundPen(_outlierRedBrush, key);
    }

    private bool DrawShapeModelAndMatchOverlays(DrawingContext dc)
    {
        if (ViewModel == null) return false;

        double strokeW = GetZoomAdaptiveStrokeThickness(_zoom);
        EnsureZoomAdaptivePens(strokeW);

        if (ViewModel.GetActiveTrainShapeNodeForCanvas() is { } trainNode)
        {
            bool drew = false;
            if (trainNode.ToolInstance.Model is { } tplModel)
            {
                DrawModelTemplateOverlayScreen(dc, tplModel, strokeW);
                drew = true;
            }
            if (trainNode.HasCustomRoi)
            {
                var p1 = ImageToScreen(trainNode.RoiX, trainNode.RoiY);
                var p2 = ImageToScreen(trainNode.RoiX + trainNode.RoiWidth, trainNode.RoiY + trainNode.RoiHeight);
                dc.DrawRectangle(null, _penRoiOrange!, new Rect(p1, p2));
            }
            return drew;
        }

        var matchNodes = ViewModel.GetActiveShapeMatchNodesForCanvas();
        if (matchNodes.Count == 0) return false;

        bool anyContoursDrawn = false;
        bool forceShowModel = ViewModel.ShowTemplateFeatures ||
                              ViewModel.InteractionMode == CanvasInteractionMode.BrushErase ||
                              ViewModel.InteractionMode == CanvasInteractionMode.BoxErase;

        foreach (var smNode in matchNodes)
        {
            if (smNode.LastMatches.Count > 0)
            {
                DrawMatchResultsOverlayScreen(dc, smNode.LastMatches, smNode.Model, strokeW);
                anyContoursDrawn = true;
            }
            else if (smNode.Model != null && (smNode == ViewModel.CurrentShapeMatchNode || forceShowModel))
            {
                DrawModelTemplateOverlayScreen(dc, smNode.Model, strokeW);
                anyContoursDrawn = true;
            }
        }

        return anyContoursDrawn;
    }

    private void DrawModelTemplateOverlayScreen(DrawingContext dc, ShapeModel model, double strokeW)
    {
        float ox = model.OriginX;
        float oy = model.OriginY;

        if (model.ModelContours != null && model.ModelContours.Count > 0)
        {
            DrawContoursScreen(dc, model.ModelContours, ox, oy, cosA: 1f, sinA: 0f, scale: 1f, _penContourGreen!);
        }
        else if (model.Levels.Count > 0 && model.Levels[0].Features.Count > 0)
        {
            DrawFeatureTangentsFromModelScreen(dc, model.Levels[0].Features, ox, oy, _penContourGreen!);
        }

        if (model.MaskedFeatures.Count > 0)
        {
            DrawFeatureTangentsFromModelScreen(dc, model.MaskedFeatures, ox, oy, _penOutlierRed!);
        }

        float objDim = GetModelObjectDimension(model);
        DrawObjectCoordinateFrameScreen(dc, ox, oy, angleDeg: 0f, objectSize: objDim, strokeW);
    }

    private void DrawMatchResultsOverlayScreen(DrawingContext dc, List<MatchResult> matches, ShapeModel? model, double strokeW)
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var drawMode = ViewModel?.OverlayDrawMode ?? OverlayDrawMode;

        for (int idx = 0; idx < matches.Count; idx++)
        {
            var match = matches[idx];
            bool isPrimary = idx == 0;
            Pen contourPen = isPrimary ? _penContourGreen! : _penContourCyan!;
            Pen boxPen = isPrimary ? _penBoxPrimary! : _penBoxSecondary!;
            Brush boxBrush = isPrimary ? _boxPrimaryBrush : _boxSecondaryBrush;

            // 1. Continuous vector contours in Screen Space (always drawn)
            if (match.TransformedContours != null && match.TransformedContours.Count > 0)
            {
                DrawContoursScreen(dc, match.TransformedContours, originX: 0f, originY: 0f, cosA: 1f, sinA: 0f, scale: 1f, contourPen);
                if (match.OutlierCount > 0 && match.FeaturePoints.Count > 0)
                {
                    DrawMatchedFeatureTangentsScreen(dc, match.FeaturePoints, onlyOutliers: true, _penContourGreen!, _penOutlierRed!);
                }
            }
            else if (model != null && model.ModelContours.Count > 0)
            {
                float rad = match.AngleDeg * (MathF.PI / 180f);
                DrawContoursScreen(dc, model.ModelContours, match.X, match.Y, MathF.Cos(rad), MathF.Sin(rad), match.Scale > 1e-4f ? match.Scale : 1f, contourPen);
                if (match.OutlierCount > 0 && match.FeaturePoints.Count > 0)
                {
                    DrawMatchedFeatureTangentsScreen(dc, match.FeaturePoints, onlyOutliers: true, _penContourGreen!, _penOutlierRed!);
                }
            }
            else if (match.FeaturePoints.Count > 0)
            {
                DrawMatchedFeatureTangentsScreen(dc, match.FeaturePoints, onlyOutliers: false, _penContourGreen!, _penOutlierRed!);
            }

            // 2. Center crosshair: drawn for NoScore, Score, and All (hidden for ContourOnly)
            if (drawMode != OverlayDrawMode.ContourOnly)
            {
                DrawCenterCrosshairScreen(dc, match.X, match.Y, size: 8, strokeW);
            }

            // 3. Rotated bounding box & Object Coordinate Frame: drawn ONLY for All
            if (drawMode == OverlayDrawMode.All)
            {
                var boxPts = match.BoundingBox.Points();
                if (boxPts != null && boxPts.Length == 4)
                {
                    var boxGeom = new StreamGeometry();
                    using (var ctx = boxGeom.Open())
                    {
                        ctx.BeginFigure(ImageToScreen(boxPts[0].X, boxPts[0].Y), isFilled: false, isClosed: true);
                        for (int i = 1; i < 4; i++)
                        {
                            ctx.LineTo(ImageToScreen(boxPts[i].X, boxPts[i].Y), isStroked: true, isSmoothJoin: false);
                        }
                    }
                    boxGeom.Freeze();
                    dc.DrawGeometry(null, boxPen, boxGeom);
                }

                float objDim = Math.Max(model?.TemplateWidth ?? 100, model?.TemplateHeight ?? 100) * (match.Scale > 1e-4f ? match.Scale : 1f);
                DrawObjectCoordinateFrameScreen(dc, match.X, match.Y, match.AngleDeg, objDim, strokeW);
            }

            // 4. Score badge: drawn for Score and All (hidden for NoScore and ContourOnly)
            if (drawMode == OverlayDrawMode.Score || drawMode == OverlayDrawMode.All)
            {
                string label = match.FeaturePoints.Count > 0
                    ? $"#{idx + 1} {match.Score:F2} (In:{match.InlierRatio:P0})"
                    : $"#{idx + 1} {match.Score:F2}";
                DrawMatchBadgeScreen(dc, label, match.X, match.Y, isPrimary, boxBrush, pixelsPerDip);
            }
        }
    }

    private void DrawCenterCrosshairScreen(DrawingContext dc, float imgX, float imgY, double size, double strokeW)
    {
        Point pt = ImageToScreen(imgX, imgY);
        Pen crosshairPen = _penCrosshair ?? _penOutlierRed ?? CreateRoundPen(Brushes.Red, strokeW);
        dc.DrawLine(crosshairPen, new Point(pt.X - size, pt.Y), new Point(pt.X + size, pt.Y));
        dc.DrawLine(crosshairPen, new Point(pt.X, pt.Y - size), new Point(pt.X, pt.Y + size));
    }

    private void DrawContoursScreen(
        DrawingContext dc,
        IReadOnlyList<List<Point2f>> contours,
        float originX,
        float originY,
        float cosA,
        float sinA,
        float scale,
        Pen pen)
    {
        var geom = new StreamGeometry();
        bool hasAny = false;
        using (var ctx = geom.Open())
        {
            foreach (var poly in contours)
            {
                if (poly.Count < 2) continue;
                hasAny = true;

                float x0 = originX + (poly[0].X * cosA - poly[0].Y * sinA) * scale;
                float y0 = originY + (poly[0].X * sinA + poly[0].Y * cosA) * scale;
                float dCloseX = poly[0].X - poly[^1].X;
                float dCloseY = poly[0].Y - poly[^1].Y;
                bool isClosed = (dCloseX * dCloseX + dCloseY * dCloseY) * scale * scale < 9.0f;

                ctx.BeginFigure(ImageToScreen(x0, y0), isFilled: false, isClosed: isClosed);
                for (int i = 1; i < poly.Count; i++)
                {
                    float xi = originX + (poly[i].X * cosA - poly[i].Y * sinA) * scale;
                    float yi = originY + (poly[i].X * sinA + poly[i].Y * cosA) * scale;
                    ctx.LineTo(ImageToScreen(xi, yi), isStroked: true, isSmoothJoin: true);
                }
            }
        }
        if (!hasAny) return;
        geom.Freeze();
        dc.DrawGeometry(null, pen, geom);
    }

    private void DrawFeatureTangentsFromModelScreen(
        DrawingContext dc,
        IReadOnlyList<ShapeFeature> features,
        float originX,
        float originY,
        Pen pen)
    {
        const float halfLen = 0.6f;
        var geom = new StreamGeometry();
        bool hasAny = false;
        using (var ctx = geom.Open())
        {
            foreach (var f in features)
            {
                hasAny = true;
                float px = originX + f.X;
                float py = originY + f.Y;
                float tx = -f.Dy;
                float ty = f.Dx;
                ctx.BeginFigure(ImageToScreen(px - halfLen * tx, py - halfLen * ty), isFilled: false, isClosed: false);
                ctx.LineTo(ImageToScreen(px + halfLen * tx, py + halfLen * ty), isStroked: true, isSmoothJoin: false);
            }
        }
        if (!hasAny) return;
        geom.Freeze();
        dc.DrawGeometry(null, pen, geom);
    }

    private void DrawMatchedFeatureTangentsScreen(
        DrawingContext dc,
        IReadOnlyList<MatchedFeaturePoint> points,
        bool onlyOutliers,
        Pen inlierPen,
        Pen outlierPen)
    {
        const float halfLen = 0.6f;
        var inGeom = onlyOutliers ? null : new StreamGeometry();
        var outGeom = new StreamGeometry();
        bool hasIn = false, hasOut = false;

        using (var inCtx = inGeom?.Open())
        using (var outCtx = outGeom.Open())
        {
            foreach (var fp in points)
            {
                if (fp.IsInlier)
                {
                    if (inCtx == null) continue;
                    hasIn = true;
                    inCtx.BeginFigure(ImageToScreen(fp.X - halfLen * fp.TangentX, fp.Y - halfLen * fp.TangentY), false, false);
                    inCtx.LineTo(ImageToScreen(fp.X + halfLen * fp.TangentX, fp.Y + halfLen * fp.TangentY), true, false);
                }
                else
                {
                    hasOut = true;
                    outCtx.BeginFigure(ImageToScreen(fp.X - halfLen * fp.TangentX, fp.Y - halfLen * fp.TangentY), false, false);
                    outCtx.LineTo(ImageToScreen(fp.X + halfLen * fp.TangentX, fp.Y + halfLen * fp.TangentY), true, false);
                }
            }
        }

        if (hasIn && inGeom != null)
        {
            inGeom.Freeze();
            dc.DrawGeometry(null, inlierPen, inGeom);
        }
        if (hasOut)
        {
            outGeom.Freeze();
            dc.DrawGeometry(null, outlierPen, outGeom);
        }
    }

    private static float GetModelObjectDimension(ShapeModel model)
    {
        float objDim = Math.Max(model.TemplateWidth, model.TemplateHeight);
        if (objDim <= 0 && model.Levels.Count > 0 && model.Levels[0].Features.Count > 0)
        {
            float minFx = float.MaxValue, maxFx = float.MinValue;
            float minFy = float.MaxValue, maxFy = float.MinValue;
            foreach (var f in model.Levels[0].Features)
            {
                if (f.X < minFx) minFx = f.X;
                if (f.X > maxFx) maxFx = f.X;
                if (f.Y < minFy) minFy = f.Y;
                if (f.Y > maxFy) maxFy = f.Y;
            }
            objDim = Math.Max(maxFx - minFx, maxFy - minFy);
        }
        return objDim > 0 ? objDim : 100f;
    }

    private void DrawObjectCoordinateFrameScreen(
        DrawingContext dc,
        float cx,
        float cy,
        float angleDeg,
        float objectSize,
        double strokeW)
    {
        float arrowLenImg = Math.Clamp(objectSize * 0.20f, 12f, 45f);
        float radX = angleDeg * (MathF.PI / 180f);
        float cosX = MathF.Cos(radX);
        float sinX = MathF.Sin(radX);

        float uxX = cosX, uyX = sinX;
        float uxY = -sinX, uyY = cosX;

        var originScr = ImageToScreen(cx, cy);
        var tipXScr = ImageToScreen(cx + arrowLenImg * uxX, cy + arrowLenImg * uyX);
        var tipYScr = ImageToScreen(cx + arrowLenImg * uxY, cy + arrowLenImg * uyY);

        DrawFilledArrowScreen(dc, originScr, tipXScr, _axisXBrush, _penAxisX!, strokeW);
        DrawFilledArrowScreen(dc, originScr, tipYScr, _axisYBrush, _penAxisY!, strokeW);

        double dotR = Math.Clamp(1.8 + strokeW * 0.85, 2.2, 3.8);
        dc.DrawEllipse(_originDotBrush, _penOriginOutline!, originScr, dotR, dotR);

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double fontSize = Math.Clamp(9.5 + strokeW * 1.2, 10.0, 12.5);
        DrawAxisLabelScreen(dc, "X", originScr, tipXScr, _axisXBrush, fontSize, pixelsPerDip);
        DrawAxisLabelScreen(dc, "Y", originScr, tipYScr, _axisYBrush, fontSize, pixelsPerDip);
    }

    private static void DrawFilledArrowScreen(DrawingContext dc, Point startScr, Point tipScr, Brush brush, Pen pen, double strokeW)
    {
        double dx = tipScr.X - startScr.X;
        double dy = tipScr.Y - startScr.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 2.0)
        {
            dc.DrawLine(pen, startScr, tipScr);
            return;
        }

        double ux = dx / len;
        double uy = dy / len;
        double vx = -uy;
        double vy = ux;

        double headLen = Math.Clamp(len * 0.30, Math.Max(4.5, strokeW * 3.2), 14.0);
        double headHalfW = headLen * 0.45;

        var shaftEnd = new Point(tipScr.X - ux * (headLen * 0.7), tipScr.Y - uy * (headLen * 0.7));
        dc.DrawLine(pen, startScr, shaftEnd);

        double baseX = tipScr.X - ux * headLen;
        double baseY = tipScr.Y - uy * headLen;
        var tri = new StreamGeometry();
        using (var ctx = tri.Open())
        {
            ctx.BeginFigure(tipScr, isFilled: true, isClosed: true);
            ctx.LineTo(new Point(baseX + vx * headHalfW, baseY + vy * headHalfW), false, false);
            ctx.LineTo(new Point(baseX - vx * headHalfW, baseY - vy * headHalfW), false, false);
        }
        tri.Freeze();
        dc.DrawGeometry(brush, null, tri);
    }

    private static void DrawAxisLabelScreen(
        DrawingContext dc,
        string text,
        Point originScr,
        Point tipScr,
        Brush brush,
        double fontSize,
        double pixelsPerDip)
    {
        double dx = tipScr.X - originScr.X;
        double dy = tipScr.Y - originScr.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 8.0) return;
        double ux = dx / len;
        double uy = dy / len;

        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _overlayTypeface, fontSize, brush, pixelsPerDip);
        double lx = tipScr.X + ux * 7.0 - ft.Width * 0.5;
        double ly = tipScr.Y + uy * 7.0 - ft.Height * 0.5;
        dc.DrawText(ft, new Point(lx, ly));
    }

    private void DrawMatchBadgeScreen(
        DrawingContext dc,
        string text,
        float matchX,
        float matchY,
        bool isPrimary,
        Brush borderBrush,
        double pixelsPerDip)
    {
        var anchorScr = ImageToScreen(matchX, matchY);
        var ft = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _overlayTypeface,
            11.5,
            isPrimary ? _inlierGreenBrush : Brushes.White,
            pixelsPerDip);

        double padX = 5.0;
        double padY = 2.5;
        double boxW = ft.Width + padX * 2;
        double boxH = ft.Height + padY * 2;

        double left = anchorScr.X - boxW * 0.5;
        double top = anchorScr.Y - boxH - 16.0;
        if (top < 6.0) top = anchorScr.Y + 20.0;
        left = Math.Clamp(left, 6.0, Math.Max(6.0, ActualWidth - boxW - 6.0));
        top = Math.Clamp(top, 6.0, Math.Max(6.0, ActualHeight - boxH - 6.0));

        var borderPen = new Pen(borderBrush, 1.0);
        borderPen.Freeze();
        dc.DrawRoundedRectangle(_badgeDarkBg, borderPen, new Rect(left, top, boxW, boxH), 3, 3);
        dc.DrawText(ft, new Point(left + padX, top + padY));
    }

    private void DrawTemplateFeaturePoints(DrawingContext dc, bool skipGreenContours)
    {
        if (ViewModel == null) return;
        var model = ViewModel.GetActiveEditableModel();
        if (model == null) return;

        double strokeW = GetZoomAdaptiveStrokeThickness(_zoom);
        EnsureZoomAdaptivePens(strokeW);

        var smNode = ViewModel.CurrentShapeMatchNode ?? ViewModel.ActiveGraph.Nodes.OfType<ShapeMatchNode>().FirstOrDefault();
        float cx = model.OriginX;
        float cy = model.OriginY;
        float cosA = 1f, sinA = 0f, scale = 1f;

        if (ViewModel.SelectedNode?.Node is not TrainShapeModelNode && smNode != null && smNode.LastMatches.Count > 0)
        {
            var m = smNode.LastMatches[0];
            cx = m.X;
            cy = m.Y;
            float rad = m.AngleDeg * (MathF.PI / 180f);
            cosA = MathF.Cos(rad);
            sinA = MathF.Sin(rad);
            scale = m.Scale > 1e-4f ? m.Scale : 1f;
        }

        // 1. Draw continuous vector contours from ModelContours (#00FF00) if not already rendered by vector overlay
        if (!skipGreenContours)
        {
            if (model.ModelContours != null && model.ModelContours.Count > 0)
            {
                DrawContoursScreen(dc, model.ModelContours, cx, cy, cosA, sinA, scale, _penContourGreen!);
            }
            else
            {
                var lvl0 = model.Levels.FirstOrDefault(l => l.Level == 0);
                if (lvl0 != null && lvl0.Features.Count > 0)
                {
                    double featR = Math.Clamp(1.2 + strokeW * 0.45, 1.2, 2.4);
                    var snapshot = lvl0.Features.ToArray();
                    foreach (var f in snapshot)
                    {
                        float sx = cx + (f.X * cosA - f.Y * sinA) * scale;
                        float sy = cy + (f.X * sinA + f.Y * cosA) * scale;
                        var ptScr = ImageToScreen(sx, sy);
                        dc.DrawEllipse(_featurePointBrush, null, ptScr, featR, featR);
                    }
                }
            }
        }

        // 2. If in Erase mode, draw masked / erased points in red so user sees what was removed
        if (ViewModel.InteractionMode == CanvasInteractionMode.BrushErase ||
            ViewModel.InteractionMode == CanvasInteractionMode.BoxErase)
        {
            if (model.MaskedFeatures != null && model.MaskedFeatures.Count > 0)
            {
                double dotR = Math.Clamp(1.6 + strokeW * 0.55, 1.8, 3.0);
                foreach (var mf in model.MaskedFeatures)
                {
                    float sx = cx + (mf.X * cosA - mf.Y * sinA) * scale;
                    float sy = cy + (mf.X * sinA + mf.Y * cosA) * scale;
                    var ptScr = ImageToScreen(sx, sy);
                    dc.DrawEllipse(_maskedPointBrush, null, ptScr, dotR, dotR);
                }
            }
        }
    }

    private void DrawActiveNodeGizmos(DrawingContext dc)
    {
        if (ViewModel == null) return;

        if (ViewModel.CurrentCaliperCircleNode is { } cNode)
        {
            var centerScr = ImageToScreen(cNode.LastEffectiveCenter.X, cNode.LastEffectiveCenter.Y);
            double rNom = Math.Max(4, cNode.ExpectedRadius) * _zoom;
            double rInner = Math.Max(2, cNode.ExpectedRadius - cNode.SearchRange) * _zoom;
            double rOuter = (cNode.ExpectedRadius + cNode.SearchRange) * _zoom;

            // Inner & Outer orange search rings + Purple nominal circle
            dc.DrawEllipse(null, _orangeRingPen, centerScr, rOuter, rOuter);
            dc.DrawEllipse(null, _orangeRingPen, centerScr, rInner, rInner);
            dc.DrawEllipse(null, _purpleNomPen, centerScr, rNom, rNom);

            // 8 directional scan arrows (Outward or Inward)
            for (int i = 0; i < 8; i++)
            {
                double ang = i * (Math.PI / 4.0);
                double cosA = Math.Cos(ang);
                double sinA = Math.Sin(ang);
                double rFrom = cNode.ScanDirection == CircleScanDirection.Outward ? rInner : rOuter;
                double rTo = cNode.ScanDirection == CircleScanDirection.Outward ? rOuter : rInner;
                var pStart = new Point(centerScr.X + cosA * rFrom, centerScr.Y + sinA * rFrom);
                var pEnd = new Point(centerScr.X + cosA * rTo, centerScr.Y + sinA * rTo);
                DrawArrowLine(dc, pStart, pEnd, _orangeDashPen);
            }

            // 3 Interactive Handles: Center, Radius (Right), Annulus (Top)
            DrawSquareHandle(dc, centerScr, Brushes.Gold);
            DrawSquareHandle(dc, new Point(centerScr.X + rNom, centerScr.Y), Brushes.White);
            DrawSquareHandle(dc, new Point(centerScr.X, centerScr.Y - rOuter), Brushes.Orange);
        }
        else if (ViewModel.CurrentSearchLineNode is { } lNode)
        {
            DrawLineRakeGizmo(dc, lNode.LastEffectiveStart, lNode.LastEffectiveEnd, lNode.SearchHalfWidth, true);
        }
        else if (ViewModel.CurrentSearchCornerNode is { } crNode)
        {
            DrawLineRakeGizmo(dc, crNode.LastEffL1Start, crNode.LastEffL1End, crNode.SearchHalfWidth, false);
            DrawLineRakeGizmo(dc, crNode.LastEffL2Start, crNode.LastEffL2End, crNode.SearchHalfWidth, false);
        }
        else if (ViewModel.CurrentRoiRectNode is { } rNode)
        {
            float cx = rNode.X + rNode.Width * 0.5f;
            float cy = rNode.Y + rNode.Height * 0.5f;
            float rad = rNode.AngleDeg * (MathF.PI / 180f);
            float ux = MathF.Cos(rad);
            float uy = MathF.Sin(rad);
            float vx = -uy;
            float vy = ux;
            float hw = Math.Max(5f, rNode.Width * 0.5f);
            float hh = Math.Max(5f, rNode.Height * 0.5f);

            var tl = ImageToScreen(cx - hw * ux - hh * vx, cy - hw * uy - hh * vy);
            var tr = ImageToScreen(cx + hw * ux - hh * vx, cy + hw * uy - hh * vy);
            var br = ImageToScreen(cx + hw * ux + hh * vx, cy + hw * uy + hh * vy);
            var bl = ImageToScreen(cx - hw * ux + hh * vx, cy - hw * uy + hh * vy);
            var centerScr = ImageToScreen(cx, cy);
            var rightMidScr = ImageToScreen(cx + hw * ux, cy + hw * uy);
            var rotHandleScr = new Point(rightMidScr.X + ux * 24.0, rightMidScr.Y + uy * 24.0);

            var rectGeom = new StreamGeometry();
            using (var gctx = rectGeom.Open())
            {
                gctx.BeginFigure(tl, isFilled: true, isClosed: true);
                gctx.LineTo(tr, isStroked: true, isSmoothJoin: false);
                gctx.LineTo(br, isStroked: true, isSmoothJoin: false);
                gctx.LineTo(bl, isStroked: true, isSmoothJoin: false);
            }
            rectGeom.Freeze();
            dc.DrawGeometry(_roiFillBrush, _orangeRingPen, rectGeom);

            dc.DrawLine(_cyanArrowPen, centerScr, rotHandleScr);
            DrawSquareHandle(dc, centerScr, Brushes.Gold);
            DrawSquareHandle(dc, tl, Brushes.White);
            DrawSquareHandle(dc, tr, Brushes.White);
            DrawSquareHandle(dc, br, Brushes.White);
            DrawSquareHandle(dc, bl, Brushes.White);
            dc.DrawEllipse(Brushes.LimeGreen, _handlePen, rotHandleScr, 6, 6);

            var angleFt = new FormattedText(
                $"{rNode.AngleDeg:F1}°",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _overlayTypeface,
                11.0,
                Brushes.Cyan,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var labelPos = new Point(rotHandleScr.X + 8, rotHandleScr.Y - angleFt.Height * 0.5);
            dc.DrawRoundedRectangle(_calibBadgeBg, null, new Rect(labelPos.X - 3, labelPos.Y - 1, angleFt.Width + 6, angleFt.Height + 2), 3, 3);
            dc.DrawText(angleFt, labelPos);
        }
        else if (ViewModel.CurrentRoiCircleNode is { } rcNode)
        {
            var centerScr = ImageToScreen(rcNode.CenterX, rcNode.CenterY);
            double rScr = Math.Max(4, rcNode.Radius) * _zoom;
            dc.DrawEllipse(_roiFillBrush, _orangeRingPen, centerScr, rScr, rScr);
            DrawSquareHandle(dc, centerScr, Brushes.Gold);
            DrawSquareHandle(dc, new Point(centerScr.X + rScr, centerScr.Y), Brushes.White);
        }
        else if (ViewModel.CurrentRoiPolygonNode is { } polyNode && polyNode.Points.Count >= 3)
        {
            var scrPts = polyNode.Points.Select(p => ImageToScreen(p.X, p.Y)).ToArray();
            var polyGeom = new StreamGeometry();
            using (var gctx = polyGeom.Open())
            {
                gctx.BeginFigure(scrPts[0], isFilled: true, isClosed: true);
                for (int i = 1; i < scrPts.Length; i++)
                {
                    gctx.LineTo(scrPts[i], isStroked: true, isSmoothJoin: false);
                }
            }
            polyGeom.Freeze();
            dc.DrawGeometry(_roiFillBrush, _orangeRingPen, polyGeom);

            double sumX = 0, sumY = 0;
            for (int i = 0; i < scrPts.Length; i++)
            {
                sumX += scrPts[i].X;
                sumY += scrPts[i].Y;
                DrawSquareHandle(dc, scrPts[i], i == 0 ? Brushes.Cyan : Brushes.White);
            }
            var centroidScr = new Point(sumX / scrPts.Length, sumY / scrPts.Length);
            DrawSquareHandle(dc, centroidScr, Brushes.Gold);
        }
        else if (ViewModel.CurrentCalibrationNode is { } calNode)
        {
            DrawCalibrationRulerGizmo(
                dc,
                new Point2f(calNode.Point1X, calNode.Point1Y),
                new Point2f(calNode.Point2X, calNode.Point2Y),
                $"{calNode.PixelDistance:F2} px = {calNode.KnownRealLength:F2} {calNode.UnitName} (1 px = {calNode.MmPerPixel:F5} {calNode.UnitName})");
        }
    }

    private void DrawCalibrationRulerGizmo(DrawingContext dc, Point2f p1Img, Point2f p2Img, string badgeText)
    {
        var p1Scr = ImageToScreen(p1Img.X, p1Img.Y);
        var p2Scr = ImageToScreen(p2Img.X, p2Img.Y);

        dc.DrawLine(_cyanArrowPen, p1Scr, p2Scr);

        double dx = p2Scr.X - p1Scr.X;
        double dy = p2Scr.Y - p1Scr.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len > 4)
        {
            double nx = -dy / len;
            double ny = dx / len;
            const double tickLen = 10.0;
            dc.DrawLine(_orangeRingPen,
                new Point(p1Scr.X - nx * tickLen, p1Scr.Y - ny * tickLen),
                new Point(p1Scr.X + nx * tickLen, p1Scr.Y + ny * tickLen));
            dc.DrawLine(_orangeRingPen,
                new Point(p2Scr.X - nx * tickLen, p2Scr.Y - ny * tickLen),
                new Point(p2Scr.X + nx * tickLen, p2Scr.Y + ny * tickLen));
        }

        DrawSquareHandle(dc, p1Scr, Brushes.Gold);
        DrawSquareHandle(dc, p2Scr, Brushes.White);

        var ft = new FormattedText(
            badgeText,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            12.0,
            Brushes.Cyan,
            1.25);

        double midX = (p1Scr.X + p2Scr.X) * 0.5;
        double midY = (p1Scr.Y + p2Scr.Y) * 0.5 - 24;
        var pillRect = new Rect(midX - ft.Width * 0.5 - 8, midY - 3, ft.Width + 16, ft.Height + 6);
        dc.DrawRoundedRectangle(_calibBadgeBg, _cyanArrowPen, pillRect, 4, 4);
        dc.DrawText(ft, new Point(midX - ft.Width * 0.5, midY));
    }

    private void DrawLineRakeGizmo(DrawingContext dc, Point2f p1Img, Point2f p2Img, float halfWidth, bool drawWidthHandle)
    {
        var rr = CaliperTool.PpwToRotatedRect(p1Img, p2Img, halfWidth * 2f);
        var pts = rr.Points();
        var sPts = new Point[4];
        for (int i = 0; i < 4; i++) sPts[i] = ImageToScreen(pts[i].X, pts[i].Y);

        for (int i = 0; i < 4; i++)
        {
            dc.DrawLine(_orangeRingPen, sPts[i], sPts[(i + 1) % 4]);
        }

        var p1Scr = ImageToScreen(p1Img.X, p1Img.Y);
        var p2Scr = ImageToScreen(p2Img.X, p2Img.Y);
        dc.DrawLine(_purpleNomPen, p1Scr, p2Scr);

        // Normal search arrow at midpoint
        float dx = p2Img.X - p1Img.X;
        float dy = p2Img.Y - p1Img.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len > 2f)
        {
            float nx = -dy / len;
            float ny = dx / len;
            var midImg = new Point2f((p1Img.X + p2Img.X) * 0.5f, (p1Img.Y + p2Img.Y) * 0.5f);
            var aFrom = ImageToScreen(midImg.X - nx * halfWidth, midImg.Y - ny * halfWidth);
            var aTo = ImageToScreen(midImg.X + nx * halfWidth, midImg.Y + ny * halfWidth);
            DrawArrowLine(dc, aFrom, aTo, _cyanArrowPen);

            if (drawWidthHandle)
            {
                DrawSquareHandle(dc, aTo, Brushes.Orange);
            }
        }

        DrawSquareHandle(dc, p1Scr, Brushes.Gold);
        DrawSquareHandle(dc, p2Scr, Brushes.White);
    }

    private static void DrawSquareHandle(DrawingContext dc, Point center, Brush fill)
    {
        dc.DrawRectangle(fill, _handlePen, new Rect(center.X - 5, center.Y - 5, 10, 10));
    }

    private static void DrawArrowLine(DrawingContext dc, Point p1, Point p2, Pen pen)
    {
        dc.DrawLine(pen, p1, p2);
        double dx = p2.X - p1.X;
        double dy = p2.Y - p1.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 8) return;
        double ux = dx / len;
        double uy = dy / len;
        var left = new Point(p2.X - ux * 6 - uy * 3.5, p2.Y - uy * 6 + ux * 3.5);
        var right = new Point(p2.X - ux * 6 + uy * 3.5, p2.Y - uy * 6 - ux * 3.5);
        dc.DrawLine(pen, p2, left);
        dc.DrawLine(pen, p2, right);
    }

    private void DrawPreviewTemporaryContours(DrawingContext dc, IReadOnlyList<Point2f> pts)
    {
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            const double halfTick = 0.35;
            for (int i = 0; i < pts.Count; i++)
            {
                var p = ImageToScreen(pts[i].X, pts[i].Y);
                ctx.BeginFigure(new Point(p.X - halfTick, p.Y), isFilled: false, isClosed: false);
                ctx.LineTo(new Point(p.X + halfTick, p.Y), isStroked: true, isSmoothJoin: false);
            }
        }
        geom.Freeze();
        dc.DrawGeometry(null, _previewContourPen, geom);
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

    private ActiveHandle HitTestGizmoHandles(Point mouseScr)
    {
        if (ViewModel == null) return ActiveHandle.None;
        const double hitDist = 11.0;

        if (ViewModel.CurrentCaliperCircleNode is { } cNode)
        {
            var centerScr = ImageToScreen(cNode.LastEffectiveCenter.X, cNode.LastEffectiveCenter.Y);
            double rNom = Math.Max(4, cNode.ExpectedRadius) * _zoom;
            double rOuter = (cNode.ExpectedRadius + cNode.SearchRange) * _zoom;

            var rHandle = new Point(centerScr.X + rNom, centerScr.Y);
            var aHandle = new Point(centerScr.X, centerScr.Y - rOuter);

            if ((mouseScr - rHandle).Length <= hitDist) return ActiveHandle.CircleRadius;
            if ((mouseScr - aHandle).Length <= hitDist) return ActiveHandle.CircleAnnulus;
            if ((mouseScr - centerScr).Length <= hitDist) return ActiveHandle.CircleCenter;
        }
        else if (ViewModel.CurrentSearchLineNode is { } lNode)
        {
            var p1Scr = ImageToScreen(lNode.LastEffectiveStart.X, lNode.LastEffectiveStart.Y);
            var p2Scr = ImageToScreen(lNode.LastEffectiveEnd.X, lNode.LastEffectiveEnd.Y);
            if ((mouseScr - p1Scr).Length <= hitDist) return ActiveHandle.LineStart;
            if ((mouseScr - p2Scr).Length <= hitDist) return ActiveHandle.LineEnd;

            float dx = lNode.LastEffectiveEnd.X - lNode.LastEffectiveStart.X;
            float dy = lNode.LastEffectiveEnd.Y - lNode.LastEffectiveStart.Y;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len > 2f)
            {
                float nx = -dy / len, ny = dx / len;
                var mid = new Point2f((lNode.LastEffectiveStart.X + lNode.LastEffectiveEnd.X) * 0.5f, (lNode.LastEffectiveStart.Y + lNode.LastEffectiveEnd.Y) * 0.5f);
                var wScr = ImageToScreen(mid.X + nx * lNode.SearchHalfWidth, mid.Y + ny * lNode.SearchHalfWidth);
                if ((mouseScr - wScr).Length <= hitDist) return ActiveHandle.LineWidth;
            }
        }
        else if (ViewModel.CurrentSearchCornerNode is { } crNode)
        {
            if ((mouseScr - ImageToScreen(crNode.LastEffL1Start.X, crNode.LastEffL1Start.Y)).Length <= hitDist) return ActiveHandle.CornerL1Start;
            if ((mouseScr - ImageToScreen(crNode.LastEffL1End.X, crNode.LastEffL1End.Y)).Length <= hitDist) return ActiveHandle.CornerL1End;
            if ((mouseScr - ImageToScreen(crNode.LastEffL2Start.X, crNode.LastEffL2Start.Y)).Length <= hitDist) return ActiveHandle.CornerL2Start;
            if ((mouseScr - ImageToScreen(crNode.LastEffL2End.X, crNode.LastEffL2End.Y)).Length <= hitDist) return ActiveHandle.CornerL2End;
        }
        else if (ViewModel.CurrentRoiRectNode is { } rNode)
        {
            float cx = rNode.X + rNode.Width * 0.5f;
            float cy = rNode.Y + rNode.Height * 0.5f;
            float rad = rNode.AngleDeg * (MathF.PI / 180f);
            float ux = MathF.Cos(rad);
            float uy = MathF.Sin(rad);
            float vx = -uy;
            float vy = ux;
            float hw = Math.Max(5f, rNode.Width * 0.5f);
            float hh = Math.Max(5f, rNode.Height * 0.5f);

            var tl = ImageToScreen(cx - hw * ux - hh * vx, cy - hw * uy - hh * vy);
            var tr = ImageToScreen(cx + hw * ux - hh * vx, cy + hw * uy - hh * vy);
            var br = ImageToScreen(cx + hw * ux + hh * vx, cy + hw * uy + hh * vy);
            var bl = ImageToScreen(cx - hw * ux + hh * vx, cy - hw * uy + hh * vy);
            var centerScr = ImageToScreen(cx, cy);
            var rightMidScr = ImageToScreen(cx + hw * ux, cy + hw * uy);
            var rotHandleScr = new Point(rightMidScr.X + ux * 24.0, rightMidScr.Y + uy * 24.0);

            if ((mouseScr - rotHandleScr).Length <= hitDist) return ActiveHandle.RoiRotation;
            if ((mouseScr - tl).Length <= hitDist) return ActiveHandle.RoiTopLeft;
            if ((mouseScr - tr).Length <= hitDist) return ActiveHandle.RoiTopRight;
            if ((mouseScr - br).Length <= hitDist) return ActiveHandle.RoiBottomRight;
            if ((mouseScr - bl).Length <= hitDist) return ActiveHandle.RoiBottomLeft;
            if ((mouseScr - centerScr).Length <= hitDist) return ActiveHandle.RoiRectCenter;
        }
        else if (ViewModel.CurrentRoiCircleNode is { } rcNode)
        {
            var centerScr = ImageToScreen(rcNode.CenterX, rcNode.CenterY);
            double rScr = Math.Max(4, rcNode.Radius) * _zoom;
            var rHandle = new Point(centerScr.X + rScr, centerScr.Y);
            if ((mouseScr - rHandle).Length <= hitDist) return ActiveHandle.RoiCircleRadius;
            if ((mouseScr - centerScr).Length <= hitDist) return ActiveHandle.RoiCircleCenter;
        }
        else if (ViewModel.CurrentRoiPolygonNode is { } polyNode && polyNode.Points.Count >= 3)
        {
            double sumX = 0, sumY = 0;
            for (int i = 0; i < polyNode.Points.Count; i++)
            {
                var vScr = ImageToScreen(polyNode.Points[i].X, polyNode.Points[i].Y);
                sumX += vScr.X;
                sumY += vScr.Y;
                if ((mouseScr - vScr).Length <= hitDist)
                {
                    _draggedPolygonVertexIndex = i;
                    return ActiveHandle.RoiPolygonVertex;
                }
            }
            var centroidScr = new Point(sumX / polyNode.Points.Count, sumY / polyNode.Points.Count);
            if ((mouseScr - centroidScr).Length <= hitDist)
            {
                return ActiveHandle.RoiPolygonCenter;
            }
        }
        else if (ViewModel.CurrentCalibrationNode is { } calNode)
        {
            var p1Scr = ImageToScreen(calNode.Point1X, calNode.Point1Y);
            var p2Scr = ImageToScreen(calNode.Point2X, calNode.Point2Y);
            if ((mouseScr - p1Scr).Length <= hitDist) return ActiveHandle.CalibPoint1;
            if ((mouseScr - p2Scr).Length <= hitDist) return ActiveHandle.CalibPoint2;
        }

        return ActiveHandle.None;
    }

    private void UpdateHudPixelInfo(Point mouseScreenPos)
    {
        if (ViewModel?.CurrentPreviewSource is not { } src)
        {
            _hudPixelInfo = string.Empty;
            return;
        }

        Point imgPos = ScreenToImage(mouseScreenPos);
        int ix = (int)Math.Floor(imgPos.X);
        int iy = (int)Math.Floor(imgPos.Y);

        if (ix >= 0 && ix < src.PixelWidth && iy >= 0 && iy < src.PixelHeight)
        {
            int bpp = src.Format.BitsPerPixel;
            int destStride = Math.Max(1, (bpp + 7) / 8);
            try
            {
                src.CopyPixels(new Int32Rect(ix, iy, 1, 1), _pixelBuffer, Math.Max(destStride, 4), 0);
                if (bpp == 8)
                {
                    _hudPixelInfo = $"W:{src.PixelWidth}, H:{src.PixelHeight} | X:{ix}, Y:{iy} | Gray:{_pixelBuffer[0]}";
                }
                else
                {
                    _hudPixelInfo = $"W:{src.PixelWidth}, H:{src.PixelHeight} | X:{ix}, Y:{iy} | R:{_pixelBuffer[2]}, G:{_pixelBuffer[1]}, B:{_pixelBuffer[0]}";
                }
            }
            catch
            {
                _hudPixelInfo = $"W:{src.PixelWidth}, H:{src.PixelHeight} | X:{ix}, Y:{iy}";
            }
        }
        else
        {
            _hudPixelInfo = $"W:{src.PixelWidth}, H:{src.PixelHeight} | X:---, Y:---";
        }
    }

    private void DrawHudPixelInspectionBar(DrawingContext dc)
    {
        if (string.IsNullOrEmpty(_hudPixelInfo)) return;

        var ft = new FormattedText(
            _hudPixelInfo,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _hudTypeface,
            12,
            Brushes.LimeGreen,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        double hudY = Math.Max(10, ActualHeight - 58);
        Rect bgRect = new Rect(10, hudY, ft.Width + 16, 22);
        dc.DrawRoundedRectangle(_hudBgBrush, null, bgRect, 4, 4);
        dc.DrawText(ft, new Point(18, hudY + 3));
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _isMouseInside = true;
        var pos = e.GetPosition(this);
        _currentMouseScreen = pos;
        UpdateHudPixelInfo(pos);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _isMouseInside = false;
        if (ViewModel?.CurrentPreviewSource is { } src)
        {
            _hudPixelInfo = $"W:{src.PixelWidth}, H:{src.PixelHeight} | X:---, Y:---";
        }
        else
        {
            _hudPixelInfo = string.Empty;
        }
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        var pos = e.GetPosition(this);
        _currentMouseScreen = pos;

        if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Left)
        {
            FitToScreen();
            e.Handled = true;
            return;
        }

        // Polygon ROI right-click: remove vertex (if >3) or insert vertex on nearest edge
        if (e.ChangedButton == MouseButton.Right && ViewModel?.CurrentRoiPolygonNode is { } polyNode && polyNode.Points.Count >= 3)
        {
            const double hitDist = 10.0;
            for (int i = 0; i < polyNode.Points.Count; i++)
            {
                var vScr = ImageToScreen(polyNode.Points[i].X, polyNode.Points[i].Y);
                if ((pos - vScr).Length <= hitDist)
                {
                    if (polyNode.Points.Count > 3)
                    {
                        polyNode.Points.RemoveAt(i);
                        InvalidateVisual();
                        if (ViewModel.AutoRerunOnMouseUp)
                            _ = ViewModel.RunPipelineAsync();
                    }
                    e.Handled = true;
                    return;
                }
            }

            int bestEdgeIdx = -1;
            double bestEdgeDist = hitDist;
            for (int i = 0; i < polyNode.Points.Count; i++)
            {
                var aScr = ImageToScreen(polyNode.Points[i].X, polyNode.Points[i].Y);
                var bScr = ImageToScreen(polyNode.Points[(i + 1) % polyNode.Points.Count].X, polyNode.Points[(i + 1) % polyNode.Points.Count].Y);
                double d = DistancePointToSegment(pos, aScr, bScr);
                if (d <= bestEdgeDist)
                {
                    bestEdgeDist = d;
                    bestEdgeIdx = i;
                }
            }

            if (bestEdgeIdx >= 0)
            {
                var imgPt = ScreenToImage(pos);
                polyNode.Points.Insert(bestEdgeIdx + 1, new Point2f((float)imgPt.X, (float)imgPt.Y));
                InvalidateVisual();
                if (ViewModel.AutoRerunOnMouseUp)
                    _ = ViewModel.RunPipelineAsync();
                e.Handled = true;
                return;
            }
        }

        // Middle or Right button always pans
        if (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right)
        {
            _isPanning = true;
            _lastMousePos = pos;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && ViewModel != null)
        {
            var imgPt = ScreenToImage(pos);

            // 1. Check special interaction modes (DrawTemplateRoi, BrushErase, BoxErase)
            if (ViewModel.InteractionMode == CanvasInteractionMode.BrushErase)
            {
                _isBrushErasing = true;
                _brushFirstSampleInStroke = true;
                ApplyBrushAtImagePoint(imgPt);
                CaptureMouse();
                e.Handled = true;
                return;
            }

            if (ViewModel.InteractionMode == CanvasInteractionMode.DrawTemplateRoi ||
                ViewModel.InteractionMode == CanvasInteractionMode.BoxErase)
            {
                _isDraggingMarquee = true;
                _marqueeStartImg = imgPt;
                _marqueeCurrentImg = imgPt;
                CaptureMouse();
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            // 2. Hit-test interactive gizmo handles
            var hit = HitTestGizmoHandles(pos);
            if (hit != ActiveHandle.None)
            {
                _draggedHandle = hit;
                CaptureMouse();
                e.Handled = true;
                return;
            }

            // 3. Default: Pan image
            _isPanning = true;
            _lastMousePos = pos;
            CaptureMouse();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pos = e.GetPosition(this);
        _currentMouseScreen = pos;
        _isMouseInside = true;

        UpdateHudPixelInfo(pos);

        if (_isBrushErasing && ViewModel != null)
        {
            ApplyBrushAtImagePoint(ScreenToImage(pos));
            InvalidateVisual();
            return;
        }

        if (_isDraggingMarquee)
        {
            _marqueeCurrentImg = ScreenToImage(pos);
            InvalidateVisual();
            return;
        }

        if (_draggedHandle != ActiveHandle.None && ViewModel != null)
        {
            UpdateDraggedGizmo(ScreenToImage(pos));
            InvalidateVisual();
            return;
        }

        if (_isPanning)
        {
            _pan.X += (pos.X - _lastMousePos.X);
            _pan.Y += (pos.Y - _lastMousePos.Y);
            _lastMousePos = pos;
            InvalidateVisual();
            return;
        }

        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);

        if (_isBrushErasing)
        {
            _isBrushErasing = false;
            ReleaseMouseCapture();
            if (ViewModel?.AutoRerunOnMouseUp == true)
                _ = ViewModel.RunPipelineAsync();
            return;
        }

        if (_isDraggingMarquee && ViewModel != null)
        {
            _isDraggingMarquee = false;
            ReleaseMouseCapture();

            if (ViewModel.InteractionMode == CanvasInteractionMode.DrawTemplateRoi &&
                ViewModel.CurrentCalibrationNode is { } calNode)
            {
                double lineDist = (_marqueeCurrentImg - _marqueeStartImg).Length;
                if (lineDist > 3.0)
                {
                    calNode.SetLinePoints(
                        (float)_marqueeStartImg.X,
                        (float)_marqueeStartImg.Y,
                        (float)_marqueeCurrentImg.X,
                        (float)_marqueeCurrentImg.Y);
                    ViewModel.InteractionMode = CanvasInteractionMode.PanOrGizmo;
                    _ = ViewModel.RunPipelineAsync();
                }
                InvalidateVisual();
                return;
            }

            float x = (float)Math.Min(_marqueeStartImg.X, _marqueeCurrentImg.X);
            float y = (float)Math.Min(_marqueeStartImg.Y, _marqueeCurrentImg.Y);
            float w = (float)Math.Abs(_marqueeCurrentImg.X - _marqueeStartImg.X);
            float h = (float)Math.Abs(_marqueeCurrentImg.Y - _marqueeStartImg.Y);

            if (w > 4 && h > 4)
            {
                if (ViewModel.InteractionMode == CanvasInteractionMode.DrawTemplateRoi)
                {
                    ViewModel.ApplyTemplateRoiOnCanvas(new Rect2f(x, y, w, h));
                }
                else if (ViewModel.InteractionMode == CanvasInteractionMode.BoxErase)
                {
                    var model = ViewModel.GetActiveEditableModel();
                    if (model != null)
                    {
                        // If ShapeMatchNode matched at an offset, transform box to model space
                        Rect2f targetRect = MapSceneRectToModelTemplateRect(new Rect2f(x, y, w, h));
                        model.RemoveFeaturesByRect(targetRect, recordUndo: true);
                        if (ViewModel.AutoRerunOnMouseUp)
                            _ = ViewModel.RunPipelineAsync();
                    }
                }
            }
            InvalidateVisual();
            return;
        }

        if (_draggedHandle != ActiveHandle.None)
        {
            _draggedHandle = ActiveHandle.None;
            _draggedPolygonVertexIndex = -1;
            ReleaseMouseCapture();
            if (ViewModel?.AutoRerunOnMouseUp == true)
                _ = ViewModel.RunPipelineAsync();
            return;
        }

        if (_isPanning)
        {
            _isPanning = false;
            ReleaseMouseCapture();
        }
    }

    private void ApplyBrushAtImagePoint(Point imgPt)
    {
        var model = ViewModel?.GetActiveEditableModel();
        if (model == null || ViewModel == null) return;

        var tplPt = MapScenePointToModelTemplatePoint(new Point2f((float)imgPt.X, (float)imgPt.Y));
        int removed = model.RemoveFeaturesByBrush(tplPt, (float)ViewModel.EraserBrushRadius, recordUndo: _brushFirstSampleInStroke);
        if (removed > 0)
        {
            _brushFirstSampleInStroke = false;
        }
    }

    private Point2f MapScenePointToModelTemplatePoint(Point2f scenePt)
    {
        var model = ViewModel?.GetActiveEditableModel();
        if (model == null) return scenePt;

        var smNode = ViewModel?.CurrentShapeMatchNode
            ?? ViewModel?.ActiveGraph.Nodes.OfType<ShapeMatchNode>().FirstOrDefault();
        if (smNode != null && smNode.LastMatches.Count > 0)
        {
            var m = smNode.LastMatches[0];
            float dx = scenePt.X - m.X;
            float dy = scenePt.Y - m.Y;
            float rad = -m.AngleDeg * (MathF.PI / 180f);
            float invScale = m.Scale > 1e-4f ? (1f / m.Scale) : 1f;
            float rx = (dx * MathF.Cos(rad) - dy * MathF.Sin(rad)) * invScale;
            float ry = (dx * MathF.Sin(rad) + dy * MathF.Cos(rad)) * invScale;
            return new Point2f(model.OriginX + rx, model.OriginY + ry);
        }
        return scenePt;
    }

    private Rect2f MapSceneRectToModelTemplateRect(Rect2f sceneRect)
    {
        var c = MapScenePointToModelTemplatePoint(new Point2f(sceneRect.X + sceneRect.Width * 0.5f, sceneRect.Y + sceneRect.Height * 0.5f));
        return new Rect2f(c.X - sceneRect.Width * 0.5f, c.Y - sceneRect.Height * 0.5f, sceneRect.Width, sceneRect.Height);
    }

    private void UpdateDraggedGizmo(Point imgPt)
    {
        if (ViewModel == null) return;
        var pt2f = new Point2f((float)imgPt.X, (float)imgPt.Y);

        if (ViewModel.CurrentCaliperCircleNode is { } cNode)
        {
            switch (_draggedHandle)
            {
                case ActiveHandle.CircleCenter:
                    var refPt = cNode.LastActiveFixture.InverseTransform(pt2f);
                    cNode.CenterX = refPt.X;
                    cNode.CenterY = refPt.Y;
                    typeof(CaliperCircleNode).GetProperty(nameof(CaliperCircleNode.LastEffectiveCenter))?.SetValue(cNode, pt2f);
                    break;
                case ActiveHandle.CircleRadius:
                    float dx = pt2f.X - cNode.LastEffectiveCenter.X;
                    float dy = pt2f.Y - cNode.LastEffectiveCenter.Y;
                    cNode.ExpectedRadius = Math.Max(6f, MathF.Sqrt(dx * dx + dy * dy));
                    break;
                case ActiveHandle.CircleAnnulus:
                    float adx = pt2f.X - cNode.LastEffectiveCenter.X;
                    float ady = pt2f.Y - cNode.LastEffectiveCenter.Y;
                    float dist = MathF.Sqrt(adx * adx + ady * ady);
                    cNode.SearchRange = Math.Clamp(MathF.Abs(dist - cNode.ExpectedRadius), 4f, 150f);
                    break;
            }
        }
        else if (ViewModel.CurrentSearchLineNode is { } lNode)
        {
            switch (_draggedHandle)
            {
                case ActiveHandle.LineStart:
                    var refP1 = lNode.LastActiveFixture.InverseTransform(pt2f);
                    lNode.StartX = refP1.X;
                    lNode.StartY = refP1.Y;
                    typeof(SearchLineNode).GetProperty(nameof(SearchLineNode.LastEffectiveStart))?.SetValue(lNode, pt2f);
                    break;
                case ActiveHandle.LineEnd:
                    var refP2 = lNode.LastActiveFixture.InverseTransform(pt2f);
                    lNode.EndX = refP2.X;
                    lNode.EndY = refP2.Y;
                    typeof(SearchLineNode).GetProperty(nameof(SearchLineNode.LastEffectiveEnd))?.SetValue(lNode, pt2f);
                    break;
                case ActiveHandle.LineWidth:
                    var mid = new Point2f((lNode.LastEffectiveStart.X + lNode.LastEffectiveEnd.X) * 0.5f, (lNode.LastEffectiveStart.Y + lNode.LastEffectiveEnd.Y) * 0.5f);
                    float wDist = MathF.Sqrt((pt2f.X - mid.X) * (pt2f.X - mid.X) + (pt2f.Y - mid.Y) * (pt2f.Y - mid.Y));
                    lNode.SearchHalfWidth = Math.Clamp(wDist, 5f, 150f);
                    break;
            }
        }
        else if (ViewModel.CurrentSearchCornerNode is { } crNode)
        {
            var refPt = crNode.LastActiveFixture.InverseTransform(pt2f);
            switch (_draggedHandle)
            {
                case ActiveHandle.CornerL1Start:
                    crNode.Line1StartX = refPt.X; crNode.Line1StartY = refPt.Y;
                    typeof(SearchCornerNode).GetProperty(nameof(SearchCornerNode.LastEffL1Start))?.SetValue(crNode, pt2f);
                    break;
                case ActiveHandle.CornerL1End:
                    crNode.Line1EndX = refPt.X; crNode.Line1EndY = refPt.Y;
                    typeof(SearchCornerNode).GetProperty(nameof(SearchCornerNode.LastEffL1End))?.SetValue(crNode, pt2f);
                    break;
                case ActiveHandle.CornerL2Start:
                    crNode.Line2StartX = refPt.X; crNode.Line2StartY = refPt.Y;
                    typeof(SearchCornerNode).GetProperty(nameof(SearchCornerNode.LastEffL2Start))?.SetValue(crNode, pt2f);
                    break;
                case ActiveHandle.CornerL2End:
                    crNode.Line2EndX = refPt.X; crNode.Line2EndY = refPt.Y;
                    typeof(SearchCornerNode).GetProperty(nameof(SearchCornerNode.LastEffL2End))?.SetValue(crNode, pt2f);
                    break;
            }
        }
        else if (ViewModel.CurrentRoiRectNode is { } rNode)
        {
            float cx = rNode.X + rNode.Width * 0.5f;
            float cy = rNode.Y + rNode.Height * 0.5f;

            if (_draggedHandle == ActiveHandle.RoiRectCenter)
            {
                rNode.X = pt2f.X - rNode.Width * 0.5f;
                rNode.Y = pt2f.Y - rNode.Height * 0.5f;
            }
            else if (_draggedHandle == ActiveHandle.RoiRotation)
            {
                rNode.AngleDeg = MathF.Atan2(pt2f.Y - cy, pt2f.X - cx) * (180f / MathF.PI);
            }
            else if (Math.Abs(rNode.AngleDeg) < 0.01f && _draggedHandle == ActiveHandle.RoiTopLeft)
            {
                rNode.X = pt2f.X;
                rNode.Y = pt2f.Y;
            }
            else if (Math.Abs(rNode.AngleDeg) < 0.01f && _draggedHandle == ActiveHandle.RoiBottomRight)
            {
                rNode.Width = Math.Max(10f, pt2f.X - rNode.X);
                rNode.Height = Math.Max(10f, pt2f.Y - rNode.Y);
            }
            else if (_draggedHandle is ActiveHandle.RoiTopLeft or ActiveHandle.RoiTopRight or ActiveHandle.RoiBottomRight or ActiveHandle.RoiBottomLeft)
            {
                float rad = rNode.AngleDeg * (MathF.PI / 180f);
                float ux = MathF.Cos(rad);
                float uy = MathF.Sin(rad);
                float vx = -uy;
                float vy = ux;
                float dx = pt2f.X - cx;
                float dy = pt2f.Y - cy;
                float localX = dx * ux + dy * uy;
                float localY = dx * vx + dy * vy;
                rNode.Width = Math.Max(10f, MathF.Abs(localX) * 2f);
                rNode.Height = Math.Max(10f, MathF.Abs(localY) * 2f);
                rNode.X = cx - rNode.Width * 0.5f;
                rNode.Y = cy - rNode.Height * 0.5f;
            }
        }
        else if (ViewModel.CurrentRoiCircleNode is { } rcNode)
        {
            if (_draggedHandle == ActiveHandle.RoiCircleCenter)
            {
                rcNode.CenterX = pt2f.X;
                rcNode.CenterY = pt2f.Y;
            }
            else if (_draggedHandle == ActiveHandle.RoiCircleRadius)
            {
                float dx = pt2f.X - rcNode.CenterX;
                float dy = pt2f.Y - rcNode.CenterY;
                rcNode.Radius = Math.Max(6f, MathF.Sqrt(dx * dx + dy * dy));
            }
        }
        else if (ViewModel.CurrentRoiPolygonNode is { } polyNode && polyNode.Points.Count >= 3)
        {
            if (_draggedHandle == ActiveHandle.RoiPolygonVertex &&
                _draggedPolygonVertexIndex >= 0 &&
                _draggedPolygonVertexIndex < polyNode.Points.Count)
            {
                polyNode.Points[_draggedPolygonVertexIndex] = pt2f;
            }
            else if (_draggedHandle == ActiveHandle.RoiPolygonCenter)
            {
                float cx = 0f, cy = 0f;
                for (int i = 0; i < polyNode.Points.Count; i++)
                {
                    cx += polyNode.Points[i].X;
                    cy += polyNode.Points[i].Y;
                }
                cx /= polyNode.Points.Count;
                cy /= polyNode.Points.Count;
                float dx = pt2f.X - cx;
                float dy = pt2f.Y - cy;
                for (int i = 0; i < polyNode.Points.Count; i++)
                {
                    polyNode.Points[i] = new Point2f(polyNode.Points[i].X + dx, polyNode.Points[i].Y + dy);
                }
            }
        }
        else if (ViewModel.CurrentCalibrationNode is { } calNode)
        {
            if (_draggedHandle == ActiveHandle.CalibPoint1)
            {
                calNode.Point1X = pt2f.X;
                calNode.Point1Y = pt2f.Y;
            }
            else if (_draggedHandle == ActiveHandle.CalibPoint2)
            {
                calNode.Point2X = pt2f.X;
                calNode.Point2Y = pt2f.Y;
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            ViewModel?.UndoModelErase();
            e.Handled = true;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var mousePos = e.GetPosition(this);
        double zoomDelta = e.Delta > 0 ? 1.15 : (1.0 / 1.15);
        double newZoom = Math.Clamp(_zoom * zoomDelta, 0.05, 30.0);

        _pan.X = mousePos.X - (mousePos.X - _pan.X) * (newZoom / _zoom);
        _pan.Y = mousePos.Y - (mousePos.Y - _pan.Y) * (newZoom / _zoom);
        _zoom = newZoom;
        if (ViewModel != null)
            ViewModel.VisionZoomText = $"Zoom {(int)Math.Round(_zoom * 100)}%";

        InvalidateVisual();
    }
}
