using ISRWorldGen.Core.Geology.Evolution;

internal static class PolarityRegressionChecks
{
    public static string[] Run()
    {
        string[] cases = ["nearly equal thermal ages cannot reverse subduction polarity", "resolved material class is invariant under transport round-off", "resolvable age contrast still controls polarity", "same age across half-quantum does not reverse slab", "material contrast is compared before rounding"];
        for (int i = -8; i <= 8; i++)
        {
            double a = 86 + i * 1e-12, b = 86 - i * 1e-12;
            var left = CrustResponse.Choose(1, 0, 7, a, 2, 0, 7, b);
            var swapped = CrustResponse.Choose(2, 0, 7, b, 1, 0, 7, a);
            Require(left == swapped && left.SubductingPlate == 2, cases[0]);
            var mixed = CrustResponse.Choose(1, 7 + i * 1e-14, 7, 86, 2, 35, 0, 0);
            Require(mixed.Kind == TectonicContactKind.Subduction && mixed.SubductingPlate == 1, cases[1]);
        }
        Require(CrustResponse.Choose(1, 0, 7, 86.001, 2, 0, 7, 86).SubductingPlate == 1, cases[2]);
        // Exact cause from the Windows/Linux histories: step 129 compares the
        // original ocean cohort at 50 + 24.4140625 model Myr. Its last bits differ.
        foreach (double commonAge in new[] { .0000005, 50.0000005, 74.4140625, 99.9999995, 150.1234565 })
        for (int i = -8; i <= 8; i++)
        {
            double a = commonAge + i * 1e-12, b = commonAge - i * 1e-12;
            var choice = CrustResponse.Choose(1, 0, 7, a, 2, 0, 7, b);
            Require(choice.SubductingPlate == 2 && choice == CrustResponse.Choose(2, 0, 7, b, 1, 0, 7, a), cases[3]);
            // The rounding-cell boundary is not a material boundary either.
            double material = 7.0000000005;
            var mixed = CrustResponse.Choose(1, material + i * 1e-14, material - i * 1e-14, 60,
                2, 35, 0, 0);
            Require(mixed.SubductingPlate == 1 && mixed.Kind == TectonicContactKind.Subduction, cases[4]);
        }
        foreach (string name in cases) Console.WriteLine("CHECK PASS: " + name);
        return cases;
    }
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
}
