using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace TestX86Lic;

[StructLayout(LayoutKind.Sequential)]
public struct MwwzMatch
{
    public int X;
    public int Y;
    public int Label;
    public float Angle;
    public float Scale;
    public float Scale2;
    public float Score;
    public byte Polarity;
    public byte Pad0, Pad1, Pad2;
    public float Dx;
    public float Dy;
}

class ProgramContour
{
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool SetDllDirectory(string lpPathName);

    [DllImport("kernel32.dll")]
    static extern IntPtr LoadLibrary(string path);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetProcAddress(IntPtr h, string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int InitMwwzDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int CreateShapeModelDelegate(
        IntPtr pGray, int w, int h, int num_levels,
        float angle_start, float angle_extent, float angle_step,
        int use_polarity, IntPtr contrast, IntPtr pMask, out int outId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int FindPlanarDelegate(
        IntPtr pGray, int w, int h, int model_id,
        float angle_start, float angle_extent,
        float scale_rmin, float scale_rmax,
        float scale_cmin, float scale_cmax,
        float minScore, int numMatches, float maxOverLap,
        int subpixel, IntPtr numLevels, float greediness,
        out IntPtr pMatches, out int nFound,
        out IntPtr pHom, out IntPtr pExtra);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int GetModelContourHomDelegate(
        int model_id, IntPtr homography3x3, out IntPtr pTempl);

    static void Main()
    {
        string dllDir = @"d:\tunvision\mwwz-samples\demo_mfc_upgraded";
        SetDllDirectory(dllDir);
        Directory.SetCurrentDirectory(dllDir);

        IntPtr hMwwz = LoadLibrary(Path.Combine(dllDir, "mwwz.dll"));
        Console.WriteLine($"hMwwz = {hMwwz}");
        if (hMwwz == IntPtr.Zero) return;

        var init = Marshal.GetDelegateForFunctionPointer<InitMwwzDelegate>(GetProcAddress(hMwwz, "init_mwwz"));
        init();

        var createModel = Marshal.GetDelegateForFunctionPointer<CreateShapeModelDelegate>(GetProcAddress(hMwwz, "create_shape_model"));
        var findPlanar = Marshal.GetDelegateForFunctionPointer<FindPlanarDelegate>(GetProcAddress(hMwwz, "find_planar_uncalib_deformable_model"));

        // Read engine_part_054.png (model)
        string modelPng = @"d:\tunvision\mwwz-samples\imgs\engine_part_054.png";
        string scenePng = @"d:\tunvision\mwwz-samples\imgs\engine_part_099.png";

        // Read using OpenCV
        using var tplMat = OpenCvSharp.Cv2.ImRead(modelPng, OpenCvSharp.ImreadModes.Grayscale);
        using var scnMat = OpenCvSharp.Cv2.ImRead(scenePng, OpenCvSharp.ImreadModes.Grayscale);

        Console.WriteLine($"tpl: {tplMat.Width}x{tplMat.Height}, scn: {scnMat.Width}x{scnMat.Height}");

        int[] contrast = new int[] { 15, 30, 10 };
        unsafe
        {
            fixed (int* pContrast = contrast)
            {
                int outId = -999;
                int retId = createModel(tplMat.Data, tplMat.Width, tplMat.Height, 4, 0, 360, 1.0f, 1, (IntPtr)pContrast, IntPtr.Zero, out outId);
                int modelId = retId >= 0 ? retId : outId;
                Console.WriteLine($"create_shape_model returned retId={retId}, outId={outId} -> modelId={modelId}");

                if (modelId >= 0)
                {
                    int[] numLvls = new int[4];
                    fixed (int* pNumLvls = numLvls)
                    {
                        var sw = Stopwatch.StartNew();
                        int retF = findPlanar(
                            scnMat.Data, scnMat.Width, scnMat.Height, modelId,
                            0f, 360f,
                            0.85f, 1.15f,
                            0.85f, 1.15f,
                            0.30f, 5, 0.5f,
                            2, (IntPtr)pNumLvls, 0.90f,
                            out IntPtr pMatches, out int nFound,
                            out IntPtr pHom, out IntPtr pExtra);
                        sw.Stop();
                        Console.WriteLine($"findPlanar returned {retF}, nFound={nFound} in {sw.ElapsedMilliseconds} ms");
                        Console.WriteLine($"pMatches={pMatches}, nFound={nFound}, pHom={pHom}, pExtra={pExtra}");

                        if (nFound > 0 && pMatches != IntPtr.Zero)
                        {
                            var match = Marshal.PtrToStructure<MwwzMatch>(pMatches);
                            Console.WriteLine($"Match: X={match.X}, Y={match.Y}, Angle={match.Angle}, Score={match.Score}");
                        }
                    }
                }
            }
        }
    }
}
