using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace TunVision.UI.Common;

public static class BitmapHelper
{
    public static BitmapSource MatToBitmapSource(Mat mat)
    {
        if (mat.Empty())
            throw new ArgumentException("Mat is empty", nameof(mat));

        PixelFormat format;
        int step = (int)mat.Step();

        if (mat.Channels() == 1)
        {
            format = PixelFormats.Gray8;
        }
        else if (mat.Channels() == 3)
        {
            format = PixelFormats.Bgr24;
        }
        else if (mat.Channels() == 4)
        {
            format = PixelFormats.Bgra32;
        }
        else
        {
            throw new NotSupportedException($"Channels {mat.Channels()} not supported");
        }

        var source = BitmapSource.Create(
            mat.Width,
            mat.Height,
            96,
            96,
            format,
            null,
            mat.Data,
            step * mat.Height,
            step);

        source.Freeze();
        return source;
    }
}
