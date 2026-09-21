using ISRWorldGen.Core.Geology.Evolution;

internal static class PolarityRegressionChecks
{
    public static string[] Run()
    {
        string[] cases = ["nearly equal thermal ages cannot reverse subduction polarity", "resolved material class is invariant under transport round-off", "resolvable age contrast still controls polarity"];
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
        foreach (string name in cases) Console.WriteLine("CHECK PASS: " + name);
        return cases;
    }
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
}
