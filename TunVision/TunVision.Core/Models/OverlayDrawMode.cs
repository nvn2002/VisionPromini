namespace TunVision.Core.Models;

/// <summary>
/// Configurable display filter mode for match overlay graphics on VisionCanvas.
/// </summary>
public enum OverlayDrawMode
{
    /// <summary>
    /// Minimalist industrial mode: continuous vector contours and center crosshair only (no score badge, bounding box, or axes).
    /// </summary>
    NoScore,

    /// <summary>
    /// Standard inspection mode: vector contours, center crosshair, and match score badge (no bounding box or axes).
    /// </summary>
    Score,

    /// <summary>
    /// Comprehensive diagnostic mode: contours, center crosshair, score badge, rotated bounding box, and coordinate frame.
    /// </summary>
    All,

    /// <summary>
    /// Pure contour mode: continuous vector contours only (no crosshair, score badge, bounding box, or axes).
    /// </summary>
    ContourOnly
}
