using System;
using System.IO;
using OpenCvSharp;
using TunVision.Core.Abstractions;
using TunVision.Core.Models;

namespace TunVision.Core.ShapeMatching;

public class AlignTool : VisionToolBase
{
    public ShapeModel? Model { get; set; }
    public float AngleMinDeg { get; set; } = -180f;
    public float AngleMaxDeg { get; set; } = 180f;
    public float AngleStepDeg { get; set; } = 1.0f;
    public float ScaleMin { get; set; } = 0.9f;
    public float ScaleMax { get; set; } = 1.1f;
    public float ScaleStep { get; set; } = 0.05f;
    public float MinScore { get; set; } = 0.5f;
    public float Greediness { get; set; } = 0.8f;
    public int MaxInstances { get; set; } = 5;
    public float MaxOverlap { get; set; } = 0.5f;
    public bool Subpixel { get; set; } = true;
    public ContourExtractionMode ContourMode { get; set; } = ContourExtractionMode.StegerSubpixel;
    public int ContrastLow { get; set; } = 35;
    public int ContrastHigh { get; set; } = 75;
    public int MinContrast { get; set; } = 9;
    public int MinComponentSize { get; set; } = 16;
    public int MinLength
    {
        get => MinComponentSize;
        set => MinComponentSize = value;
    }
    public int PyramidLevels { get; set; } = 0; // 0 = Auto
    public int LowestPyramid { get; set; } = 1; // 1-based: 1 = Level 0 (full res), 2 = Level 1 (half res), up to 10
    public int MaxFeatures { get; set; } = 400;
    public BaseRoi? SearchRoi { get; set; }

    public AlignTool(string name = "AlignTool") : base(name)
    {
    }

    public ContrastEstimationResult AutoEstimateParameters(Mat templateImage, BaseRoi? modelRoi = null)
    {
        Mat? mask = null;
        if (modelRoi != null)
        {
            mask = modelRoi.CreateMask(templateImage.Width, templateImage.Height);
        }
        try
        {
            var res = ShapeTrainer.AutoEstimateContrast(templateImage, mask);
            ContrastLow = res.ContrastLow;
            ContrastHigh = res.ContrastHigh;
            MinContrast = res.MinContrast;
            MinComponentSize = res.MinComponentSize;
            PyramidLevels = res.NumLevels;
            return res;
        }
        finally
        {
            mask?.Dispose();
        }
    }

    public void Train(
        Mat templateImage,
        BaseRoi? modelRoi = null,
        int numLevels = 4,
        int? contrastLow = null,
        int? contrastHigh = null,
        int? minLength = null,
        int? maxFeatures = null,
        ContourExtractionMode? contourMode = null,
        int? minContrast = null,
        int? minComponentSize = null)
    {
        Mat? mask = null;
        if (modelRoi != null)
        {
            mask = modelRoi.CreateMask(templateImage.Width, templateImage.Height);
        }

        try
        {
            int cLow = contrastLow ?? ContrastLow;
            int cHigh = contrastHigh ?? ContrastHigh;
            int mComp = minComponentSize ?? (minLength ?? MinComponentSize);
            int mFeat = maxFeatures ?? MaxFeatures;
            var cMode = contourMode ?? ContourMode;
            int mContrast = minContrast ?? MinContrast;
            int actualLevels = PyramidLevels > 0 ? PyramidLevels : numLevels;

            Model = ShapeTrainer.Train(
                templateImage,
                mask,
                actualLevels,
                AngleMinDeg,
                AngleMaxDeg - AngleMinDeg,
                AngleStepDeg,
                ScaleMin,
                ScaleMax,
                ScaleStep,
                cLow,
                cHigh,
                mComp,
                mFeat,
                cMode,
                mContrast,
                mComp);
        }
        finally
        {
            mask?.Dispose();
        }
    }

    protected override IVisionResult Process(Mat image)
    {
        if (Model == null)
        {
            return new AlignResult
            {
                Success = false,
                Message = "Model not trained or loaded"
            };
        }

        Rect? roiRect = SearchRoi?.GetBoundingBox();
        var matches = ShapeMatcher.Match(
            image,
            Model,
            AngleMinDeg,
            AngleMaxDeg,
            AngleStepDeg,
            ScaleMin,
            ScaleMax,
            ScaleStep,
            MinScore,
            Greediness,
            MaxInstances,
            MaxOverlap,
            Subpixel,
            roiRect,
            LowestPyramid);

        return new AlignResult
        {
            Success = matches.Count > 0,
            Matches = matches,
            Message = $"Found {matches.Count} instance(s)"
        };
    }

    public override void SaveModel(string path)
    {
        if (Model == null) throw new InvalidOperationException("No model to save");
        if (path.EndsWith(".shm", StringComparison.OrdinalIgnoreCase))
            Model.SaveShm(path);
        else
            Model.SaveJson(path);
    }

    public override void LoadModel(string path)
    {
        if (path.EndsWith(".shm", StringComparison.OrdinalIgnoreCase))
            Model = ShapeModel.LoadShm(path);
        else
            Model = ShapeModel.LoadJson(path);

        AngleMinDeg = Model.AngleStartDeg;
        AngleMaxDeg = Model.AngleStartDeg + Model.AngleExtentDeg;
        AngleStepDeg = Model.AngleStepDeg;
        ScaleMin = Model.ScaleMin;
        ScaleMax = Model.ScaleMax;
        ScaleStep = Model.ScaleStep;
        ContrastLow = Model.ContrastLow;
        ContrastHigh = Model.ContrastHigh;
        MinContrast = Model.MinContrast;
        MinComponentSize = Model.MinComponentSize;
    }
}
