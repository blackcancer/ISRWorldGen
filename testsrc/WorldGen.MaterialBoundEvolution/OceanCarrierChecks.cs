using ISRWorldGen.Core.Geology.Evolution;

internal static class OceanCarrierChecks
{
    internal static string[] Run()
    {
        var checks = new List<string>();
        double[] o = [double.Epsilon, 0, 0, 0], m = [50 * double.Epsilon, 0, 0, 0];
        double[] east = [.125, .125, .125, .125], south = new double[4];
        double[] oldO = CrustTransport.Advect(o, east, south, 2, 1, 1, 1);
        double[] oldM = CrustTransport.Advect(m, east, south, 2, 1, 1, 1);
        Require(oldO[1] == 0 && oldM[1] > 0, "Pinned first-order operator did not reproduce the carrier/moment counterexample.");
        Pass("independently rounded baseline scalars reproduce a subnormal orphan moment");
        var fixture = MaterialPlateCohorts.Create(2, 1, new int[4], [35, 35, 35, 35], o, m, o);
        var moved = fixture.Advect(east, south, 1, 1, 1);
        Require(moved.Value(0, 1, 1) == 0 && moved.Value(0, 2, 1) == 0, "Moment entered a zero ocean packet.");
        Require(moved.Value(0, 1, 0) == double.Epsilon && moved.Value(0, 2, 0) == 50 * double.Epsilon, "Retention did not carry the rounded ocean packet.");
        Pass("representable donor packets retain their own age without an arbitrary floor");
        foreach (double amount in new[] { double.Epsilon, 32 * double.Epsilon, 1e-300, 1e-200, 1e-20, 7d })
        {
            var state = MaterialPlateCohorts.Create(2, 1, new int[4], [35,35,35,35], [amount,0,0,0], [50*amount,0,0,0], [amount,0,0,0]);
            for (int step = 0; step < 16; step++)
            {
                state = state.Advect(east, south, 1, 1, 1);
                for (int i = 0; i < 4; i++)
                {
                    double carrier = state.Value(0, 1, i);
                    Require(carrier > 0 || state.Value(0, 2, i) == 0, "Orphan age moment after repeated donor transport.");
                    Require(state.Value(0, 3, i) <= carrier, "Inherited constituent exceeds its carrier.");
                }
            }
        }
        Pass("normal and subnormal repeated transport preserve carrier relationships");
        var complete = fixture.ExchangeOcean(new double[4], [0,-1,-1,-1], [double.Epsilon,0,0,0]);
        Require(complete.Value(0, 1, 0) == 0 && complete.Value(0, 2, 0) == 0 && complete.Value(0, 3, 0) == 0, "Fully recycled column left an unsupported tracer.");
        Pass("complete recycling removes the exact carrier and its moments together");
        var normal = MaterialPlateCohorts.Create(2, 1, new int[4], [35,35,35,35], [7,3,2,1], [350,150,100,50], [7,3,2,1]);
        var result = normal.Advect(east, south, 1, 1, 1);
        var reference = CrustTransport.Advect([7,3,2,1], east, south, 2, 1, 1, 1);
        for (int i = 0; i < 4; i++)
        {
            Require(Math.Abs(result.Value(0,1,i)-reference[i]) < 1e-14, "Carrier law is not the donor stencil.");
            Require(Math.Abs(result.Value(0,2,i)-50*reference[i]) < 1e-12, "Resolved uniform age changed.");
        }
        Pass("resolved quantities retain the first-order donor law and constant age");
        return checks.ToArray();
        void Pass(string label) { checks.Add(label); Console.WriteLine("CHECK PASS: " + label); }
    }
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
}
