using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ISRWorldGen.Core.Climate.Precipitation;
using ISRWorldGen.Core.Climate.Temperature;
using ISRWorldGen.Core.Climate.WaterBudget;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Materials;

namespace ISRWorldGen.Tests.L04C;

[TestClass]
public sealed class WaterBudgetInputAssociationTests
{
    public TestContext TestContext { get; set; } = null!;
    private static readonly WaterBudgetSettings Settings = new(.5d, .25d, 10d);
    private static readonly TemperatureEvaluation Temperature = TemperatureField.Evaluate(
        new LatitudeAxis(new WorldBlockPosition(0, 0), 0d, 1d, 100d),
        new TemperatureSettings(20d, .2d, .005d, -2d),
        new TemperatureInput(new WorldBlockPosition(0, 0), 0d, 0d));

    [TestMethod]
    public void RejectsMisattributedPrecipitationInsteadOfBalancingTheWrongCell()
    {
        // A successful lookup alone is insufficient: the returned ID must match.
        var source = new FixedSource(new PrecipitationField(99, 2d, 10d));
        try
        {
            WaterBudgetSolver.Solve([Input(42, 1)], source, Settings, [], 0);
            Assert.Fail("MISATTRIBUTED_PRECIPITATION_ACCEPTED");
        }
        catch (ArgumentException exception)
        {
            Assert.AreEqual("precipitation", exception.ParamName);
        }
    }

    [TestMethod]
    public void RejectsNonFiniteOrNegativeAtmosphericMoistureAtTheHandoff()
    {
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
        {
            var source = new FixedSource(new PrecipitationField(42, invalid, 10d));
            ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
                WaterBudgetSolver.Solve([Input(42, 1)], source, Settings, [], 0));
            Assert.AreEqual("precipitation", exception.ParamName);
        }
    }

    [TestMethod]
    public void MissingRainAndInvalidRainRemainErrorsRatherThanDryCells()
    {
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
        {
            Assert.ThrowsExactly<ArgumentException>(() => WaterBudgetSolver.Solve([Input(42, 1)],
                new FixedSource(new PrecipitationField(42, 2d, invalid)), Settings, [], 0));
        }
        Assert.ThrowsExactly<ArgumentException>(() => WaterBudgetSolver.Solve([Input(42, 1)],
            new FixedSource(new PrecipitationField(42, 0d, 0d), found: false), Settings, [], 0));
    }

    [TestMethod]
    public void ValidZeroRainAndZeroIdAreNotMistakenForMissingData()
    {
        WaterBudgetSnapshot snapshot = WaterBudgetSolver.Solve([Input(0, 1)],
            new FixedSource(new PrecipitationField(0, 0d, 0d)), Settings, [], 0);
        WaterBudgetCell cell = snapshot.Cells.Single();
        Assert.AreEqual(0d, cell.PrecipitationModelLengthPerYear);
        Assert.AreEqual(0d, cell.RunoffModelLengthPerYear);
        Assert.AreEqual(0d, cell.RechargeModelLengthPerYear);
        Assert.AreEqual(.25d, cell.SoilMoistureNormalized);
    }

    [TestMethod]
    public void InvalidSecondCellFailsBeforeAnyTransferEnumerationOrPublication()
    {
        bool transfersEnumerated = false;
        IEnumerable<GroundwaterTransfer> Transfers()
        {
            transfersEnumerated = true;
            yield break;
        }
        var source = new FixedSource(new PrecipitationField(1, 2d, 10d));
        Assert.ThrowsExactly<ArgumentException>(() =>
            WaterBudgetSolver.Solve([Input(1, 1), Input(2, 2)], source, Settings, Transfers(), 0));
        Assert.IsFalse(transfersEnumerated);
        Assert.AreEqual(new PrecipitationField(1, 2d, 10d), source.Fields[0]);
    }

    [TestMethod]
    public void ManyReservoirsKeepLocalConservationAndCanonicalTransferOrder()
    {
        const int count = 2_048;
        WaterBudgetInput[] inputs = Enumerable.Range(0, count).Select(i => Input(i * 2L, (ulong)i + 1)).ToArray();
        var rain = new CorrespondingSource(inputs.Select(input => new PrecipitationField(input.CellId, 2d, 10d)));
        GroundwaterTransfer[] transfers = Enumerable.Range(0, count).Select(i => new GroundwaterTransfer(
            Id((ulong)count - (ulong)i), inputs[i].ReservoirId, inputs[(i + 1) % count].ReservoirId,
            2.5d, i % 2 == 0 ? GroundwaterTransferKind.Resurgence : GroundwaterTransferKind.Loss)).ToArray();
        WaterBudgetSnapshot first = WaterBudgetSolver.Solve(inputs, rain, Settings, transfers, 7);
        WaterBudgetSnapshot second = WaterBudgetSolver.Solve(inputs.Reverse(), rain, Settings, transfers.Reverse(), 7);
        CollectionAssert.AreEqual(first.Cells.ToArray(), second.Cells.ToArray());
        CollectionAssert.AreEqual(first.Transfers.ToArray(), second.Transfers.ToArray());
        Assert.AreEqual(count, first.Transfers.Count);
        foreach (WaterBudgetCell cell in first.Cells)
        {
            Assert.AreEqual(10d, cell.PrecipitationModelLengthPerYear);
            Assert.AreEqual(4d, cell.ActualEvapotranspirationModelLengthPerYear);
            Assert.AreEqual(1.5d, cell.StorageChangeModelLengthPerYear);
            Assert.AreEqual(4.5d, cell.RechargeModelLengthPerYear);
            Assert.AreEqual(18d, cell.RechargeModelVolumePerYear);
            Assert.AreEqual(0d, cell.ResidualModelLengthPerYear);
        }
        Assert.IsTrue(transfers.All(transfer => transfer.FlowModelVolumePerYear == 2.5d));
    }

    [TestMethod]
    public void OverdrawCannotBorrowTheRechargeOfAnotherIndexedReservoir()
    {
        WaterBudgetInput[] inputs = [Input(1, 1) with { AreaModelSquareLength = 1d },
            Input(2, 2) with { AreaModelSquareLength = 1_000d }];
        var rain = new CorrespondingSource([new PrecipitationField(1, 0d, 10d), new PrecipitationField(2, 0d, 10d)]);
        GroundwaterTransfer transfer = new(Id(99), Id(1), Id(2), 5d, GroundwaterTransferKind.Resurgence);
        Assert.ThrowsExactly<ArgumentException>(() => WaterBudgetSolver.Solve(inputs, rain, Settings, [transfer], 0));
        Assert.ThrowsExactly<ArgumentException>(() => WaterBudgetSolver.Solve(inputs.Reverse(), rain, Settings, [transfer], 0));
    }

    [TestMethod]
    [DoNotParallelize]
    public void ValidClimatePipelineProducesStableBaselineFingerprint()
    {
        const int width = 16, height = 8;
        PrecipitationCell[] cells = Enumerable.Range(0, width * height).Select(index =>
        {
            int x = index % width, z = index / width;
            long id = index == 0 ? long.MinValue : index == width * height - 1 ? long.MaxValue : index * 2L;
            return new PrecipitationCell(id, x, z, new WorldBlockPosition(x, z),
                (x % 5) * 32d, x == 0, Temperature);
        }).ToArray();
        long[] ports = cells.Where(cell => cell.GridX == 0).Select(cell => cell.Id).ToArray();
        PrecipitationSnapshot rain = PrecipitationSolver.Solve(cells.Reverse(), new WindVector(1, 0),
            MoistureBoundaryCondition.OpenGlobalOcean, 12d, ports.Reverse(), new PrecipitationSettings(4d, .25d, 1d / 256d));
        WaterBudgetInput[] inputs = cells.Select((cell, i) => Input(cell.Id, (ulong)i + 1) with
        {
            Material = new MaterialProperties(0d, 0d, (i % 5) / 4d),
            Temperature = cell.Temperature
        }).ToArray();
        WaterBudgetSnapshot local = WaterBudgetSolver.Solve(inputs, rain, Settings, [], 3);
        WaterBudgetCell[] ordered = local.Cells.ToArray();
        GroundwaterTransfer[] transfers = ordered.Select((cell, i) => new GroundwaterTransfer(
            Id((ulong)i + 1), cell.ReservoirId, ordered[(i + 1) % ordered.Length].ReservoirId,
            cell.RechargeModelVolumePerYear * .25d, GroundwaterTransferKind.Resurgence)).ToArray();
        WaterBudgetSnapshot coupled = WaterBudgetSolver.Solve(inputs.Reverse(), rain, Settings, transfers.Reverse(), 3);
        CollectionAssert.AreEqual(local.Cells.ToArray(), coupled.Cells.ToArray());
        Assert.IsTrue(coupled.Cells.All(cell => Math.Abs(cell.ResidualModelLengthPerYear) <= 1e-9d));
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new { rain.Fields, coupled.Cells, coupled.Transfers, coupled.Iteration });
        string fingerprint = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        TestContext.WriteLine("CLIMATE_PIPELINE_SHA256=" + fingerprint);
        WriteDiagnostics(cells, coupled, width, height, payload, fingerprint);
    }

    private void WriteDiagnostics(PrecipitationCell[] positions, WaterBudgetSnapshot water,
        int width, int height, byte[] payload, string fingerprint)
    {
        string root = Path.Combine(TestContext.TestRunDirectory ?? Path.GetTempPath(), "climate-pipeline-analytic-v1");
        Directory.CreateDirectory(root);
        string numeric = Path.Combine(root, "fields.json");
        File.WriteAllBytes(numeric, payload);
        TestContext.AddResultFile(numeric);
        Dictionary<long, WaterBudgetCell> cells = water.Cells.ToDictionary(cell => cell.CellId);
        var layers = new List<object>();
        foreach ((string name, Func<WaterBudgetCell, double> value) in new (string, Func<WaterBudgetCell, double>)[]
        {
            ("precipitation", cell => cell.PrecipitationModelLengthPerYear),
            ("runoff", cell => cell.RunoffModelLengthPerYear),
            ("recharge", cell => cell.RechargeModelLengthPerYear)
        })
        {
            // Fixed linear greyscale, 0..16 L/Ymod; never normalize per rendering.
            var svg = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width * 16}\" height=\"{height * 16}\" viewBox=\"0 0 {width} {height}\"><title>{name}; analytic fixture; L/Ymod; black=0, white=16</title>");
            foreach (PrecipitationCell position in positions)
            {
                int shade = (int)Math.Round(Math.Clamp(value(cells[position.Id]) / 16d, 0d, 1d) * 255d);
                svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{position.GridX}\" y=\"{position.GridZ}\" width=\"1\" height=\"1\" fill=\"rgb({shade},{shade},{shade})\"/>");
            }
            svg.Append("</svg>");
            string path = Path.Combine(root, name + ".svg");
            byte[] bytes = Encoding.UTF8.GetBytes(svg.ToString());
            File.WriteAllBytes(path, bytes);
            TestContext.AddResultFile(path);
            layers.Add(new { path = name + ".svg", sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() });
        }
        string manifest = Path.Combine(root, "manifest.json");
        File.WriteAllBytes(manifest, JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, fixture = "climate-pipeline-analytic-v1", seed = "none: closed-form analytical fixture",
            scope = "CORE_CLIMATE_WATER_ONLY", commit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "UNVERIFIED_LOCAL_COMMIT",
            extent = new { xMinInclusive = 0, zMinInclusive = 0, xMaxExclusive = width, zMaxExclusive = height },
            width, height, units = "L/Ymod", palette = "linear-greyscale-0-to-16-v1", fieldSha256 = fingerprint,
            precipitationAlgorithmVersion = PrecipitationSnapshot.AlgorithmVersion,
            waterAlgorithmVersion = WaterBudgetSnapshot.AlgorithmVersion, iteration = water.Iteration, layers,
            nativeGame = "NOT_RUN"
        }));
        TestContext.AddResultFile(manifest);
    }

    private static WaterBudgetInput Input(long cellId, ulong reservoir) =>
        new(cellId, Id(reservoir), 4d, .25d, 8d, new MaterialProperties(0d, 0d, 1d), Temperature);
    private static StableId Id(ulong low) => new(0, low);

    private sealed class FixedSource(PrecipitationField field, bool found = true) : IPrecipitationFieldSource
    {
        public int AlgorithmVersion => PrecipitationSnapshot.AlgorithmVersion;
        public IReadOnlyList<PrecipitationField> Fields { get; } = Array.AsReadOnly(new[] { field });
        public bool TryGetField(long cellId, out PrecipitationField result) { result = field; return found; }
    }

    private sealed class CorrespondingSource(IEnumerable<PrecipitationField> fields) : IPrecipitationFieldSource
    {
        private readonly Dictionary<long, PrecipitationField> index = fields.ToDictionary(field => field.CellId);
        public int AlgorithmVersion => PrecipitationSnapshot.AlgorithmVersion;
        public IReadOnlyList<PrecipitationField> Fields => index.Values.OrderBy(field => field.CellId).ToArray();
        public bool TryGetField(long cellId, out PrecipitationField field) => index.TryGetValue(cellId, out field);
    }
}
