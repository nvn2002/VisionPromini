using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using OpenCvSharp;
using TunVision.Core.Abstractions;
using TunVision.Core.Barcode;
using TunVision.Core.Blob;
using TunVision.Core.Caliper;
using TunVision.Core.Common;
using TunVision.Core.Graph;
using TunVision.Core.Models;
using TunVision.Core.ShapeMatching;
using TunVision.UI.Common;

namespace TunVision.UI.ViewModels;

public enum CanvasInteractionMode
{
    PanOrGizmo,
    DrawTemplateRoi,
    BrushErase,
    BoxErase
}

public class MainViewModel : ObservableObject
{
    private readonly GraphExecutor _executor = new();
    public PipelineGraph ActiveGraph { get; private set; }
    public event EventHandler? PipelineRunCompleted;

    private void AttachGraph(PipelineGraph graph)
    {
        graph.CustomAsyncExecutor = async (g, ctx) =>
        {
            await RunPipelineAsync();
            return _lastExecResult ?? new GraphExecutionResult();
        };
        graph.AutoExecute = true;
        foreach (var n in graph.Nodes)
        {
            n.Graph = graph;
        }
    }

    // Graph Collections for Canvas
    public ObservableCollection<NodeViewModel> GraphNodes { get; } = new();
    public ObservableCollection<ConnectionViewModel> GraphConnections { get; } = new();
    public ObservableCollection<PaletteItemViewModel> PaletteModules { get; } = new();
    public ObservableCollection<PaletteGroupViewModel> PaletteGroups { get; } = new();
    public ObservableCollection<ToleranceRuleItem> ToleranceRules { get; } = new();

    private string _paletteSearchText = "";
    public string PaletteSearchText
    {
        get => _paletteSearchText;
        set
        {
            if (SetProperty(ref _paletteSearchText, value))
            {
                RebuildFilteredPaletteGroups();
            }
        }
    }

    private string _graphCursorText = "X: 0 - Y: 0";
    public string GraphCursorText
    {
        get => _graphCursorText;
        set => SetProperty(ref _graphCursorText, value);
    }

    private string _visionZoomText = "Zoom 100%";
    public string VisionZoomText
    {
        get => _visionZoomText;
        set => SetProperty(ref _visionZoomText, value);
    }

    public event EventHandler? NodeSettingsRequested;
    public void RequestOpenNodeSettings() => NodeSettingsRequested?.Invoke(this, EventArgs.Empty);

    private NodeViewModel? _selectedNode;
    public NodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            var oldNode = _selectedNode?.Node;
            if (SetProperty(ref _selectedNode, value))
            {
                if (oldNode is ImageFilterNode oldFilter)
                {
                    oldFilter.PropertyChanged -= OnSelectedFilterNodePropertyChanged;
                }
                if (_selectedNode?.Node is ImageFilterNode newFilter)
                {
                    newFilter.PropertyChanged += OnSelectedFilterNodePropertyChanged;
                }
                if (oldNode is CodeReaderNodeBase oldCode)
                {
                    oldCode.PropertyChanged -= OnSelectedCodeReaderNodePropertyChanged;
                }
                if (_selectedNode?.Node is CodeReaderNodeBase newCode)
                {
                    newCode.PropertyChanged += OnSelectedCodeReaderNodePropertyChanged;
                }
                if (oldNode is TrainShapeModelNode oldTrain)
                {
                    oldTrain.PropertyChanged -= OnSelectedShapeParamNodePropertyChanged;
                }
                if (oldNode is ShapeMatchNode oldMatch)
                {
                    oldMatch.PropertyChanged -= OnSelectedShapeParamNodePropertyChanged;
                }
                if (_selectedNode?.Node is TrainShapeModelNode newTrain)
                {
                    newTrain.PropertyChanged += OnSelectedShapeParamNodePropertyChanged;
                }
                if (_selectedNode?.Node is ShapeMatchNode newMatch)
                {
                    newMatch.PropertyChanged += OnSelectedShapeParamNodePropertyChanged;
                }

                _autoApplyParamsCts?.Cancel();
                ClearLiveContrastPreview();

                // Switch back to PanOrGizmo if moving away from shape node while in erase mode
                if (!IsShapeOrTrainSelected && InteractionMode != CanvasInteractionMode.PanOrGizmo)
                {
                    InteractionMode = CanvasInteractionMode.PanOrGizmo;
                }

                NotifySelectionPropertiesChanged();
                SyncToleranceRulesFromNode();
                UpdateDisplayedPreviewMat();
            }
        }
    }

    private void OnSelectedFilterNodePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GraphNode.Status) or nameof(GraphNode.LastResult) or nameof(GraphNode.ExecutionTimeMs))
            return;
        if (IsLiveRunning)
        {
            _ = RunPipelineAsync();
        }
    }

    private void OnSelectedCodeReaderNodePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GraphNode.Status) or nameof(GraphNode.LastResult) or nameof(GraphNode.ExecutionTimeMs))
            return;
        if (IsLiveRunning)
        {
            _ = RunPipelineAsync();
        }
    }

    private CancellationTokenSource? _livePreviewCts;
    private CancellationTokenSource? _autoApplyParamsCts;
    private readonly SemaphoreSlim _previewLock = new(1, 1);
    private bool _suppressAutoApplyParamEvents = false;
    private IReadOnlyList<Point2f>? _previewTemporaryContours;
    public IReadOnlyList<Point2f>? PreviewTemporaryContours
    {
        get => _previewTemporaryContours;
        set => SetProperty(ref _previewTemporaryContours, value);
    }

    private void ClearLiveContrastPreview()
    {
        _livePreviewCts?.Cancel();
        PreviewTemporaryContours = null;
    }

    private TrainShapeModelNode? FindUpstreamTrainShapeNode(ShapeMatchNode sm)
    {
        var incoming = ActiveGraph.Connections
            .Where(c => c.ToNodeId == sm.Id && (c.ToPortId == "model" || c.ToPortId == "template"))
            .ToList();
        foreach (var conn in incoming)
        {
            var parent = ActiveGraph.Nodes.FirstOrDefault(n => n.Id == conn.FromNodeId);
            if (parent is TrainShapeModelNode tn)
                return tn;
        }
        return null;
    }

    private (Mat? ImageCopy, BaseRoi? Roi) ResolveTemplateImageAndRoi(TrainShapeModelNode tn)
    {
        BaseRoi? roi = tn.HasCustomRoi
            ? new RectRoi((int)Math.Round(tn.RoiX), (int)Math.Round(tn.RoiY), (int)Math.Round(tn.RoiWidth), (int)Math.Round(tn.RoiHeight))
            : null;

        if (roi == null)
        {
            var roiConn = ActiveGraph.Connections.FirstOrDefault(c => c.ToNodeId == tn.Id && c.ToPortId == "roi");
            if (roiConn != null && ActiveGraph.Nodes.FirstOrDefault(n => n.Id == roiConn.FromNodeId) is { } roiNode)
            {
                roi = roiNode switch
                {
                    RoiRectangleNode rr => Math.Abs(rr.AngleDeg) > 0.01f
                        ? new RotatedRectRoi(new Point2f(rr.X + rr.Width * 0.5f, rr.Y + rr.Height * 0.5f), new Size2f(rr.Width, rr.Height), rr.AngleDeg)
                        : new RectRoi((int)Math.Round(rr.X), (int)Math.Round(rr.Y), (int)Math.Round(rr.Width), (int)Math.Round(rr.Height)),
                    RoiCircleNode rc => new CircleRoi(new Point2f(rc.CenterX, rc.CenterY), rc.Radius),
                    RoiPolygonNode rp => new PolygonRoi(rp.Points.ToList()),
                    _ => null
                };
            }
        }

        Mat? src = null;
        if (tn.RawTemplateMat is { } rawTpl && !rawTpl.Empty())
        {
            src = rawTpl.Clone();
        }
        else if (!string.IsNullOrEmpty(tn.TemplateImagePath) && File.Exists(tn.TemplateImagePath))
        {
            src = Cv2.ImRead(tn.TemplateImagePath, ImreadModes.Color);
        }
        else
        {
            var imgConn = ActiveGraph.Connections.FirstOrDefault(c => c.ToNodeId == tn.Id && c.ToPortId == "image");
            if (imgConn != null && ActiveGraph.Nodes.FirstOrDefault(n => n.Id == imgConn.FromNodeId) is { } imgNode)
            {
                if (imgNode.LastOutputMat is { } lm && !lm.Empty())
                    src = lm.Clone();
                else if (imgNode is SourceImageNode sNode && !string.IsNullOrEmpty(sNode.ImagePath) && File.Exists(sNode.ImagePath))
                    src = Cv2.ImRead(sNode.ImagePath, ImreadModes.Color);
            }
        }

        if (src == null && (ResolveSceneForSelection() ?? _lastRootSceneMat ?? CurrentPreviewMat) is { } fallback && !fallback.Empty())
        {
            src = fallback.Clone();
        }

        return (src, roi);
    }

    private void AutoBalanceNodesOnNewInputImage()
    {
        foreach (var tn in ActiveGraph.Nodes.OfType<TrainShapeModelNode>())
        {
            tn.ResetAutoBalanceState(clearManualEdit: true);
        }
        foreach (var sm in ActiveGraph.Nodes.OfType<ShapeMatchNode>())
        {
            sm.ResetAutoBalanceState(clearManualEdit: true);
        }
    }

    private async Task ApplyG3ParametersAndRunAsync(bool retrainModel, bool forceRun = false)
    {
        try
        {
            if (retrainModel)
            {
                if (CurrentTrainShapeNode is { } tn)
                {
                    tn.RequestRetrain();
                }
                else if ((CurrentShapeMatchNode ?? ActiveGraph.Nodes.OfType<ShapeMatchNode>().FirstOrDefault()) is { } sm)
                {
                    var upstreamTrain = FindUpstreamTrainShapeNode(sm);
                    if (upstreamTrain != null)
                    {
                        _suppressAutoApplyParamEvents = true;
                        try
                        {
                            upstreamTrain.ContrastLow = sm.ContrastLow;
                            upstreamTrain.ContrastHigh = sm.ContrastHigh;
                            upstreamTrain.MinContrast = sm.MinContrast;
                            upstreamTrain.MinComponentSize = sm.MinComponentSize;
                            upstreamTrain.PyramidLevels = sm.PyramidLevels;
                            upstreamTrain.LowestPyramid = sm.LowestPyramid;
                            upstreamTrain.ContourMode = sm.ContourMode;
                        }
                        finally
                        {
                            _suppressAutoApplyParamEvents = false;
                        }
                        upstreamTrain.RequestRetrain();
                    }
                    else if (sm.HasTrainRoi)
                    {
                        sm.RequestRetrain();
                    }
                    else if (sm.Model != null)
                    {
                        sm.Model.ContrastLow = sm.ContrastLow;
                        sm.Model.ContrastHigh = sm.ContrastHigh;
                        sm.Model.MinContrast = sm.MinContrast;
                        sm.Model.MinComponentSize = sm.MinComponentSize;
                        sm.MarkModelSynced();
                    }
                }
            }

            if (forceRun || IsLiveRunning)
            {
                await RunPipelineAsync();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Parameter update error: {ex.Message}";
        }
    }

    private void OnSelectedShapeParamNodePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_suppressAutoApplyParamEvents || _isExecutingPipeline != 0)
            return;

        bool isTrainParam = e.PropertyName is
            nameof(TrainShapeModelNode.ContrastLow) or
            nameof(TrainShapeModelNode.ContrastHigh) or
            nameof(TrainShapeModelNode.MinContrast) or
            nameof(TrainShapeModelNode.MinComponentSize) or
            nameof(TrainShapeModelNode.PyramidLevels) or
            nameof(TrainShapeModelNode.ContourMode);

        bool isSearchParam = e.PropertyName is
            nameof(TrainShapeModelNode.LowestPyramid) or
            nameof(ShapeMatchNode.UseTiledSearch) or
            nameof(ShapeMatchNode.MinScore) or
            nameof(ShapeMatchNode.Greediness) or
            nameof(ShapeMatchNode.AngleMinDeg) or
            nameof(ShapeMatchNode.AngleMaxDeg) or
            nameof(ShapeMatchNode.ScaleMin) or
            nameof(ShapeMatchNode.ScaleMax) or
            nameof(ShapeMatchNode.MaxInstances) or
            nameof(ShapeMatchNode.MaxOverlap);

        if (!isTrainParam && !isSearchParam)
            return;

        if (e.PropertyName is nameof(TrainShapeModelNode.ContrastLow)
            or nameof(TrainShapeModelNode.ContrastHigh)
            or nameof(TrainShapeModelNode.MinComponentSize))
        {
            if (sender is TrainShapeModelNode tn)
                TriggerLiveContrastPreview(tn.ContrastLow, tn.ContrastHigh, tn.MinComponentSize);
            else if (sender is ShapeMatchNode sm)
                TriggerLiveContrastPreview(sm.ContrastLow, sm.ContrastHigh, sm.MinComponentSize);
        }

        _autoApplyParamsCts?.Cancel();
        var cts = new CancellationTokenSource();
        _autoApplyParamsCts = cts;
        var token = cts.Token;
        bool shouldRetrain = isTrainParam;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(shouldRetrain ? 180 : 140, token);
                if (token.IsCancellationRequested) return;
                var disp = Application.Current?.Dispatcher;
                if (disp != null)
                {
                    await disp.InvokeAsync(() =>
                    {
                        if (!token.IsCancellationRequested)
                            _ = ApplyG3ParametersAndRunAsync(retrainModel: shouldRetrain);
                    });
                }
                else if (!token.IsCancellationRequested)
                {
                    await ApplyG3ParametersAndRunAsync(retrainModel: shouldRetrain);
                }
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private (Mat? ImageCopy, BaseRoi? Roi) ResolveActiveShapeImageAndRoi()
    {
        Mat? src = null;
        BaseRoi? roi = null;

        if (CurrentTrainShapeNode is { } tn)
        {
            if (tn.HasCustomRoi)
                roi = new RectRoi((int)Math.Round(tn.RoiX), (int)Math.Round(tn.RoiY), (int)Math.Round(tn.RoiWidth), (int)Math.Round(tn.RoiHeight));

            if (tn.RawTemplateMat is { } rawTpl && !rawTpl.Empty())
            {
                src = rawTpl.Clone();
            }
            else if (!string.IsNullOrEmpty(tn.TemplateImagePath) && File.Exists(tn.TemplateImagePath))
            {
                src = Cv2.ImRead(tn.TemplateImagePath, ImreadModes.Color);
            }
            else if ((ResolveSceneForSelection() ?? _lastRootSceneMat ?? CurrentPreviewMat) is { } fallback && !fallback.Empty())
            {
                src = fallback.Clone();
            }
        }
        else
        {
            var sm = CurrentShapeMatchNode ?? ActiveGraph.Nodes.OfType<ShapeMatchNode>().FirstOrDefault();
            if (sm != null)
            {
                if (sm.HasTrainRoi)
                    roi = new RectRoi((int)Math.Round(sm.TrainRoiX), (int)Math.Round(sm.TrainRoiY), (int)Math.Round(sm.TrainRoiWidth), (int)Math.Round(sm.TrainRoiHeight));
                else if (sm.ToolInstance.SearchRoi != null)
                    roi = sm.ToolInstance.SearchRoi;

                if ((sm.RawSceneMat ?? ResolveSceneForSelection() ?? _lastRootSceneMat ?? CurrentPreviewMat) is { } scene && !scene.Empty())
                {
                    src = scene.Clone();
                }
            }
        }

        return (src, roi);
    }

    public void TriggerLiveContrastPreview(int contrastLow, int contrastHigh, int minComponentSize)
    {
        try
        {
            var (ownedMat, roi) = ResolveActiveShapeImageAndRoi();
            if (ownedMat == null || ownedMat.Empty())
            {
                ownedMat?.Dispose();
                return;
            }

            _livePreviewCts?.Cancel();
            var cts = new CancellationTokenSource();
            _livePreviewCts = cts;
            var token = cts.Token;
            OpenCvSharp.Rect? roiBox = roi?.GetBoundingBox();

            _ = Task.Run(async () =>
            {
                bool acquired = false;
                try
                {
                    await _previewLock.WaitAsync(token);
                    acquired = true;

                    using (ownedMat)
                    {
                        if (token.IsCancellationRequested || ownedMat.IsDisposed || ownedMat.Empty()) return;
                        using var gray = ownedMat.Channels() == 1
                            ? ownedMat.Clone()
                            : ownedMat.CvtColor(ColorConversionCodes.BGR2GRAY);

                        Mat extractTarget = gray;
                        Mat? subMat = null;
                        Mat? resizedMat = null;
                        int offsetX = 0, offsetY = 0;
                        float invScale = 1.0f;

                        if (roiBox is { Width: > 4, Height: > 4 } rb)
                        {
                            int rx = Math.Clamp(rb.X, 0, Math.Max(0, gray.Width - 1));
                            int ry = Math.Clamp(rb.Y, 0, Math.Max(0, gray.Height - 1));
                            int rw = Math.Clamp(rb.Width, 1, gray.Width - rx);
                            int rh = Math.Clamp(rb.Height, 1, gray.Height - ry);
                            if (rw >= 5 && rh >= 5)
                            {
                                subMat = new Mat(gray, new OpenCvSharp.Rect(rx, ry, rw, rh));
                                extractTarget = subMat;
                                offsetX = rx;
                                offsetY = ry;
                            }
                        }

                        try
                        {
                            if (extractTarget.Width > 1280 || extractTarget.Height > 1280)
                            {
                                double s = Math.Min(1280.0 / extractTarget.Width, 1280.0 / extractTarget.Height);
                                int nw = Math.Max(16, (int)Math.Round(extractTarget.Width * s));
                                int nh = Math.Max(16, (int)Math.Round(extractTarget.Height * s));
                                resizedMat = new Mat();
                                Cv2.Resize(extractTarget, resizedMat, new OpenCvSharp.Size(nw, nh), 0, 0, InterpolationFlags.Area);
                                invScale = (float)(extractTarget.Width / (double)nw);
                                extractTarget = resizedMat;
                            }

                            int low = Math.Clamp(contrastLow, 1, 250);
                            int high = Math.Clamp(Math.Max(low + 1, contrastHigh), 2, 255);
                            int minComp = Math.Max(1, minComponentSize);
                            var chains = TunVision.Core.ShapeMatching.Geometry.TopologicalContourExtractor.ExtractContoursFast(
                                extractTarget, low, high, minComp);

                            if (token.IsCancellationRequested) return;

                            var flatPts = new List<Point2f>(chains.Sum(c => c.Count));
                            foreach (var chain in chains)
                            {
                                foreach (var pt in chain)
                                {
                                    flatPts.Add(new Point2f(pt.X * invScale + offsetX, pt.Y * invScale + offsetY));
                                }
                            }

                            if (token.IsCancellationRequested) return;

                            var disp = Application.Current?.Dispatcher;
                            if (disp != null)
                            {
                                _ = disp.BeginInvoke(() =>
                                {
                                    if (!token.IsCancellationRequested)
                                        PreviewTemporaryContours = flatPts;
                                });
                            }
                            else
                            {
                                PreviewTemporaryContours = flatPts;
                            }
                        }
                        finally
                        {
                            resizedMat?.Dispose();
                            subMat?.Dispose();
                        }
                    }
                }
                catch (OperationCanceledException) { ownedMat.Dispose(); }
                catch { ownedMat.Dispose(); }
                finally
                {
                    if (acquired) _previewLock.Release();
                }
            }, token);
        }
        catch { }
    }

    private void NotifySelectionPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsSourceImageSelected));
        OnPropertyChanged(nameof(IsImageFilterSelected));
        OnPropertyChanged(nameof(IsShapeMatchSelected));
        OnPropertyChanged(nameof(IsTrainShapeSelected));
        OnPropertyChanged(nameof(IsShapeOrTrainSelected));
        OnPropertyChanged(nameof(IsRoiRectSelected));
        OnPropertyChanged(nameof(IsRoiCircleSelected));
        OnPropertyChanged(nameof(IsRoiPolygonSelected));
        OnPropertyChanged(nameof(IsFixtureSelected));
        OnPropertyChanged(nameof(IsCropSelected));
        OnPropertyChanged(nameof(IsCaliperCircleSelected));
        OnPropertyChanged(nameof(IsSearchLineSelected));
        OnPropertyChanged(nameof(IsSearchCornerSelected));
        OnPropertyChanged(nameof(IsPointToPointSelected));
        OnPropertyChanged(nameof(IsScanQrSelected));
        OnPropertyChanged(nameof(IsScanBarcodeSelected));
        OnPropertyChanged(nameof(IsCodeReaderSelected));
        OnPropertyChanged(nameof(IsBlobSelected));
        OnPropertyChanged(nameof(IsToleranceVerdictSelected));
        OnPropertyChanged(nameof(IsCalibrationSelected));
        OnPropertyChanged(nameof(IsCsvExportSelected));
        OnPropertyChanged(nameof(IsFileRouteSelected));
        OnPropertyChanged(nameof(CurrentSourceNode));
        OnPropertyChanged(nameof(CurrentImageFilterNode));
        OnPropertyChanged(nameof(CurrentShapeMatchNode));
        OnPropertyChanged(nameof(CurrentTrainShapeNode));
        OnPropertyChanged(nameof(CurrentRoiRectNode));
        OnPropertyChanged(nameof(CurrentRoiCircleNode));
        OnPropertyChanged(nameof(CurrentRoiPolygonNode));
        OnPropertyChanged(nameof(CurrentFixtureNode));
        OnPropertyChanged(nameof(CurrentCropNode));
        OnPropertyChanged(nameof(CurrentCaliperCircleNode));
        OnPropertyChanged(nameof(CurrentSearchLineNode));
        OnPropertyChanged(nameof(CurrentSearchCornerNode));
        OnPropertyChanged(nameof(CurrentPointToPointNode));
        OnPropertyChanged(nameof(CurrentScanQrNode));
        OnPropertyChanged(nameof(CurrentScanBarcodeNode));
        OnPropertyChanged(nameof(CurrentCodeReaderNode));
        OnPropertyChanged(nameof(CurrentBlobNode));
        OnPropertyChanged(nameof(CurrentToleranceVerdictNode));
        OnPropertyChanged(nameof(CurrentCalibrationNode));
        OnPropertyChanged(nameof(CurrentCsvExportNode));
        OnPropertyChanged(nameof(CurrentFileRouteNode));
        OnPropertyChanged(nameof(SelectedNodeMeasurements));
        OnPropertyChanged(nameof(SelectedNodeTitle));
        OnPropertyChanged(nameof(SelectedNodeGuide));
        OnPropertyChanged(nameof(HasSelectedNode));
    }

    public bool HasSelectedNode => SelectedNode != null;
    public string SelectedNodeTitle => SelectedNode?.Title ?? (Loc.IsVietnamese ? "Chưa chọn node" : "No node selected");
    public NodeGuideInfo SelectedNodeGuide => NodeGuideProvider.GetGuide(SelectedNode?.Node, Loc.IsVietnamese);

    // Typed selected node helpers
    public SourceImageNode? CurrentSourceNode => SelectedNode?.Node as SourceImageNode;
    public ImageFilterNode? CurrentImageFilterNode => SelectedNode?.Node as ImageFilterNode;
    public ShapeMatchNode? CurrentShapeMatchNode => SelectedNode?.Node as ShapeMatchNode;
    public TrainShapeModelNode? CurrentTrainShapeNode => SelectedNode?.Node as TrainShapeModelNode;
    public RoiRectangleNode? CurrentRoiRectNode => SelectedNode?.Node as RoiRectangleNode;
    public RoiCircleNode? CurrentRoiCircleNode => SelectedNode?.Node as RoiCircleNode;
    public RoiPolygonNode? CurrentRoiPolygonNode => SelectedNode?.Node as RoiPolygonNode;
    public FixtureNode? CurrentFixtureNode => SelectedNode?.Node as FixtureNode;
    public CropNode? CurrentCropNode => SelectedNode?.Node as CropNode;
    public CaliperCircleNode? CurrentCaliperCircleNode => SelectedNode?.Node as CaliperCircleNode;
    public SearchLineNode? CurrentSearchLineNode => SelectedNode?.Node as SearchLineNode;
    public SearchCornerNode? CurrentSearchCornerNode => SelectedNode?.Node as SearchCornerNode;
    public PointToPointNode? CurrentPointToPointNode => SelectedNode?.Node as PointToPointNode;
    public ScanQrNode? CurrentScanQrNode => SelectedNode?.Node as ScanQrNode;
    public ScanBarcodeNode? CurrentScanBarcodeNode => SelectedNode?.Node as ScanBarcodeNode;
    public CodeReaderNodeBase? CurrentCodeReaderNode => SelectedNode?.Node as CodeReaderNodeBase;
    public BlobDetectNode? CurrentBlobNode => SelectedNode?.Node as BlobDetectNode;
    public ToleranceVerdictNode? CurrentToleranceVerdictNode => SelectedNode?.Node as ToleranceVerdictNode;
    public CalibrationNode? CurrentCalibrationNode => SelectedNode?.Node as CalibrationNode;
    public CsvExportNode? CurrentCsvExportNode => SelectedNode?.Node as CsvExportNode;
    public FileRouteNode? CurrentFileRouteNode => SelectedNode?.Node as FileRouteNode;

    public bool IsSourceImageSelected => CurrentSourceNode != null;
    public bool IsImageFilterSelected => CurrentImageFilterNode != null;
    public bool IsShapeMatchSelected => CurrentShapeMatchNode != null;
    public bool IsTrainShapeSelected => CurrentTrainShapeNode != null;
    public bool IsShapeOrTrainSelected => CurrentShapeMatchNode != null || CurrentTrainShapeNode != null;
    public bool IsRoiRectSelected => CurrentRoiRectNode != null;
    public bool IsRoiCircleSelected => CurrentRoiCircleNode != null;
    public bool IsRoiPolygonSelected => CurrentRoiPolygonNode != null;
    public bool IsFixtureSelected => CurrentFixtureNode != null;
    public bool IsCropSelected => CurrentCropNode != null;
    public bool IsCaliperCircleSelected => CurrentCaliperCircleNode != null;
    public bool IsSearchLineSelected => CurrentSearchLineNode != null;
    public bool IsSearchCornerSelected => CurrentSearchCornerNode != null;
    public bool IsPointToPointSelected => CurrentPointToPointNode != null;
    public bool IsScanQrSelected => CurrentScanQrNode != null;
    public bool IsScanBarcodeSelected => CurrentScanBarcodeNode != null;
    public bool IsCodeReaderSelected => CurrentCodeReaderNode != null;
    public bool IsBlobSelected => CurrentBlobNode != null;
    public bool IsToleranceVerdictSelected => CurrentToleranceVerdictNode != null;
    public bool IsCalibrationSelected => CurrentCalibrationNode != null;
    public bool IsCsvExportSelected => CurrentCsvExportNode != null;
    public bool IsFileRouteSelected => CurrentFileRouteNode != null;

    public ObservableCollection<NodeMeasurementEntry>? SelectedNodeMeasurements => SelectedNode?.Measurements;

    // Interactive Canvas Tool State
    private CanvasInteractionMode _interactionMode = CanvasInteractionMode.PanOrGizmo;
    public CanvasInteractionMode InteractionMode
    {
        get => _interactionMode;
        set
        {
            if (SetProperty(ref _interactionMode, value))
            {
                OnPropertyChanged(nameof(IsPanOrGizmoMode));
                OnPropertyChanged(nameof(IsDrawRoiMode));
                OnPropertyChanged(nameof(IsBrushEraseMode));
                OnPropertyChanged(nameof(IsBoxEraseMode));
            }
        }
    }

    public bool IsPanOrGizmoMode
    {
        get => InteractionMode == CanvasInteractionMode.PanOrGizmo;
        set { if (value) InteractionMode = CanvasInteractionMode.PanOrGizmo; }
    }

    public bool IsDrawRoiMode
    {
        get => InteractionMode == CanvasInteractionMode.DrawTemplateRoi;
        set { if (value) InteractionMode = CanvasInteractionMode.DrawTemplateRoi; }
    }

    public bool IsBrushEraseMode
    {
        get => InteractionMode == CanvasInteractionMode.BrushErase;
        set
        {
            if (value)
            {
                EnsureShapeNodeSelected();
                InteractionMode = CanvasInteractionMode.BrushErase;
            }
        }
    }

    public bool IsBoxEraseMode
    {
        get => InteractionMode == CanvasInteractionMode.BoxErase;
        set
        {
            if (value)
            {
                EnsureShapeNodeSelected();
                InteractionMode = CanvasInteractionMode.BoxErase;
            }
        }
    }

    private bool _showTemplateFeatures = false;
    public bool ShowTemplateFeatures
    {
        get => _showTemplateFeatures;
        set => SetProperty(ref _showTemplateFeatures, value);
    }

    private OverlayDrawMode _overlayDrawMode = OverlayDrawMode.NoScore;
    public OverlayDrawMode OverlayDrawMode
    {
        get => _overlayDrawMode;
        set => SetProperty(ref _overlayDrawMode, value);
    }

    public static IReadOnlyList<OverlayDrawMode> AvailableOverlayDrawModes { get; } =
        Enum.GetValues<OverlayDrawMode>();

    private double _eraserBrushRadius = 18.0;
    public double EraserBrushRadius
    {
        get => _eraserBrushRadius;
        set => SetProperty(ref _eraserBrushRadius, Math.Clamp(value, 2.0, 120.0));
    }

    private bool _autoRerunOnMouseUp = true;
    public bool AutoRerunOnMouseUp
    {
        get => _autoRerunOnMouseUp;
        set => SetProperty(ref _autoRerunOnMouseUp, value);
    }

    private bool _showOnlySelectedNodeOverlay = false;
    public bool ShowOnlySelectedNodeOverlay
    {
        get => _showOnlySelectedNodeOverlay;
        set
        {
            if (SetProperty(ref _showOnlySelectedNodeOverlay, value))
            {
                UpdateDisplayedPreviewMat();
            }
        }
    }

    public ImageFileItem? SelectedSourceFileItem
    {
        get
        {
            var src = CurrentSourceNode ?? ActiveGraph?.Nodes.OfType<SourceImageNode>().FirstOrDefault();
            if (src == null || string.IsNullOrEmpty(src.ImagePath)) return null;
            return src.FolderFileItems.FirstOrDefault(i =>
                string.Equals(i.FullPath, src.ImagePath, StringComparison.OrdinalIgnoreCase));
        }
        set
        {
            var src = CurrentSourceNode ?? ActiveGraph?.Nodes.OfType<SourceImageNode>().FirstOrDefault();
            if (value != null && src != null)
            {
                AutoBalanceNodesOnNewInputImage();
                src.SelectImageByPath(value.FullPath);
                OnPropertyChanged(nameof(CurrentSourceNode));
                OnPropertyChanged(nameof(SelectedSourceFileItem));
                SelectedNode?.UpdatePortPositions();
                if (ActiveGraph != null && !ActiveGraph.AutoExecute)
                {
                    _ = RunPipelineAsync();
                }
            }
        }
    }

    // Result & Verdict Banner (ForgeStation & Smart Vision Style)
    private VerdictResult? _overallVerdict;
    public VerdictResult? OverallVerdict
    {
        get => _overallVerdict;
        set
        {
            if (SetProperty(ref _overallVerdict, value))
            {
                OnPropertyChanged(nameof(VerdictBannerText));
                OnPropertyChanged(nameof(VerdictBannerBackground));
                OnPropertyChanged(nameof(VerdictShortText));
                OnPropertyChanged(nameof(VerdictBadgeBackground));
                OnPropertyChanged(nameof(VerdictBadgeForeground));
                OnPropertyChanged(nameof(IsVerdictVisible));
            }
        }
    }

    private static readonly Brush _badgeBgPass = CreateFrozenBrush(Color.FromArgb(220, 20, 50, 32));
    private static readonly Brush _badgeBgDefect = CreateFrozenBrush(Color.FromArgb(220, 75, 20, 20));
    private static readonly Brush _badgeFgPass = CreateFrozenBrush(Color.FromRgb(46, 204, 113));
    private static readonly Brush _badgeFgDefect = CreateFrozenBrush(Color.FromRgb(255, 85, 85));
    private static readonly Brush _bannerBgPass = CreateFrozenBrush(Color.FromRgb(16, 124, 65));
    private static readonly Brush _bannerBgDefect = CreateFrozenBrush(Color.FromRgb(216, 59, 1));
    private static readonly Brush _bannerBgNeutral = CreateFrozenBrush(Color.FromRgb(50, 50, 55));

    private static SolidColorBrush CreateFrozenBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public bool IsVerdictVisible => OverallVerdict != null;
    public string VerdictShortText => OverallVerdict?.State == VerdictState.Defect ? "NG" : "OK";
    public Brush VerdictBadgeBackground => OverallVerdict?.State == VerdictState.Defect ? _badgeBgDefect : _badgeBgPass;
    public Brush VerdictBadgeForeground => OverallVerdict?.State == VerdictState.Defect ? _badgeFgDefect : _badgeFgPass;

    public string VerdictBannerText => OverallVerdict?.State switch
    {
        VerdictState.Pass => $"✔ PASS - {OverallVerdict.Message}",
        VerdictState.Defect => $"✖ DEFECT - {OverallVerdict.Message}",
        _ => "NO VERDICT"
    };

    public Brush VerdictBannerBackground => OverallVerdict?.State switch
    {
        VerdictState.Pass => _bannerBgPass,
        VerdictState.Defect => _bannerBgDefect,
        _ => _bannerBgNeutral
    };

    // Live & Continuous Loop Execution
    private bool _isLiveRunning = true;
    public bool IsLiveRunning
    {
        get => _isLiveRunning;
        set => SetProperty(ref _isLiveRunning, value);
    }

    private bool _isContinuousRunning = false;
    public bool IsContinuousRunning
    {
        get => _isContinuousRunning;
        set
        {
            if (SetProperty(ref _isContinuousRunning, value))
            {
                OnPropertyChanged(nameof(IsNotContinuousRunning));
            }
        }
    }
    public bool IsNotContinuousRunning => !_isContinuousRunning;

    private int _continuousDelayMs = 250;
    public int ContinuousDelayMs
    {
        get => _continuousDelayMs;
        set => SetProperty(ref _continuousDelayMs, Math.Clamp(value, 20, 10000));
    }

    private CancellationTokenSource? _continuousCts;

    private double _totalExecutionTimeMs;
    public double TotalExecutionTimeMs
    {
        get => _totalExecutionTimeMs;
        set => SetProperty(ref _totalExecutionTimeMs, value);
    }

    private string _statusMessage = "TunVision Studio Ready";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    // Image & Preview
    private Mat? _lastCompositeMat;
    private Mat? _lastRootSceneMat;
    private GraphExecutionResult? _lastExecResult;
    public Mat? LastRootSceneMat => _lastRootSceneMat;

    /// <summary>Composite (scene + overlays) belonging to the image source of the currently selected node.</summary>
    private Mat? ResolveCompositeForSelection()
    {
        var res = _lastExecResult;
        if (res != null && SelectedNode != null &&
            res.NodeSourceIds.TryGetValue(SelectedNode.Id, out var srcId) &&
            res.SourceComposites.TryGetValue(srcId, out var comp) && !comp.Empty())
        {
            return comp;
        }
        return _lastCompositeMat;
    }

    /// <summary>Raw scene image feeding the currently selected node (falls back to first source).</summary>
    private Mat? ResolveSceneForSelection()
    {
        var res = _lastExecResult;
        if (res != null && SelectedNode != null &&
            res.NodeSourceIds.TryGetValue(SelectedNode.Id, out var srcId) &&
            res.SourceScenes.TryGetValue(srcId, out var scene) && !scene.Empty())
        {
            return scene;
        }
        return _lastRootSceneMat;
    }

    private Mat? _currentPreviewMat;
    public Mat? CurrentPreviewMat
    {
        get => _currentPreviewMat;
        private set
        {
            _currentPreviewMat = value;
            if (_currentPreviewMat != null && !_currentPreviewMat.Empty())
            {
                CurrentPreviewSource = BitmapHelper.MatToBitmapSource(_currentPreviewMat);
                PreviewWidth = _currentPreviewMat.Width;
                PreviewHeight = _currentPreviewMat.Height;
            }
            else
            {
                CurrentPreviewSource = null;
                PreviewWidth = 0;
                PreviewHeight = 0;
            }
        }
    }

    private Mat? ResolveTargetPreviewMat(Mat? compositeFallback)
    {
        if (SelectedNode?.Node is TrainShapeModelNode trainNode &&
            (trainNode.RawTemplateMat ?? trainNode.LastOutputMat) is { } trainMat && !trainMat.Empty())
        {
            return trainMat;
        }
        if (ShowOnlySelectedNodeOverlay)
        {
            if (SelectedNode?.Node is ShapeMatchNode smNode &&
                (smNode.RawSceneMat ?? ResolveSceneForSelection() ?? smNode.LastOutputMat) is { } smMat && !smMat.Empty())
            {
                return smMat;
            }
            if (SelectedNode?.Node.LastOutputMat is { } nodeMat && !nodeMat.Empty())
            {
                return nodeMat;
            }
        }
        if (SelectedNode?.Node is ImageFilterNode filterNode && filterNode.LastOutputMat is { } filterMat && !filterMat.Empty())
        {
            return filterMat;
        }
        if (SelectedNode?.Node is SourceImageNode srcNode &&
            ActiveGraph.Nodes.OfType<ImageFilterNode>().Any() &&
            srcNode.LastOutputMat is { } rawSrcMat && !rawSrcMat.Empty())
        {
            return rawSrcMat;
        }
        return compositeFallback;
    }

    public TrainShapeModelNode? GetActiveTrainShapeNodeForCanvas() =>
        SelectedNode?.Node as TrainShapeModelNode;

    public IReadOnlyList<ShapeMatchNode> GetActiveShapeMatchNodesForCanvas()
    {
        if (SelectedNode?.Node is TrainShapeModelNode or ImageFilterNode)
            return Array.Empty<ShapeMatchNode>();

        if (SelectedNode?.Node is SourceImageNode && ActiveGraph.Nodes.OfType<ImageFilterNode>().Any())
            return Array.Empty<ShapeMatchNode>();

        if (ShowOnlySelectedNodeOverlay)
        {
            return SelectedNode?.Node is ShapeMatchNode smSel
                ? new[] { smSel }
                : Array.Empty<ShapeMatchNode>();
        }

        var allMatchNodes = ActiveGraph.Nodes.OfType<ShapeMatchNode>().ToList();
        if (allMatchNodes.Count == 0)
            return allMatchNodes;

        var res = _lastExecResult;
        if (res != null)
        {
            string? activeSrcId = null;
            if (SelectedNode != null && res.NodeSourceIds.TryGetValue(SelectedNode.Id, out var sid))
            {
                activeSrcId = sid;
            }
            else if (res.SourceComposites.Count > 0)
            {
                activeSrcId = ActiveGraph.Nodes.OfType<SourceImageNode>()
                    .FirstOrDefault(s => res.SourceComposites.ContainsKey(s.Id))?.Id;
            }

            if (activeSrcId != null)
            {
                return allMatchNodes
                    .Where(m => !res.NodeSourceIds.TryGetValue(m.Id, out var mSrcId) || mSrcId == activeSrcId)
                    .ToList();
            }
        }

        return allMatchNodes;
    }

    public void UpdateDisplayedPreviewMat()
    {
        Mat? fallback = ResolveCompositeForSelection();
        if (fallback == null || fallback.Empty())
        {
            fallback = SelectedNode?.Node.LastOutputMat;
        }
        Mat? target = ResolveTargetPreviewMat(fallback);
        if (target != null && !target.Empty())
        {
            CurrentPreviewMat = target;
        }
    }

    private BitmapSource? _currentPreviewSource;
    public BitmapSource? CurrentPreviewSource
    {
        get => _currentPreviewSource;
        set => SetProperty(ref _currentPreviewSource, value);
    }

    private int _previewWidth;
    public int PreviewWidth
    {
        get => _previewWidth;
        set => SetProperty(ref _previewWidth, value);
    }

    private int _previewHeight;
    public int PreviewHeight
    {
        get => _previewHeight;
        set => SetProperty(ref _previewHeight, value);
    }

    // Localization Singleton
    public LocalizationManager Loc => LocalizationManager.Instance;
    public event EventHandler? GraphResetRequested;

    // Commands
    public ICommand RunPipelineCommand { get; }
    public ICommand ToggleLiveCommand { get; }
    public ICommand StartContinuousRunCommand { get; }
    public ICommand StopContinuousRunCommand { get; }
    public ICommand SaveProjectCommand { get; }
    public ICommand OpenProjectCommand { get; }
    public ICommand NewPipelineCommand { get; }
    public ICommand SelectPipelinePresetCommand { get; }
    public ICommand ToggleLanguageCommand { get; }
    public ICommand AddNodeCommand { get; }
    public ICommand DeleteSelectedNodeCommand { get; }
    public ICommand LoadSamplePipelineCommand { get; }
    public ICommand OpenImageCommand { get; }
    public ICommand BrowseModelFileCommand { get; }
    public ICommand SaveModelFileCommand { get; }
    public ICommand TrainModelNowCommand { get; }
    public ICommand BrowseSourceImageCommand { get; }
    public ICommand BrowseSourceFolderCommand { get; }
    public ICommand NextSourceImageCommand { get; }
    public ICommand PrevSourceImageCommand { get; }
    public ICommand SelectSourceImageCommand { get; }
    public ICommand BrowseTrainTemplateImageCommand { get; }
    public ICommand BrowseTrainExportModelCommand { get; }

    // Context Action Bar & Eraser / Origin / Tolerance Commands
    public ICommand SetPanModeCommand { get; }
    public ICommand SetDrawRoiModeCommand { get; }
    public ICommand SetBrushEraseModeCommand { get; }
    public ICommand SetBoxEraseModeCommand { get; }
    public ICommand ToggleShowTemplateFeaturesCommand { get; }
    public ICommand UndoEraseCommand { get; }
    public ICommand ResetModelCommand { get; }
    public ICommand SetOriginFromMatchCommand { get; }
    public ICommand CaptureNominalsCommand { get; }
    public ICommand ClearToleranceRulesCommand { get; }
    public ICommand OpenNodeSettingsCommand { get; }
    public ICommand ResetImageFilterCommand { get; }
    public ICommand AutoBalanceContrastLowCommand { get; }
    public ICommand AutoBalanceContrastHighCommand { get; }
    public ICommand AutoBalanceNumLevelsCommand { get; }
    public ICommand AddPolygonVertexCommand { get; }
    public ICommand RemovePolygonVertexCommand { get; }
    public ICommand ResetPolygonQuadCommand { get; }

    public ContourExtractionMode[] AvailableContourModes => Enum.GetValues<ContourExtractionMode>();
    public CircleScanDirection[] AvailableScanDirections => Enum.GetValues<CircleScanDirection>();
    public EdgePolarity[] AvailablePolarities => Enum.GetValues<EdgePolarity>();
    public EdgeSelection[] AvailableSelections => Enum.GetValues<EdgeSelection>();
    public ThresholdMode[] AvailableThresholdModes => Enum.GetValues<ThresholdMode>();
    public MorphOp[] AvailableMorphOps => Enum.GetValues<MorphOp>();
    public ImageColorMode[] AvailableColorModes => Enum.GetValues<ImageColorMode>();
    public ImageFilterOp[] AvailableFilterOps => Enum.GetValues<ImageFilterOp>();
    public CodeKind[] AvailableCodeKinds => Enum.GetValues<CodeKind>();
    public TextMatchRule[] AvailableMatchRules => Enum.GetValues<TextMatchRule>();

    public MainViewModel()
    {
        InitializePalette();

        LocalizationManager.Instance.LanguageChanged += (_, _) =>
        {
            foreach (var p in PaletteModules) p.RefreshLocalization();
            foreach (var g in PaletteGroups) g.RefreshLocalization();
            foreach (var n in GraphNodes) n.RefreshLocalization();
            OnPropertyChanged(nameof(Loc));
            OnPropertyChanged(nameof(SelectedNodeTitle));
            OnPropertyChanged(nameof(SelectedNodeGuide));
        };

        ActiveGraph = PipelineGraph.CreateCompleteMetrologyPipeline();
        AttachGraph(ActiveGraph);
        SyncFromGraph();

        OpenNodeSettingsCommand = new RelayCommand(_ => RequestOpenNodeSettings());
        ResetImageFilterCommand = new RelayCommand(_ =>
        {
            if (CurrentImageFilterNode != null)
            {
                CurrentImageFilterNode.ResetDefaults();
                OnPropertyChanged(nameof(CurrentImageFilterNode));
                _ = RunPipelineAsync();
            }
        });

        RunPipelineCommand = new RelayCommand(async _ =>
        {
            if (Keyboard.FocusedElement is System.Windows.Controls.TextBox focusedTb)
            {
                focusedTb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            }
            await RunPipelineAsync();
        });
        ToggleLiveCommand = new RelayCommand(_ => IsLiveRunning = !IsLiveRunning);
        StartContinuousRunCommand = new RelayCommand(_ => StartContinuousLoop());
        StopContinuousRunCommand = new RelayCommand(_ => StopContinuousLoop());
        SaveProjectCommand = new RelayCommand(_ => SaveProject());
        OpenProjectCommand = new RelayCommand(_ => OpenProject());

        NewPipelineCommand = new RelayCommand(param =>
        {
            if (param is string preset && !string.IsNullOrWhiteSpace(preset))
                ResetPipeline(preset);
            else
                ResetPipeline("metrology");
        });
        SelectPipelinePresetCommand = new RelayCommand(param =>
        {
            if (param is string preset)
                ResetPipeline(preset);
        });
        ToggleLanguageCommand = new RelayCommand(param =>
        {
            if (param is string lang && !string.IsNullOrWhiteSpace(lang))
                LocalizationManager.Instance.SetLanguage(lang);
            else
                LocalizationManager.Instance.ToggleLanguage();
        });
        AddNodeCommand = new RelayCommand(param =>
        {
            if (param is PaletteItemViewModel item)
                AddNodeFromPalette(item);
        });
        DeleteSelectedNodeCommand = new RelayCommand(_ => DeleteSelectedNode());
        LoadSamplePipelineCommand = new RelayCommand(async param =>
        {
            if (param is string sample)
                await SwitchSampleAsync(sample);
        });
        OpenImageCommand = new RelayCommand(_ => OpenCustomImage());

        SetPanModeCommand = new RelayCommand(_ => InteractionMode = CanvasInteractionMode.PanOrGizmo);
        SetDrawRoiModeCommand = new RelayCommand(_ =>
        {
            InteractionMode = InteractionMode == CanvasInteractionMode.DrawTemplateRoi
                ? CanvasInteractionMode.PanOrGizmo
                : CanvasInteractionMode.DrawTemplateRoi;
        });
        SetBrushEraseModeCommand = new RelayCommand(_ =>
        {
            EnsureShapeNodeSelected();
            InteractionMode = InteractionMode == CanvasInteractionMode.BrushErase
                ? CanvasInteractionMode.PanOrGizmo
                : CanvasInteractionMode.BrushErase;
        });
        SetBoxEraseModeCommand = new RelayCommand(_ =>
        {
            EnsureShapeNodeSelected();
            InteractionMode = InteractionMode == CanvasInteractionMode.BoxErase
                ? CanvasInteractionMode.PanOrGizmo
                : CanvasInteractionMode.BoxErase;
        });
        ToggleShowTemplateFeaturesCommand = new RelayCommand(_ =>
        {
            EnsureShapeNodeSelected();
            ShowTemplateFeatures = !ShowTemplateFeatures;
        });
        UndoEraseCommand = new RelayCommand(_ => UndoModelErase());
        ResetModelCommand = new RelayCommand(_ => ResetModelErase());
        SetOriginFromMatchCommand = new RelayCommand(_ =>
        {
            var shapeNode = ResolveTargetShapeMatchNode();
            if (shapeNode != null)
            {
                shapeNode.SetOriginFromLastMatch();
                StatusMessage = Loc.IsVietnamese
                    ? $"Đã đặt gốc tọa độ theo mẫu khớp: ({shapeNode.ReferenceOriginX:F1}, {shapeNode.ReferenceOriginY:F1})"
                    : $"Set reference origin to last match: ({shapeNode.ReferenceOriginX:F1}, {shapeNode.ReferenceOriginY:F1})";
                _ = RunPipelineAsync();
            }
        });

        CaptureNominalsCommand = new RelayCommand(_ =>
        {
            var tolNode = CurrentToleranceVerdictNode ?? ActiveGraph.Nodes.OfType<ToleranceVerdictNode>().FirstOrDefault();
            if (tolNode != null)
            {
                tolNode.CaptureNominalsFromGraph(ActiveGraph, 5.0);
                SyncToleranceRulesFromNode();
                _ = RunPipelineAsync();
            }
        });

        ClearToleranceRulesCommand = new RelayCommand(_ =>
        {
            var tolNode = CurrentToleranceVerdictNode ?? ActiveGraph.Nodes.OfType<ToleranceVerdictNode>().FirstOrDefault();
            if (tolNode != null)
            {
                tolNode.Rules.Clear();
                SyncToleranceRulesFromNode();
                _ = RunPipelineAsync();
            }
        });

        BrowseModelFileCommand = new RelayCommand(_ =>
        {
            var shapeNode = ResolveTargetShapeMatchNode();
            var dlg = new OpenFileDialog
            {
                Filter = "Shape Models (*.shm;*.json)|*.shm;*.json|All Files (*.*)|*.*",
                InitialDirectory = GetSafeInitialDirectory(shapeNode?.ModelPath),
                RestoreDirectory = true
            };
            if (dlg.ShowDialog() == true && shapeNode != null)
            {
                try
                {
                    shapeNode.ModelPath = dlg.FileName;
                    shapeNode.Model = dlg.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                        ? ShapeModel.LoadJson(dlg.FileName)
                        : ShapeModel.LoadShm(dlg.FileName);
                    if (shapeNode.Model != null)
                    {
                        _suppressAutoApplyParamEvents = true;
                        try
                        {
                            shapeNode.SyncFromModel(shapeNode.Model);
                        }
                        finally
                        {
                            _suppressAutoApplyParamEvents = false;
                        }
                    }
                    OnPropertyChanged(nameof(CurrentShapeMatchNode));
                    _ = RunPipelineAsync();
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Load model error: {ex.Message}";
                }
            }
        });

        SaveModelFileCommand = new RelayCommand(_ =>
        {
            var shapeNode = ResolveTargetShapeMatchNode();
            var model = shapeNode?.Model ?? CurrentTrainShapeNode?.ToolInstance.Model;
            if (model == null)
            {
                StatusMessage = Loc.IsVietnamese
                    ? "Chưa có template nào để lưu. Hãy vẽ ROI và tạo template trước."
                    : "No template model available to save.";
                return;
            }
            var dlg = new SaveFileDialog
            {
                Filter = "Binary Model (*.shm)|*.shm|JSON Model (*.json)|*.json",
                FileName = "my_model.shm"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    if (dlg.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        model.SaveJson(dlg.FileName);
                    else
                        model.SaveShm(dlg.FileName);
                    StatusMessage = $"Saved model to {Path.GetFileName(dlg.FileName)}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Save model error: {ex.Message}";
                }
            }
        });

        TrainModelNowCommand = new RelayCommand(_ =>
        {
            ClearLiveContrastPreview();
            var srcMat = ResolveSceneForSelection() ?? CurrentPreviewMat;
            if (srcMat == null || srcMat.Empty()) return;

            if (CurrentTrainShapeNode != null)
            {
                BaseRoi? roi = (CurrentTrainShapeNode.RoiWidth >= 8 && CurrentTrainShapeNode.RoiHeight >= 8)
                    ? new RectRoi((int)CurrentTrainShapeNode.RoiX, (int)CurrentTrainShapeNode.RoiY, (int)CurrentTrainShapeNode.RoiWidth, (int)CurrentTrainShapeNode.RoiHeight)
                    : null;
                _suppressAutoApplyParamEvents = true;
                try
                {
                    CurrentTrainShapeNode.AutoEstimateParameters(srcMat, roi);
                    CurrentTrainShapeNode.RetrainNow(srcMat, roi);
                }
                finally
                {
                    _suppressAutoApplyParamEvents = false;
                }
                OnPropertyChanged(nameof(CurrentTrainShapeNode));
                StatusMessage = $"Model trained! Features: {CurrentTrainShapeNode.ToolInstance.Model?.Levels.FirstOrDefault()?.Features.Count ?? 0}";
                _ = RunPipelineAsync();
                return;
            }

            var shapeNode = ResolveTargetShapeMatchNode();
            if (shapeNode != null)
            {
                BaseRoi? roi = shapeNode.HasTrainRoi
                    ? new RectRoi((int)shapeNode.TrainRoiX, (int)shapeNode.TrainRoiY, (int)shapeNode.TrainRoiWidth, (int)shapeNode.TrainRoiHeight)
                    : null;
                _suppressAutoApplyParamEvents = true;
                try
                {
                    shapeNode.AutoEstimateParameters(srcMat, roi);
                    shapeNode.ToolInstance.Train(srcMat, roi);
                    shapeNode.MarkModelSynced();
                }
                finally
                {
                    _suppressAutoApplyParamEvents = false;
                }
                shapeNode.SetOriginFromLastMatch();
                OnPropertyChanged(nameof(CurrentShapeMatchNode));
                StatusMessage = $"Model trained! Features: {shapeNode.Model?.Levels.FirstOrDefault()?.Features.Count ?? 0}";
                _ = RunPipelineAsync();
            }
        });

        BrowseSourceImageCommand = new RelayCommand(_ =>
        {
            var srcNode = CurrentSourceNode ?? ActiveGraph.Nodes.OfType<SourceImageNode>().FirstOrDefault();
            if (srcNode == null) return;
            var dlg = new OpenFileDialog
            {
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                InitialDirectory = GetSafeInitialDirectory(srcNode.FolderPath),
                RestoreDirectory = true
            };
            if (dlg.ShowDialog() == true)
            {
                AutoBalanceNodesOnNewInputImage();
                srcNode.SetImageFile(dlg.FileName);
                OnPropertyChanged(nameof(CurrentSourceNode));
                OnPropertyChanged(nameof(SelectedSourceFileItem));
                SelectedNode?.UpdatePortPositions();
                if (!ActiveGraph.AutoExecute) _ = RunPipelineAsync();
            }
        });

        BrowseSourceFolderCommand = new RelayCommand(_ =>
        {
            var srcNode = CurrentSourceNode ?? ActiveGraph.Nodes.OfType<SourceImageNode>().FirstOrDefault();
            if (srcNode == null) return;
            var dlg = new OpenFolderDialog
            {
                InitialDirectory = GetSafeInitialDirectory(srcNode.FolderPath)
            };
            if (dlg.ShowDialog() == true)
            {
                AutoBalanceNodesOnNewInputImage();
                srcNode.SetFolder(dlg.FolderName);
                OnPropertyChanged(nameof(CurrentSourceNode));
                OnPropertyChanged(nameof(SelectedSourceFileItem));
                SelectedNode?.UpdatePortPositions();
                if (!ActiveGraph.AutoExecute) _ = RunPipelineAsync();
            }
        });

        NextSourceImageCommand = new RelayCommand(_ =>
        {
            var srcNode = CurrentSourceNode ?? ActiveGraph.Nodes.OfType<SourceImageNode>().FirstOrDefault();
            if (srcNode == null) return;
            AutoBalanceNodesOnNewInputImage();
            srcNode.NextImage();
            OnPropertyChanged(nameof(CurrentSourceNode));
            OnPropertyChanged(nameof(SelectedSourceFileItem));
            SelectedNode?.UpdatePortPositions();
            if (!ActiveGraph.AutoExecute) _ = RunPipelineAsync();
        });

        PrevSourceImageCommand = new RelayCommand(_ =>
        {
            var srcNode = CurrentSourceNode ?? ActiveGraph.Nodes.OfType<SourceImageNode>().FirstOrDefault();
            if (srcNode == null) return;
            AutoBalanceNodesOnNewInputImage();
            srcNode.PrevImage();
            OnPropertyChanged(nameof(CurrentSourceNode));
            OnPropertyChanged(nameof(SelectedSourceFileItem));
            SelectedNode?.UpdatePortPositions();
            if (!ActiveGraph.AutoExecute) _ = RunPipelineAsync();
        });

        SelectSourceImageCommand = new RelayCommand(param =>
        {
            var srcNode = CurrentSourceNode ?? ActiveGraph.Nodes.OfType<SourceImageNode>().FirstOrDefault();
            if (srcNode != null && param is string path)
            {
                AutoBalanceNodesOnNewInputImage();
                srcNode.SelectImageByPath(path);
                OnPropertyChanged(nameof(CurrentSourceNode));
                OnPropertyChanged(nameof(SelectedSourceFileItem));
                SelectedNode?.UpdatePortPositions();
                if (!ActiveGraph.AutoExecute) _ = RunPipelineAsync();
            }
        });

        BrowseTrainTemplateImageCommand = new RelayCommand(_ =>
        {
            if (CurrentTrainShapeNode == null) return;
            var dlg = new OpenFileDialog
            {
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                InitialDirectory = GetSafeInitialDirectory(CurrentTrainShapeNode.TemplateImagePath),
                RestoreDirectory = true
            };
            if (dlg.ShowDialog() == true)
            {
                CurrentTrainShapeNode.TemplateImagePath = dlg.FileName;
                CurrentTrainShapeNode.ToolInstance.Model = null;
                if (File.Exists(dlg.FileName))
                {
                    using var tplMat = Cv2.ImRead(dlg.FileName, ImreadModes.Color);
                    if (!tplMat.Empty())
                    {
                        BaseRoi? roi = (CurrentTrainShapeNode.RoiWidth >= 8 && CurrentTrainShapeNode.RoiHeight >= 8)
                            ? new RectRoi((int)CurrentTrainShapeNode.RoiX, (int)CurrentTrainShapeNode.RoiY, (int)CurrentTrainShapeNode.RoiWidth, (int)CurrentTrainShapeNode.RoiHeight)
                            : null;
                        _suppressAutoApplyParamEvents = true;
                        try
                        {
                            CurrentTrainShapeNode.AutoEstimateParameters(tplMat, roi);
                        }
                        finally
                        {
                            _suppressAutoApplyParamEvents = false;
                        }
                    }
                }
                OnPropertyChanged(nameof(CurrentTrainShapeNode));
                _ = RunPipelineAsync();
            }
        });

        BrowseTrainExportModelCommand = new RelayCommand(_ =>
        {
            if (CurrentTrainShapeNode == null) return;
            var dlg = new SaveFileDialog
            {
                Filter = "Shape Model (*.shm)|*.shm|All Files (*.*)|*.*",
                FileName = "new_trained_model.shm"
            };
            if (dlg.ShowDialog() == true)
            {
                CurrentTrainShapeNode.ExportPath = dlg.FileName;
                OnPropertyChanged(nameof(CurrentTrainShapeNode));
                _ = RunPipelineAsync();
            }
        });

        AutoBalanceContrastLowCommand = new RelayCommand(_ =>
        {
            var (ownedMat, roi) = ResolveActiveShapeImageAndRoi();
            if (ownedMat == null || ownedMat.Empty()) { ownedMat?.Dispose(); return; }
            _autoApplyParamsCts?.Cancel();
            using (ownedMat)
            using (var mask = roi?.CreateMask(ownedMat.Width, ownedMat.Height))
            {
                var est = ShapeTrainer.AutoEstimateContrast(ownedMat, mask);
                if (CurrentTrainShapeNode is { } tn)
                {
                    _suppressAutoApplyParamEvents = true;
                    try
                    {
                        tn.MinContrast = est.MinContrast;
                        tn.ContrastLow = est.ContrastLow;
                    }
                    finally
                    {
                        _suppressAutoApplyParamEvents = false;
                    }
                    OnPropertyChanged(nameof(CurrentTrainShapeNode));
                    TriggerLiveContrastPreview(tn.ContrastLow, tn.ContrastHigh, tn.MinComponentSize);
                    StatusMessage = $"Auto ContrastLow (P70): {tn.ContrastLow}, MinContrast: {tn.MinContrast}";
                    _ = ApplyG3ParametersAndRunAsync(retrainModel: true, forceRun: true);
                }
                else if (ResolveTargetShapeMatchNode() is { } sm)
                {
                    _suppressAutoApplyParamEvents = true;
                    try
                    {
                        sm.MinContrast = est.MinContrast;
                        sm.ContrastLow = est.ContrastLow;
                    }
                    finally
                    {
                        _suppressAutoApplyParamEvents = false;
                    }
                    OnPropertyChanged(nameof(CurrentShapeMatchNode));
                    TriggerLiveContrastPreview(sm.ContrastLow, sm.ContrastHigh, sm.MinComponentSize);
                    StatusMessage = $"Auto ContrastLow (P70): {sm.ContrastLow}, MinContrast: {sm.MinContrast}";
                    _ = ApplyG3ParametersAndRunAsync(retrainModel: true, forceRun: true);
                }
            }
        });

        AutoBalanceContrastHighCommand = new RelayCommand(_ =>
        {
            var (ownedMat, roi) = ResolveActiveShapeImageAndRoi();
            if (ownedMat == null || ownedMat.Empty()) { ownedMat?.Dispose(); return; }
            _autoApplyParamsCts?.Cancel();
            using (ownedMat)
            using (var mask = roi?.CreateMask(ownedMat.Width, ownedMat.Height))
            {
                var est = ShapeTrainer.AutoEstimateContrast(ownedMat, mask);
                if (CurrentTrainShapeNode is { } tn)
                {
                    _suppressAutoApplyParamEvents = true;
                    try
                    {
                        tn.ContrastHigh = Math.Max(tn.ContrastLow + 10, est.ContrastHigh);
                    }
                    finally
                    {
                        _suppressAutoApplyParamEvents = false;
                    }
                    OnPropertyChanged(nameof(CurrentTrainShapeNode));
                    TriggerLiveContrastPreview(tn.ContrastLow, tn.ContrastHigh, tn.MinComponentSize);
                    StatusMessage = $"Auto ContrastHigh (P85): {tn.ContrastHigh}";
                    _ = ApplyG3ParametersAndRunAsync(retrainModel: true, forceRun: true);
                }
                else if (ResolveTargetShapeMatchNode() is { } sm)
                {
                    _suppressAutoApplyParamEvents = true;
                    try
                    {
                        sm.ContrastHigh = Math.Max(sm.ContrastLow + 10, est.ContrastHigh);
                    }
                    finally
                    {
                        _suppressAutoApplyParamEvents = false;
                    }
                    OnPropertyChanged(nameof(CurrentShapeMatchNode));
                    TriggerLiveContrastPreview(sm.ContrastLow, sm.ContrastHigh, sm.MinComponentSize);
                    StatusMessage = $"Auto ContrastHigh (P85): {sm.ContrastHigh}";
                    _ = ApplyG3ParametersAndRunAsync(retrainModel: true, forceRun: true);
                }
            }
        });

        AutoBalanceNumLevelsCommand = new RelayCommand(_ =>
        {
            var (ownedMat, roi) = ResolveActiveShapeImageAndRoi();
            if (ownedMat == null || ownedMat.Empty()) { ownedMat?.Dispose(); return; }
            _autoApplyParamsCts?.Cancel();
            using (ownedMat)
            {
                var bbox = roi?.GetBoundingBox();
                int w = bbox is { Width: > 0 } b ? b.Width : ownedMat.Width;
                int h = bbox is { Height: > 0 } b2 ? b2.Height : ownedMat.Height;
                int minDim = Math.Max(16, Math.Min(w, h));
                int levels = (int)Math.Clamp(Math.Round(Math.Log2(minDim / 16.0)) + 1, 1, 6);

                if (CurrentTrainShapeNode is { } tn)
                {
                    _suppressAutoApplyParamEvents = true;
                    try
                    {
                        tn.PyramidLevels = levels;
                    }
                    finally
                    {
                        _suppressAutoApplyParamEvents = false;
                    }
                    OnPropertyChanged(nameof(CurrentTrainShapeNode));
                    StatusMessage = $"Auto NumLevels (Log2): {levels}";
                    _ = ApplyG3ParametersAndRunAsync(retrainModel: true, forceRun: true);
                }
                else if (ResolveTargetShapeMatchNode() is { } sm)
                {
                    _suppressAutoApplyParamEvents = true;
                    try
                    {
                        sm.PyramidLevels = levels;
                    }
                    finally
                    {
                        _suppressAutoApplyParamEvents = false;
                    }
                    OnPropertyChanged(nameof(CurrentShapeMatchNode));
                    StatusMessage = $"Auto NumLevels (Log2): {levels}";
                    _ = ApplyG3ParametersAndRunAsync(retrainModel: true, forceRun: true);
                }
            }
        });

        AddPolygonVertexCommand = new RelayCommand(_ =>
        {
            if (CurrentRoiPolygonNode is { } pn)
            {
                pn.AddVertexAtLongestEdge();
                OnPropertyChanged(nameof(CurrentRoiPolygonNode));
                if (IsLiveRunning) _ = RunPipelineAsync();
            }
        });

        RemovePolygonVertexCommand = new RelayCommand(_ =>
        {
            if (CurrentRoiPolygonNode is { } pn)
            {
                pn.RemoveLastVertex();
                OnPropertyChanged(nameof(CurrentRoiPolygonNode));
                if (IsLiveRunning) _ = RunPipelineAsync();
            }
        });

        ResetPolygonQuadCommand = new RelayCommand(_ =>
        {
            if (CurrentRoiPolygonNode is { } pn)
            {
                pn.ResetDefaultQuad();
                OnPropertyChanged(nameof(CurrentRoiPolygonNode));
                if (IsLiveRunning) _ = RunPipelineAsync();
            }
        });

        // Initial run
        _ = RunPipelineAsync();
    }

    private void InitializePalette()
    {
        // Group 1: Nguồn ảnh (Image)
        PaletteModules.Add(new PaletteItemViewModel(
            "Source Image", "Load Image", "Image", "Image", "Load image file or folder", typeof(SourceImageNode),
            "M 3,5 L 21,5 L 21,19 L 3,19 Z M 7.5,9.5 A 1.5,1.5 0 1 1 7.5,9.49 M 4,17 L 9,12 L 13,15.5 L 16.5,11.5 L 20,17"));
        PaletteModules.Add(new PaletteItemViewModel(
            "Image Filter", "Image Filter", "Image", "Image", "Convert color, exposure, contrast, gamma & denoise", typeof(ImageFilterNode),
            "M 4,6 L 20,6 M 8,4 L 8,8 M 4,12 L 20,12 M 15,10 L 15,14 M 4,18 L 20,18 M 10,16 L 10,20"));
        PaletteModules.Add(new PaletteItemViewModel(
            "Crop", "Crop ROI", "Image", "Image", "Crop image using wired ROI", typeof(CropNode),
            "M 6,2 L 6,18 L 22,18 M 2,6 L 18,6 L 18,22"));

        // Group 2: Định vị (Localization)
        PaletteModules.Add(new PaletteItemViewModel(
            "Shape Match (MWWZ)", "Contour Matching", "Detection", "Detection", "Contour match, brush erase & Frame output", typeof(ShapeMatchNode),
            "M 4,8 L 4,4 L 8,4 M 16,4 L 20,4 L 20,8 M 20,16 L 20,20 L 16,20 M 8,20 L 4,20 L 4,16 M 12,7 L 12,17 M 7,12 L 17,12 M 9,9 L 15,9 L 15,15 L 9,15 Z"));
        PaletteModules.Add(new PaletteItemViewModel(
            "Train Shape Model", "Train Template", "Detection", "Detection", "Train reusable model from template", typeof(TrainShapeModelNode),
            "M 12,3 L 14.5,8.5 L 20.5,9.5 L 16,14 L 17,20 L 12,17 L 7,20 L 8,14 L 3.5,9.5 L 9.5,8.5 Z"));
        PaletteModules.Add(new PaletteItemViewModel(
            "Fixture", "Fixture Coordinate", "Fixturing", "Detection", "Align coordinates from pose", typeof(FixtureNode),
            "M 5,19 L 5,5 M 5,5 L 2.5,8 M 5,5 L 7.5,8 M 5,19 L 19,19 M 19,19 L 16,16.5 M 19,19 L 16,21.5 M 5,15 L 9,15 L 9,19"));

        // Group 3: Hình học (Geometry)
        PaletteModules.Add(new PaletteItemViewModel(
            "Search Line", "Search Line", "Measurement", "Geometry", "2D Rake caliper + RANSAC line fit", typeof(SearchLineNode),
            "M 4,20 L 20,4 M 4,9 L 9,4 M 15,20 L 20,15 M 8,16 L 16,8"));
        PaletteModules.Add(new PaletteItemViewModel(
            "Search Circle (Caliper)", "Search Circle", "Measurement", "Geometry", "Spoke caliper ring Inward/Outward + RANSAC", typeof(CaliperCircleNode),
            "M 12,5 A 7,7 0 1 1 11.99,5 M 12,2 L 12,6 M 12,18 L 12,22 M 2,12 L 6,12 M 18,12 L 22,12"));
        PaletteModules.Add(new PaletteItemViewModel(
            "Search Corner", "Search Corner", "Measurement", "Geometry", "2-line intersection & included angle", typeof(SearchCornerNode),
            "M 5,4 L 5,19 L 20,19 M 5,12 A 7,7 0 0 1 12,19 M 3,19 L 7,19 M 5,17 L 5,21"));
        PaletteModules.Add(new PaletteItemViewModel(
            "Point-to-Point Distance", "Khoảng cách điểm-điểm", "Measurement", "Geometry", "Measure dist, ΔX, ΔY, angle between 2 points", typeof(PointToPointNode),
            "M 5,18 A 2,2 0 1 1 5,17.99 M 19,6 A 2,2 0 1 1 19,5.99 M 6.5,16.5 L 17.5,7.5 M 4,21 L 20,21"));
        PaletteModules.Add(new PaletteItemViewModel(
            "ROI Rectangle", "Search Rectangle", "ROI", "Geometry", "Rotatable rectangular region", typeof(RoiRectangleNode),
            "M 4,6 L 20,6 L 20,18 L 4,18 Z M 2,4 L 6,4 L 6,8 L 2,8 Z M 18,16 L 22,16 L 22,20 L 18,20 Z"));
        PaletteModules.Add(new PaletteItemViewModel(
            "ROI Circle", "ROI Circle", "ROI", "Geometry", "Circular inspection region", typeof(RoiCircleNode),
            "M 12,4 A 8,8 0 1 1 11.99,4 M 12,9 L 12,15 M 9,12 L 15,12"));
        PaletteModules.Add(new PaletteItemViewModel(
            "ROI Polygon", "ROI Polygon", "ROI", "Geometry", "Freeform polygon inspection region", typeof(RoiPolygonNode),
            "M 5,5 L 18,4 L 20,16 L 11,20 L 4,14 Z"));

        // Group 4: Đọc mã (Code Reading)
        PaletteModules.Add(new PaletteItemViewModel(
            "Scan QR", "Scan QR", "Code", "Code", "Read QR Code, Data Matrix, Aztec, PDF417 using ZXing", typeof(ScanQrNode),
            "M 3,3 L 10,3 L 10,10 L 3,10 Z M 5,5 L 8,5 L 8,8 L 5,8 Z M 14,3 L 21,3 L 21,10 L 14,10 Z M 16,5 L 19,5 L 19,8 L 16,8 Z M 3,14 L 10,14 L 10,21 L 3,21 Z M 5,16 L 8,16 L 8,19 L 5,19 Z M 14,14 L 17,14 L 17,17 L 14,17 Z M 18,14 L 21,14 L 21,21 L 18,21 Z M 14,18 L 17,18 L 17,21 L 14,21 Z"));
        PaletteModules.Add(new PaletteItemViewModel(
            "Scan Barcode", "Scan Barcode", "Code", "Code", "Read 1D Barcode (Code 128, Code 39, EAN, UPC) using ZXing", typeof(ScanBarcodeNode),
            "M 3,4 L 5,4 L 5,20 L 3,20 Z M 7,4 L 8,4 L 8,20 L 7,20 Z M 10,4 L 13,4 L 13,20 L 10,20 Z M 15,4 L 16,4 L 16,20 L 15,20 Z M 18,4 L 21,4 L 21,20 L 18,20 Z"));

        // Group 5: Kiểm tra & Đầu ra (Inspection & Output)
        PaletteModules.Add(new PaletteItemViewModel(
            "Blob Detect", "Blob Detect", "Blob", "Inspection", "Thresholding, morphology & region features", typeof(BlobDetectNode),
            "M 7,6 C 11,3 18,5 19,10 C 20,15 16,20 11,19 C 6,18 3,14 5,9 Z M 10,10 A 1.5,1.5 0 1 1 10,9.99"));
        PaletteModules.Add(new PaletteItemViewModel(
            "Tolerance Verdict", "Tolerance OK/NG", "Verdict", "Inspection", "Multi-row Pass/Fail table & nominal capture", typeof(ToleranceVerdictNode),
            "M 4,12 L 9,17 L 20,6 M 3,4 L 21,4 L 21,20 L 3,20 Z"));
        PaletteModules.Add(new PaletteItemViewModel(
            "Calibration px→mm", "Calibration px→mm", "Measurement", "Inspection", "Convert pixels to millimeters", typeof(CalibrationNode),
            "M 3,8 L 21,8 L 21,16 L 3,16 Z M 7,8 L 7,12 M 11,8 L 11,14 M 15,8 L 15,12 M 19,8 L 19,14"));
        PaletteModules.Add(new PaletteItemViewModel(
            "CSV Export", "CSV Export", "Output", "Inspection", "Log verdicts and measurements to CSV", typeof(CsvExportNode),
            "M 6,3 L 15,3 L 19,7 L 19,21 L 6,21 Z M 9,11 L 16,11 M 9,14 L 16,14 M 9,17 L 13,17"));
        PaletteModules.Add(new PaletteItemViewModel(
            "Route OK/NG File", "Route OK/NG", "Output", "Inspection", "Save/route images to OK or NG folders", typeof(FileRouteNode),
            "M 3,6 L 9,6 L 11,8 L 21,8 L 21,19 L 3,19 Z M 8,13 L 16,13 M 13,10 L 16,13 L 13,16"));

        RebuildFilteredPaletteGroups();
    }

    public void RebuildFilteredPaletteGroups()
    {
        string q = (_paletteSearchText ?? "").Trim();
        var groupDefs = new (string Key, string Vi, string En)[]
        {
            ("Image", "Nguồn ảnh", "Image Source"),
            ("Detection", "Định vị", "Localization"),
            ("Geometry", "Hình học", "Geometry"),
            ("Code", "Đọc mã", "Code Reading"),
            ("Inspection", "Kiểm tra", "Inspection & Output")
        };

        PaletteGroups.Clear();
        foreach (var def in groupDefs)
        {
            var grp = new PaletteGroupViewModel(def.Key, def.Vi, def.En);
            foreach (var item in PaletteModules.Where(m => m.GroupKey == def.Key))
            {
                if (string.IsNullOrEmpty(q) ||
                    item.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    item.ShortTitle.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    item.Description.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    grp.Items.Add(item);
                }
            }
            if (grp.Items.Count > 0)
            {
                PaletteGroups.Add(grp);
            }
        }
    }

    public void SyncToleranceRulesFromNode()
    {
        ToleranceRules.Clear();
        if (CurrentToleranceVerdictNode != null)
        {
            foreach (var r in CurrentToleranceVerdictNode.Rules)
                ToleranceRules.Add(r);
        }
    }

    private void EnsureShapeNodeSelected()
    {
        if (CurrentShapeMatchNode != null || CurrentTrainShapeNode != null) return;
        var shapeVm = GraphNodes.FirstOrDefault(n => n.Node is ShapeMatchNode)
            ?? GraphNodes.FirstOrDefault(n => n.Node is TrainShapeModelNode);
        if (shapeVm != null)
        {
            SelectedNode = shapeVm;
        }
    }

    private ShapeMatchNode? ResolveTargetShapeMatchNode()
    {
        if (CurrentShapeMatchNode != null) return CurrentShapeMatchNode;
        var shapeVm = GraphNodes.FirstOrDefault(n => n.Node is ShapeMatchNode);
        if (shapeVm != null)
        {
            SelectedNode = shapeVm;
            return shapeVm.Node as ShapeMatchNode;
        }
        return null;
    }

    private static string GetSafeInitialDirectory(string? pathOrDir)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(pathOrDir))
            {
                if (Directory.Exists(pathOrDir))
                    return Path.GetFullPath(pathOrDir);

                string? dir = Path.GetDirectoryName(pathOrDir);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    return Path.GetFullPath(dir);
            }
        }
        catch { }

        return AppDomain.CurrentDomain.BaseDirectory;
    }

    public ShapeModel? GetActiveEditableModel()
    {
        if (CurrentShapeMatchNode?.Model != null) return CurrentShapeMatchNode.Model;
        if (CurrentTrainShapeNode?.ToolInstance.Model != null) return CurrentTrainShapeNode.ToolInstance.Model;
        var firstShape = ActiveGraph.Nodes.OfType<ShapeMatchNode>().FirstOrDefault(n => n.Model != null);
        if (firstShape != null) return firstShape.Model;
        var firstTrain = ActiveGraph.Nodes.OfType<TrainShapeModelNode>().FirstOrDefault(n => n.ToolInstance.Model != null);
        return firstTrain?.ToolInstance.Model;
    }

    public void ApplyTemplateRoiOnCanvas(Rect2f roiRect)
    {
        var srcMat = ResolveSceneForSelection() ?? CurrentPreviewMat;
        if (srcMat == null || srcMat.Empty()) return;

        int rx = Math.Clamp((int)Math.Round(roiRect.X), 0, Math.Max(0, srcMat.Width - 4));
        int ry = Math.Clamp((int)Math.Round(roiRect.Y), 0, Math.Max(0, srcMat.Height - 4));
        int rw = Math.Clamp((int)Math.Round(roiRect.Width), 4, Math.Max(4, srcMat.Width - rx));
        int rh = Math.Clamp((int)Math.Round(roiRect.Height), 4, Math.Max(4, srcMat.Height - ry));
        var roi = new RectRoi(rx, ry, rw, rh);

        if (CurrentCaliperCircleNode != null)
        {
            var cRef = CurrentCaliperCircleNode.LastActiveFixture.InverseTransform(new Point2f(rx + rw * 0.5f, ry + rh * 0.5f));
            CurrentCaliperCircleNode.CenterX = cRef.X;
            CurrentCaliperCircleNode.CenterY = cRef.Y;
            CurrentCaliperCircleNode.ExpectedRadius = Math.Max(8f, Math.Min(rw, rh) * 0.42f);
            OnPropertyChanged(nameof(CurrentCaliperCircleNode));
        }
        else if (CurrentSearchLineNode != null)
        {
            var p1Ref = CurrentSearchLineNode.LastActiveFixture.InverseTransform(new Point2f(rx, ry + rh * 0.5f));
            var p2Ref = CurrentSearchLineNode.LastActiveFixture.InverseTransform(new Point2f(rx + rw, ry + rh * 0.5f));
            CurrentSearchLineNode.StartX = p1Ref.X;
            CurrentSearchLineNode.StartY = p1Ref.Y;
            CurrentSearchLineNode.EndX = p2Ref.X;
            CurrentSearchLineNode.EndY = p2Ref.Y;
            CurrentSearchLineNode.SearchHalfWidth = Math.Max(10f, rh * 0.45f);
            OnPropertyChanged(nameof(CurrentSearchLineNode));
        }
        else if (CurrentSearchCornerNode != null)
        {
            var l1S = CurrentSearchCornerNode.LastActiveFixture.InverseTransform(new Point2f(rx, ry + rh));
            var mid = CurrentSearchCornerNode.LastActiveFixture.InverseTransform(new Point2f(rx + rw * 0.5f, ry));
            var l2E = CurrentSearchCornerNode.LastActiveFixture.InverseTransform(new Point2f(rx + rw, ry + rh));
            CurrentSearchCornerNode.Line1StartX = l1S.X;
            CurrentSearchCornerNode.Line1StartY = l1S.Y;
            CurrentSearchCornerNode.Line1EndX = mid.X;
            CurrentSearchCornerNode.Line1EndY = mid.Y;
            CurrentSearchCornerNode.Line2StartX = mid.X;
            CurrentSearchCornerNode.Line2StartY = mid.Y;
            CurrentSearchCornerNode.Line2EndX = l2E.X;
            CurrentSearchCornerNode.Line2EndY = l2E.Y;
            OnPropertyChanged(nameof(CurrentSearchCornerNode));
        }
        else if (CurrentRoiRectNode != null)
        {
            CurrentRoiRectNode.X = rx;
            CurrentRoiRectNode.Y = ry;
            CurrentRoiRectNode.Width = rw;
            CurrentRoiRectNode.Height = rh;
            OnPropertyChanged(nameof(CurrentRoiRectNode));
        }
        else if (CurrentRoiCircleNode != null)
        {
            CurrentRoiCircleNode.CenterX = rx + rw * 0.5f;
            CurrentRoiCircleNode.CenterY = ry + rh * 0.5f;
            CurrentRoiCircleNode.Radius = Math.Max(8f, Math.Min(rw, rh) * 0.45f);
            OnPropertyChanged(nameof(CurrentRoiCircleNode));
        }
        else if (CurrentRoiPolygonNode != null)
        {
            CurrentRoiPolygonNode.ResetDefaultQuad(rx, ry, rw, rh);
            OnPropertyChanged(nameof(CurrentRoiPolygonNode));
        }
        else if (CurrentTrainShapeNode != null)
        {
            CurrentTrainShapeNode.RoiX = rx;
            CurrentTrainShapeNode.RoiY = ry;
            CurrentTrainShapeNode.RoiWidth = rw;
            CurrentTrainShapeNode.RoiHeight = rh;
            _suppressAutoApplyParamEvents = true;
            try
            {
                CurrentTrainShapeNode.AutoEstimateParameters(srcMat, roi);
                CurrentTrainShapeNode.RetrainNow(srcMat, roi);
            }
            finally
            {
                _suppressAutoApplyParamEvents = false;
            }
            OnPropertyChanged(nameof(CurrentTrainShapeNode));
        }
        else
        {
            var shapeNode = ResolveTargetShapeMatchNode();
            if (shapeNode != null)
            {
                shapeNode.TrainRoiX = rx;
                shapeNode.TrainRoiY = ry;
                shapeNode.TrainRoiWidth = rw;
                shapeNode.TrainRoiHeight = rh;
                _suppressAutoApplyParamEvents = true;
                try
                {
                    shapeNode.AutoEstimateParameters(srcMat, roi);
                    shapeNode.ToolInstance.Train(srcMat, roi);
                    shapeNode.MarkModelSynced();
                }
                finally
                {
                    _suppressAutoApplyParamEvents = false;
                }
                shapeNode.ReferenceOriginX = shapeNode.Model?.OriginX ?? (rx + rw * 0.5f);
                shapeNode.ReferenceOriginY = shapeNode.Model?.OriginY ?? (ry + rh * 0.5f);
                OnPropertyChanged(nameof(CurrentShapeMatchNode));
            }
        }

        InteractionMode = CanvasInteractionMode.PanOrGizmo;
        _ = RunPipelineAsync();
    }

    public void UndoModelErase()
    {
        EnsureShapeNodeSelected();
        var model = GetActiveEditableModel();
        if (model != null && model.UndoFeatureRemoval())
        {
            OnPropertyChanged(nameof(CurrentShapeMatchNode));
            OnPropertyChanged(nameof(CurrentTrainShapeNode));
            _ = RunPipelineAsync();
        }
    }

    public void ResetModelErase()
    {
        EnsureShapeNodeSelected();
        var model = GetActiveEditableModel();
        if (model != null && model.ResetAllRemovedFeatures())
        {
            OnPropertyChanged(nameof(CurrentShapeMatchNode));
            OnPropertyChanged(nameof(CurrentTrainShapeNode));
            _ = RunPipelineAsync();
        }
    }

    private async void StartContinuousLoop()
    {
        if (IsContinuousRunning) return;
        IsContinuousRunning = true;
        _continuousCts = new CancellationTokenSource();
        var token = _continuousCts.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var srcNode = CurrentSourceNode ?? ActiveGraph.Nodes.OfType<SourceImageNode>().FirstOrDefault();
                srcNode?.NextImage();
                OnPropertyChanged(nameof(CurrentSourceNode));
                SelectedNode?.RefreshLocalization();

                if (!ActiveGraph.AutoExecute)
                {
                    await RunPipelineAsync();
                }
                int delay = Math.Clamp(ContinuousDelayMs, 20, 10000);
                await Task.Delay(delay, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusMessage = $"Continuous loop stopped: {ex.Message}";
        }
        finally
        {
            IsContinuousRunning = false;
        }
    }

    public void StopContinuousLoop()
    {
        _continuousCts?.Cancel();
        IsContinuousRunning = false;
    }

    private void SaveProject()
    {
        string rawName = string.IsNullOrWhiteSpace(ActiveGraph.Name) ? "pipeline" : ActiveGraph.Name;
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            rawName = rawName.Replace(c, '_');
        }
        var dlg = new SaveFileDialog
        {
            Filter = "TunVision Project (*.tvproj;*.json)|*.tvproj;*.json|All Files (*.*)|*.*",
            FileName = $"{rawName.Replace(' ', '_')}.tvproj"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                ActiveGraph.SaveToFile(dlg.FileName);
                StatusMessage = $"Saved pipeline project to {Path.GetFileName(dlg.FileName)}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Save failed: {ex.Message}";
            }
        }
    }

    private void OpenProject()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "TunVision Project (*.tvproj;*.json)|*.tvproj;*.json|All Files (*.*)|*.*",
            RestoreDirectory = true
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                StopContinuousLoop();
                ActiveGraph = PipelineGraph.LoadFromFile(dlg.FileName);
                AttachGraph(ActiveGraph);
                SelectedNode = null;
                SyncFromGraph();
                GraphResetRequested?.Invoke(this, EventArgs.Empty);
                _ = RunPipelineAsync();
                StatusMessage = $"Loaded pipeline project {Path.GetFileName(dlg.FileName)}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Open failed: {ex.Message}";
            }
        }
    }

    public void SyncFromGraph()
    {
        GraphNodes.Clear();
        GraphConnections.Clear();

        foreach (var node in ActiveGraph.Nodes)
        {
            GraphNodes.Add(new NodeViewModel(node));
        }

        foreach (var conn in ActiveGraph.Connections)
        {
            var fromNodeVm = GraphNodes.FirstOrDefault(n => n.Id == conn.FromNodeId);
            var toNodeVm = GraphNodes.FirstOrDefault(n => n.Id == conn.ToNodeId);
            if (fromNodeVm != null && toNodeVm != null)
            {
                var fromPortVm = fromNodeVm.OutputPorts.FirstOrDefault(p => p.Id == conn.FromPortId);
                var toPortVm = toNodeVm.InputPorts.FirstOrDefault(p => p.Id == conn.ToPortId);
                if (fromPortVm != null && toPortVm != null)
                {
                    GraphConnections.Add(new ConnectionViewModel(conn, fromPortVm, toPortVm));
                }
            }
        }
    }

    private void ResetTargetNodePortState(GraphNode targetNode, string toPortId)
    {
        if (targetNode is TrainShapeModelNode tn)
        {
            if (toPortId is "image" or "roi")
            {
                tn.ToolInstance.Model = null;
                tn.ResetAutoBalanceState(clearManualEdit: true);
            }
        }
        else if (targetNode is ShapeMatchNode sm)
        {
            if (toPortId is "model" or "template")
            {
                if (string.IsNullOrEmpty(sm.ModelPath))
                {
                    sm.Model = null;
                }
                sm.ResetAutoBalanceState(clearManualEdit: true);
            }
            else if (toPortId == "roi")
            {
                sm.ToolInstance.SearchRoi = null;
            }
            else if (toPortId == "image")
            {
                sm.ResetAutoBalanceState(clearManualEdit: true);
            }
        }
    }

    public void DeleteConnection(ConnectionViewModel connVm, bool runPipeline = true)
    {
        ActiveGraph.Disconnect(
            connVm.Connection.FromNodeId,
            connVm.Connection.FromPortId,
            connVm.Connection.ToNodeId,
            connVm.Connection.ToPortId);
        GraphConnections.Remove(connVm);
        ResetTargetNodePortState(connVm.ToPort.ParentNode.Node, connVm.ToPort.Id);

        if (runPipeline && IsLiveRunning)
        {
            _ = RunPipelineAsync();
        }
    }

    public void ConnectPorts(PortViewModel fromPort, PortViewModel toPort)
    {
        // If target input port already has an incoming wire, replace it so the user can easily re-route connections
        var existingConn = GraphConnections.FirstOrDefault(c =>
            c.Connection.ToNodeId == toPort.ParentNode.Id &&
            c.Connection.ToPortId == toPort.Id);
        if (existingConn != null)
        {
            ActiveGraph.Disconnect(
                existingConn.Connection.FromNodeId,
                existingConn.Connection.FromPortId,
                existingConn.Connection.ToNodeId,
                existingConn.Connection.ToPortId);
            GraphConnections.Remove(existingConn);
        }

        if (ActiveGraph.Connect(fromPort.ParentNode.Id, fromPort.Id, toPort.ParentNode.Id, toPort.Id))
        {
            var conn = ActiveGraph.Connections.Last();
            GraphConnections.Add(new ConnectionViewModel(conn, fromPort, toPort));
            ResetTargetNodePortState(toPort.ParentNode.Node, toPort.Id);
            if (IsLiveRunning)
            {
                _ = RunPipelineAsync();
            }
        }
    }

    public void AddNodeFromPalette(PaletteItemViewModel item)
    {
        if (Activator.CreateInstance(item.NodeType) is GraphNode node)
        {
            node.PositionX = 280 + (GraphNodes.Count % 4) * 65;
            node.PositionY = 140 + (GraphNodes.Count % 4) * 50;
            ActiveGraph.AddNode(node);
            var vm = new NodeViewModel(node);
            GraphNodes.Add(vm);

            // Auto-wire 'image' input port from active ImageFilterNode (or SourceImageNode) so the newly added tool works immediately
            var imgInPort = vm.InputPorts.FirstOrDefault(p => p.Port.Name == "image");
            if (imgInPort != null)
            {
                NodeViewModel? providerVm = null;
                if (node is not ImageFilterNode)
                {
                    providerVm = GraphNodes.FirstOrDefault(n => n.Node is ImageFilterNode && n.Id != vm.Id);
                }
                providerVm ??= GraphNodes.FirstOrDefault(n => n.Node is SourceImageNode);

                var srcOutPort = providerVm?.OutputPorts.FirstOrDefault(p => p.Port.Name == "image");
                if (srcOutPort != null && ActiveGraph.Connect(providerVm!.Id, srcOutPort.Id, vm.Id, imgInPort.Id))
                {
                    var conn = ActiveGraph.Connections.Last();
                    GraphConnections.Add(new ConnectionViewModel(conn, srcOutPort, imgInPort));

                    // If we just added an ImageFilterNode and connected it to SourceImageNode, redirect downstream image wires through this filter node
                    if (node is ImageFilterNode && providerVm.Node is SourceImageNode)
                    {
                        var filterOutPort = vm.OutputPorts.FirstOrDefault(p => p.Port.Name == "image");
                        if (filterOutPort != null)
                        {
                            var wiresToRedirect = GraphConnections
                                .Where(c => c.Connection.FromNodeId == providerVm.Id &&
                                            c.Connection.FromPortId == srcOutPort.Id &&
                                            c.Connection.ToNodeId != vm.Id &&
                                            c.ToPort.Port.Name == "image")
                                .ToList();

                            foreach (var oldWire in wiresToRedirect)
                            {
                                var targetInPort = oldWire.ToPort;
                                ActiveGraph.Disconnect(
                                    oldWire.Connection.FromNodeId,
                                    oldWire.Connection.FromPortId,
                                    oldWire.Connection.ToNodeId,
                                    oldWire.Connection.ToPortId);
                                GraphConnections.Remove(oldWire);

                                if (ActiveGraph.Connect(vm.Id, filterOutPort.Id, targetInPort.ParentNode.Id, targetInPort.Id))
                                {
                                    var newConn = ActiveGraph.Connections.Last();
                                    GraphConnections.Add(new ConnectionViewModel(newConn, filterOutPort, targetInPort));
                                }
                            }
                        }
                    }
                }
            }

            SelectedNode = vm;
            if (IsLiveRunning)
            {
                _ = RunPipelineAsync();
            }
        }
    }

    public void DeleteSelectedNode()
    {
        var wiresToDelete = GraphConnections.Where(c => c.IsSelected).ToList();
        var nodesToDelete = GraphNodes.Where(n => n.IsSelected).ToList();
        if (nodesToDelete.Count == 0 && SelectedNode != null && wiresToDelete.Count == 0)
        {
            nodesToDelete.Add(SelectedNode);
        }

        if (wiresToDelete.Count > 0)
        {
            foreach (var w in wiresToDelete)
            {
                DeleteConnection(w, runPipeline: false);
            }
            if (nodesToDelete.Count == 0)
            {
                if (IsLiveRunning)
                {
                    _ = RunPipelineAsync();
                }
                return;
            }
        }

        if (nodesToDelete.Count == 0) return;

        foreach (var nodeVm in nodesToDelete)
        {
            string id = nodeVm.Id;

            // If deleting an ImageFilterNode, bridge its upstream image source back to its downstream consumers
            if (nodeVm.Node is ImageFilterNode)
            {
                var incomingImgWire = GraphConnections.FirstOrDefault(c => c.Connection.ToNodeId == id && c.ToPort.Port.Name == "image");
                var outgoingImgWires = GraphConnections.Where(c => c.Connection.FromNodeId == id && c.FromPort.Port.Name == "image").ToList();

                if (incomingImgWire != null && outgoingImgWires.Count > 0)
                {
                    var upstreamOutPort = incomingImgWire.FromPort;
                    foreach (var outWire in outgoingImgWires)
                    {
                        var downstreamInPort = outWire.ToPort;
                        ActiveGraph.Disconnect(outWire.Connection.FromNodeId, outWire.Connection.FromPortId, outWire.Connection.ToNodeId, outWire.Connection.ToPortId);
                        GraphConnections.Remove(outWire);

                        if (ActiveGraph.Connect(upstreamOutPort.ParentNode.Id, upstreamOutPort.Id, downstreamInPort.ParentNode.Id, downstreamInPort.Id))
                        {
                            var bridged = ActiveGraph.Connections.Last();
                            GraphConnections.Add(new ConnectionViewModel(bridged, upstreamOutPort, downstreamInPort));
                        }
                    }
                }
            }

            var removedWires = GraphConnections.Where(c => c.Connection.FromNodeId == id || c.Connection.ToNodeId == id).ToList();
            ActiveGraph.RemoveNode(id);
            foreach (var c in removedWires)
            {
                GraphConnections.Remove(c);
                if (c.Connection.FromNodeId == id && c.Connection.ToNodeId != id)
                {
                    ResetTargetNodePortState(c.ToPort.ParentNode.Node, c.ToPort.Id);
                }
            }
            GraphNodes.Remove(nodeVm);
        }

        SelectedNode = null;
        if (IsLiveRunning)
        {
            _ = RunPipelineAsync();
        }
    }

    private int _isExecutingPipeline = 0;
    private bool _hasPendingPipelineRun = false;

    public async Task RunPipelineAsync()
    {
        if (Interlocked.CompareExchange(ref _isExecutingPipeline, 1, 0) != 0)
        {
            _hasPendingPipelineRun = true;
            return;
        }

        try
        {
            do
            {
                _hasPendingPipelineRun = false;
                var context = new GraphExecutionContext();
                var res = await _executor.ExecuteAsync(ActiveGraph, context);

                _lastRootSceneMat = res.RootSceneMat;
                _lastCompositeMat = res.CompositePreviewMat ?? res.FinalPreviewMat;
                _lastExecResult = res;

                Mat? targetPreview = ResolveTargetPreviewMat(ResolveCompositeForSelection());

                BitmapSource? bmp = null;
                int w = 0, h = 0;
                if (targetPreview != null && !targetPreview.Empty())
                {
                    w = targetPreview.Width;
                    h = targetPreview.Height;
                    var cloneForUi = targetPreview.Clone();
                    bmp = await Task.Run(() =>
                    {
                        using (cloneForUi)
                        {
                            return BitmapHelper.MatToBitmapSource(cloneForUi);
                        }
                    });
                }

                void ApplyUiUpdates()
                {
                    ClearLiveContrastPreview();
                    TotalExecutionTimeMs = res.TotalExecutionTimeMs;
                    OverallVerdict = res.OverallVerdict;
                    _currentPreviewMat = targetPreview;
                    CurrentPreviewSource = bmp;
                    PreviewWidth = w;
                    PreviewHeight = h;

                    foreach (var nVm in GraphNodes)
                    {
                        nVm.SyncRuntimeMetrics();
                        nVm.RefreshLocalization();
                    }

                    if (IsToleranceVerdictSelected)
                    {
                        SyncToleranceRulesFromNode();
                    }

                    PipelineRunCompleted?.Invoke(this, EventArgs.Empty);
                    OnPropertyChanged(nameof(CurrentSourceNode));
                    OnPropertyChanged(nameof(SelectedSourceFileItem));
                    _suppressAutoApplyParamEvents = true;
                    try
                    {
                        CurrentShapeMatchNode?.NotifyBalancedParametersChanged();
                        CurrentTrainShapeNode?.NotifyBalancedParametersChanged();
                    }
                    finally
                    {
                        _suppressAutoApplyParamEvents = false;
                    }
                    OnPropertyChanged(nameof(CurrentShapeMatchNode));
                    OnPropertyChanged(nameof(CurrentTrainShapeNode));
                    OnPropertyChanged(nameof(SelectedNodeMeasurements));
                    StatusMessage = $"Pipeline run completed in {TotalExecutionTimeMs:F1}ms | Nodes: {GraphNodes.Count}";
                }

                if (Application.Current?.Dispatcher is { } disp && !disp.CheckAccess())
                {
                    await disp.InvokeAsync(ApplyUiUpdates);
                }
                else
                {
                    ApplyUiUpdates();
                }
            } while (_hasPendingPipelineRun);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Pipeline error: {ex.Message}";
        }
        finally
        {
            Interlocked.Exchange(ref _isExecutingPipeline, 0);
        }
    }

    public async Task SwitchSampleAsync(string sampleName)
    {
        var srcNode = CurrentSourceNode ?? ActiveGraph.Nodes.OfType<SourceImageNode>().FirstOrDefault();
        if (srcNode != null)
        {
            AutoBalanceNodesOnNewInputImage();
            srcNode.SelectSampleByFileName(sampleName);
            OnPropertyChanged(nameof(CurrentSourceNode));
            OnPropertyChanged(nameof(SelectedSourceFileItem));
            SelectedNode?.UpdatePortPositions();
        }

        if (srcNode == null || !ActiveGraph.AutoExecute)
        {
            await RunPipelineAsync();
        }
    }

    private void OpenCustomImage()
    {
        var srcNode = CurrentSourceNode ?? ActiveGraph.Nodes.OfType<SourceImageNode>().FirstOrDefault();
        var dlg = new OpenFileDialog
        {
            Filter = "Images (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.bmp|All Files (*.*)|*.*",
            InitialDirectory = GetSafeInitialDirectory(srcNode?.FolderPath),
            RestoreDirectory = true
        };
        if (dlg.ShowDialog() == true)
        {
            if (srcNode == null)
            {
                srcNode = new SourceImageNode();
                ActiveGraph.AddNode(srcNode);
                SyncFromGraph();
            }
            AutoBalanceNodesOnNewInputImage();
            srcNode.SetImageFile(dlg.FileName);
            OnPropertyChanged(nameof(CurrentSourceNode));
            OnPropertyChanged(nameof(SelectedSourceFileItem));
            SelectedNode?.UpdatePortPositions();
            if (!ActiveGraph.AutoExecute)
            {
                _ = RunPipelineAsync();
            }
        }
    }

    public void ResetPipeline(string preset = "metrology")
    {
        StopContinuousLoop();
        ActiveGraph = preset.ToLowerInvariant() switch
        {
            "blank" => PipelineGraph.CreateBlankPipeline(),
            "detect" => PipelineGraph.CreateShapeDetectionPipeline(),
            "blob" => PipelineGraph.CreateBlobInspectionPipeline(),
            "code" or "qr" or "barcode" => PipelineGraph.CreateCodeReadingPipeline(),
            "measure" => PipelineGraph.CreateDefaultInspectionPipeline(),
            _ => PipelineGraph.CreateCompleteMetrologyPipeline()
        };
        AttachGraph(ActiveGraph);

        SelectedNode = null;
        SyncFromGraph();
        GraphResetRequested?.Invoke(this, EventArgs.Empty);

        if (ActiveGraph.Nodes.Count == 0)
        {
            OverallVerdict = null;
            CurrentPreviewSource = null;
            PreviewWidth = 0;
            PreviewHeight = 0;
            TotalExecutionTimeMs = 0;
            StatusMessage = Loc.IsVietnamese
                ? "Đã tạo bảng trắng mới. Kéo/nhấn thẻ bên trái để thêm vào sơ đồ."
                : "Created blank pipeline. Click modules on the left palette to add nodes.";
        }
        else
        {
            _ = RunPipelineAsync();
        }
    }
}

