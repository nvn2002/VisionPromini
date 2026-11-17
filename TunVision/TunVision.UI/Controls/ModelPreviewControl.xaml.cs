using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenCvSharp;
using TunVision.Core.ShapeMatching;
using Point = System.Windows.Point;

namespace TunVision.UI.Controls;

/// <summary>
/// Dedicated isolated preview control for ShapeModel templates, featuring an alpha
/// checkerboard transparency background and aspect-ratio-preserving zoom-fit vector rendering.
/// </summary>
public partial class ModelPreviewControl : UserControl
{
    public static readonly DependencyProperty ModelProperty =
        DependencyProperty.Register(
            nameof(Model),
            typeof(ShapeModel),
            typeof(ModelPreviewControl),
            new FrameworkPropertyMetadata(null, OnModelPropertyChanged));

    public ShapeModel? Model
    {
        get => (ShapeModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private static void OnModelPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ModelPreviewControl control)
        {
            var model = e.NewValue as ShapeModel;
            if (!control.Dispatcher.CheckAccess())
            {
                control.Dispatcher.BeginInvoke(() => control.RenderModel(model));
                return;
            }
            control.RenderModel(model);
        }
    }

    private ShapeModel? _cachedModel;

    public ModelPreviewControl()
    {
        InitializeComponent();
        SizeChanged += OnControlSizeChanged;
    }

    private void OnControlSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_cachedModel != null)
        {
            RenderModel(_cachedModel);
        }
    }

    /// <summary>
    /// Public API to update the displayed shape model and re-render vector contours.
    /// </summary>
    public void UpdateModel(ShapeModel? model)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => RenderModel(model));
            return;
        }
        RenderModel(model);
    }

    private void RenderModel(ShapeModel? model)
    {
        try
        {
            _cachedModel = model;
            ContourHostCanvas.Children.Clear();

            if (model == null) return;

            double targetW = ContourHostCanvas.ActualWidth > 0 ? ContourHostCanvas.ActualWidth : (Width > 0 ? Width : 160.0);
            double targetH = ContourHostCanvas.ActualHeight > 0 ? ContourHostCanvas.ActualHeight : (Height > 0 ? Height : 140.0);

            if (targetW <= 10.0 || targetH <= 10.0) return;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            bool hasContours = false;

            // 1. Try continuous vector contours (ModelContours)
            var contoursSnap = model.ModelContours?.ToArray();
            if (contoursSnap != null && contoursSnap.Length > 0)
            {
                foreach (var contour in contoursSnap)
                {
                    if (contour == null) continue;
                    foreach (var pt in contour)
                    {
                        if (pt.X < minX) minX = pt.X;
                        if (pt.X > maxX) maxX = pt.X;
                        if (pt.Y < minY) minY = pt.Y;
                        if (pt.Y > maxY) maxY = pt.Y;
                        hasContours = true;
                    }
                }
            }

            // 2. Fall back to Level 0 Features if ModelContours are empty
            bool useFeatures = false;
            ShapeFeature[]? featsSnap = null;
            if (!hasContours && model.Levels != null && model.Levels.Count > 0 && model.Levels[0].Features.Count > 0)
            {
                featsSnap = model.Levels[0].Features.ToArray();
                foreach (var f in featsSnap)
                {
                    if (f.X < minX) minX = f.X;
                    if (f.X > maxX) maxX = f.X;
                    if (f.Y < minY) minY = f.Y;
                    if (f.Y > maxY) maxY = f.Y;
                }
                hasContours = featsSnap.Length > 0;
                useFeatures = true;
            }

            if (!hasContours || minX > maxX || minY > maxY)
            {
                return;
            }

            float w = maxX - minX;
            float h = maxY - minY;
            if (w <= 1e-4f) w = 1f;
            if (h <= 1e-4f) h = 1f;

            // Viewport padding (12px on each side)
            double padX = 24.0;
            double padY = 24.0;
            double viewW = Math.Max(20.0, targetW - padX);
            double viewH = Math.Max(20.0, targetH - padY);

            // Aspect-ratio-preserving zoom-fit scale
            float scale = (float)Math.Min(viewW / w, viewH / h);
            float offsetX = (float)((targetW - w * scale) / 2.0 - minX * scale);
            float offsetY = (float)((targetH - h * scale) / 2.0 - minY * scale);

            var pathGeom = new PathGeometry();

            if (!useFeatures && contoursSnap != null)
            {
                foreach (var contour in contoursSnap)
                {
                    if (contour == null || contour.Count == 0) continue;
                    var fig = new PathFigure
                    {
                        StartPoint = new Point(contour[0].X * scale + offsetX, contour[0].Y * scale + offsetY),
                        IsClosed = false
                    };
                    for (int i = 1; i < contour.Count; i++)
                    {
                        fig.Segments.Add(new LineSegment(
                            new Point(contour[i].X * scale + offsetX, contour[i].Y * scale + offsetY),
                            isStroked: true));
                    }
                    pathGeom.Figures.Add(fig);
                }
            }
            else if (featsSnap != null && featsSnap.Length > 0)
            {
                var fig = new PathFigure
                {
                    StartPoint = new Point(featsSnap[0].X * scale + offsetX, featsSnap[0].Y * scale + offsetY),
                    IsClosed = false
                };
                for (int i = 1; i < featsSnap.Length; i++)
                {
                    fig.Segments.Add(new LineSegment(
                        new Point(featsSnap[i].X * scale + offsetX, featsSnap[i].Y * scale + offsetY),
                        isStroked: true));
                }
                pathGeom.Figures.Add(fig);
            }

            pathGeom.Freeze();

            var path = new Path
            {
                Data = pathGeom,
                Stroke = Brushes.LimeGreen,
                StrokeThickness = 1.2
            };
            ContourHostCanvas.Children.Add(path);

            // Draw model origin / center crosshair (subtle red marker)
            float originX = model.OriginX;
            float originY = model.OriginY;
            if (originX >= minX - 10 && originX <= maxX + 10 && originY >= minY - 10 && originY <= maxY + 10)
            {
                double scrOx = originX * scale + offsetX;
                double scrOy = originY * scale + offsetY;
                var crossGeom = new GeometryGroup();
                crossGeom.Children.Add(new LineGeometry(new Point(scrOx - 4, scrOy), new Point(scrOx + 4, scrOy)));
                crossGeom.Children.Add(new LineGeometry(new Point(scrOx, scrOy - 4), new Point(scrOx, scrOy + 4)));
                crossGeom.Freeze();

                var crossPath = new Path
                {
                    Data = crossGeom,
                    Stroke = Brushes.Red,
                    StrokeThickness = 1.0
                };
                ContourHostCanvas.Children.Add(crossPath);
            }
        }
        catch { }
    }
}
