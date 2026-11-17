using System;
using System.Diagnostics;

namespace TunVision.Tests;

public delegate void CheckDelegate(bool condition, string testName, string details = "");

/// <summary>
/// Master Coordinator for TunVision Clean-Room E2E Verification Suites (Tiers 1 through 4).
/// Executes all 159 new E2E tests across 14 features, validating full system integrity.
/// </summary>
public static class TestE2ETiersCleanRoom
{
    public static void RunAll(string imgsDir, CheckDelegate check)
    {
        Console.WriteLine("\n######################################################################");
        Console.WriteLine("    TUNVISION STUDIO CLEAN-ROOM E2E VERIFICATION SUITE (TIERS 1 - 4)  ");
        Console.WriteLine("######################################################################");

        var sw = Stopwatch.StartNew();

        // 1. Tier 1: Feature Coverage (>=5 tests per feature for all 14 features = 70 tests)
        TestE2ETier1FeatureCoverage.RunAll(imgsDir, check);

        // 2. Tier 2: Boundary & Corner Cases (>=5 tests per feature for all 14 features = 70 tests)
        TestE2ETier2BoundaryCases.RunAll(imgsDir, check);

        // 3. Tier 3: Cross-Feature Interactions (14 pairwise and multi-feature tests)
        TestE2ETier3CrossFeature.RunAll(imgsDir, check);

        // 4. Tier 4: Real-World Industrial Application Scenarios (5 industrial workloads)
        TestE2ETier4IndustrialScenarios.RunAll(imgsDir, check);

        sw.Stop();
        Console.WriteLine("\n######################################################################");
        Console.WriteLine($"    COMPLETED ALL E2E TIERS 1 - 4 IN {sw.ElapsedMilliseconds} ms");
        Console.WriteLine("######################################################################\n");
    }
}
