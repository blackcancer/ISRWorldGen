using System.Security.Cryptography;
using System.Text.Json;
using ISRWorldGen.Core.Climate.Precipitation;
using ISRWorldGen.Core.Climate.Temperature;
using ISRWorldGen.Core.Climate.WaterBudget;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Landscapes;
using ISRWorldGen.Core.Geology.Materials;
using ISRWorldGen.Core.Hydrology.Depressions;
using ISRWorldGen.Core.Hydrology.Discharge;
using ISRWorldGen.Tests.L03B;

namespace ISRWorldGen.Tests.L05A;

/// <summary>
/// A diagnostic integration fixture of the current Core, not the final worldgen
/// pipeline. Actual L03-B relief, L04 climate and L05 routing are exported without
/// erosion, voxelization, native seasons or a claim of in-game qualification.
/// </summary>
[TestClass]
public sealed class SpatialDiagnosticExportTests
{
    private const int Side = 256;
    private const int Seed = -437287116;
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void ExportActualCoreReliefRegionsClimateAndDrainageFields()
    {
        var profile = L03BTestSupport.FrozenProfile("balanced");
        var identity = L03BTestSupport.Identity(Seed, profile);
        var (atlas, plates) = L03BTestSupport.PlateFixture(Seed, profile);
        LandscapeModel model = L03BTestSupport.Success(LandscapeModelBuilder.Build(identity, atlas, plates,
            profile, new LandscapeGenerationSettings(new ReliefBudgetRequest(64, 48, 128), profile.SiteQuota, 1.25)));
        int count = Side * Side;
        long stepX = profile.WidthBlocks / Side, stepZ = profile.LengthBlocks / Side;
        double sea = model.VerticalPlan.Transform.SeaLevelBlocks;
        var samples = new LandscapeSample[count];
        var height = new double[count];
        var positions = new WorldBlockPosition[count];
        var cellIndex = model.Cells.Select((cell, index) => (cell.CellId, index)).ToDictionary(item => item.CellId, item => item.index);
        var plateIds = model.Cells.Select(cell => cell.PlateId).Distinct().OrderBy(id => id.High).ThenBy(id => id.Low).ToArray();
        var plateIndex = plateIds.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index);
        var regions = new int[count]; var plate = new int[count]; var families = new int[count];
        for (int z = 0; z < Side; z++)
        for (int x = 0; x < Side; x++)
        {
            int i = z * Side + x;
            positions[i] = new WorldBlockPosition(x * stepX + stepX / 2, z * stepZ + stepZ / 2);
            samples[i] = model.Sample(positions[i].X, positions[i].Z);
            height[i] = samples[i].AltitudeBlocks;
            Assert.IsTrue(double.IsFinite(height[i]) && height[i] >= 0 && height[i] < profile.HeightBlocks);
            regions[i] = cellIndex[samples[i].DominantCellId];
            plate[i] = plateIndex[model.Cells[regions[i]].PlateId];
            families[i] = (int)samples[i].DominantFamily;
            if (i % 1024 == 0) Assert.AreEqual(samples[i], model.Sample(positions[i].X, positions[i].Z));
        }

        // Saline ocean requires sub-sea connectivity to a world edge. An enclosed
        // sub-sea basin must not become ocean merely because its altitude is low.
        bool[] ocean = OceanMask(height, sea);
        int[] coastDistance = DistanceFromOcean(ocean);
        var axis = new LatitudeAxis(new WorldBlockPosition(profile.WidthBlocks / 2, profile.LengthBlocks / 2),
            0, 1, profile.LengthBlocks / 120d);
        var temperatureSettings = new TemperatureSettings(28, 0.42, 0.06, -4);
        var evaluations = new TemperatureEvaluation[count];
        var precipitationCells = new PrecipitationCell[count];
        var drainageCells = new DrainageCell[count];
        for (int i = 0; i < count; i++)
        {
            evaluations[i] = TemperatureField.Evaluate(axis, temperatureSettings,
                new TemperatureInput(positions[i], height[i] - sea, Math.Min(1d, coastDistance[i] / 32d)));
            precipitationCells[i] = new PrecipitationCell(i, i % Side, i / Side, positions[i], height[i], ocean[i], evaluations[i]);
            // Connected marine cells are explicit sinks in this diagnostic grid.
            // This is a declared fixture boundary, not a native ocean placement pass.
            drainageCells[i] = new DrainageCell(i, height[i], Neighbours(i, true),
                ocean[i] ? DrainageTerminalKind.Ocean : null, ocean[i]);
        }
        PrecipitationSnapshot rain = PrecipitationSolver.Solve(precipitationCells, new WindVector(1, 0),
            MoistureBoundaryCondition.Closed, 0, Array.Empty<long>(), new PrecipitationSettings(1, 0.12, 0.025));
        var material = new MaterialProperties(0, 0, 0.35);
        WaterBudgetInput[] inputs = Enumerable.Range(0, count).Select(i => new WaterBudgetInput(i,
            new StableId(0, (ulong)i), (double)stepX * stepZ, 0.25, 0.3, material, evaluations[i])).ToArray();
        WaterBudgetSnapshot water = WaterBudgetSolver.Solve(inputs, rain, new WaterBudgetSettings(0.5, 0.25, 2),
            Array.Empty<GroundwaterTransfer>(), 0);
        DrainageTopology topology = DepressionTopologyBuilder.Build(drainageCells, sea);
        DischargeSnapshot discharge = DischargeAccumulator.Accumulate(water, topology, Array.Empty<DischargeAdjustment>(),
            new DischargeClassificationSettings(stepX * (double)stepZ, 8d * stepX * stepZ, 8d * stepX * stepZ));
        Assert.HasCount(count, topology.Cells);
        Assert.HasCount(count, discharge.Reaches);
        Assert.IsLessThanOrEqualTo(1e-9 + 1e-6 * discharge.Balance.ReferenceFlowModelVolumePerYear,
            Math.Abs(discharge.Balance.ResidualModelVolumePerYear));
        for (int i = 0; i < count; i++)
        {
            Assert.AreEqual(height[i], topology.Cells[i].PhysicalElevation);
            if (topology.Cells[i].ReceiverId is long receiver)
                Assert.IsLessThanOrEqualTo(topology.Cells[i].RoutingElevation, topology.Cells[(int)receiver].RoutingElevation);
            Assert.IsGreaterThanOrEqualTo(0d, discharge.Reaches[i].DischargeModelVolumePerYear);
        }

        long[] terminalIds = topology.Connectivity.Select(item => item.TerminalCellId).Distinct().Order().ToArray();
        var basinIndex = terminalIds.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index);
        var fields = new Dictionary<string, object>
        {
            ["height"] = height,
            ["routing_height"] = topology.Cells.Select(item => item.RoutingElevation).ToArray(),
            ["region"] = regions, ["plate"] = plate, ["landscape_family"] = families,
            ["ocean"] = ocean, ["temperature"] = evaluations.Select(item => item.Sample.SurfaceCelsius).ToArray(),
            ["rain"] = rain.Fields.Select(item => item.PrecipitationModelLengthPerYear).ToArray(),
            ["runoff"] = water.Cells.Select(item => item.RunoffModelLengthPerYear).ToArray(),
            ["recharge"] = water.Cells.Select(item => item.RechargeModelLengthPerYear).ToArray(),
            ["soil_moisture"] = water.Cells.Select(item => item.SoilMoistureNormalized).ToArray(),
            ["discharge"] = discharge.Reaches.Select(item => item.DischargeModelVolumePerYear).ToArray(),
            ["drainage_area"] = discharge.Reaches.Select(item => item.DrainageAreaModelSquareLength).ToArray(),
            ["receiver"] = topology.Cells.Select(item => item.ReceiverId ?? -1).ToArray(),
            ["basin"] = topology.Connectivity.Select(item => basinIndex[item.TerminalCellId]).ToArray()
        };
        byte[] raw = JsonSerializer.SerializeToUtf8Bytes(fields);
        string sourceHash = Hash(raw);
        string parent = Environment.GetEnvironmentVariable("ISR_SPATIAL_DIAGNOSTICS_ROOT") ??
            Path.Combine(L03BTestSupport.FindRepositoryRoot(), ".local", "spatial-diagnostics", Guid.NewGuid().ToString("N"));
        string output = Path.Combine(parent, "balanced-seed-" + Seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (Directory.Exists(output)) throw new IOException("Diagnostic output already exists; previous evidence is not overwritten.");
        Directory.CreateDirectory(output);
        WriteNew(Path.Combine(output, "fields.json"), raw);
        string commit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "WORKING_TREE_UNVERIFIED";
        byte[] metadata = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, scope = "CORE_SPATIAL_DIAGNOSTIC_NOT_FINAL_WORLD", status = "PASS",
            test = nameof(ExportActualCoreReliefRegionsClimateAndDrainageFields), commit, seed = Seed,
            profile = profile.Id, width = Side, height = Side, stepX, stepZ,
            extent = new { minX = 0, minZ = 0, maxXExclusive = profile.WidthBlocks, maxZExclusive = profile.LengthBlocks },
            sampling = "cell centres; row-major; X right, positive Z down; no interpolation",
            seaLevel = sea, worldHeight = profile.HeightBlocks,
            fieldsSha256 = sourceHash, coreAssemblySha256 = Hash(File.ReadAllBytes(typeof(LandscapeModel).Assembly.Location)),
            atlasChecksum = model.AtlasContentChecksum.ToString(), landscapeChecksum = model.ContentChecksum.ToString(),
            nativeGame = "NOT_RUN", finalErosion = "NOT_IMPLEMENTED_IN_THIS_FIXTURE",
            nativeStrataSoilsOresVegetationSnow = "NOT_REPRESENTED",
            units = new { height = "blocks", temperature = "model Celsius", rain = "L/Ymod", runoff = "L/Ymod",
                recharge = "L/Ymod", discharge = "L^3/Ymod", drainage_area = "L^2", soil_moisture = "fraction, not fertility" },
            fixture = new { upstreamProfile = "existing L03B analytical constraints, not a live native audit",
                reliefBudget = new[] { 64, 48, 128 }, overlap = 1.25, windX = 1, windZ = 0,
                oceanEvaporation = 1, baseCondensation = 0.12, orographicCondensation = 0.025,
                potentialET = 0.3, etFraction = 0.5, soilRetention = 0.25, storageCapacity = 2,
                uniformPermeability = 0.35, initialSoilMoisture = 0.25, groundwaterTransfers = 0,
                continentality = "four-neighbour distance from connected ocean / 32, saturated at 1",
                temperature = new { equator = 28, polewardCooling = 0.42, altitudeLapse = 0.06, inlandOffset = -4 },
                drainage = "eight-neighbour sampling graph; connected sub-sea ocean cells are explicit marine terminals" },
            legend = model.Cells.Select((cell, index) => new { region = index, cellId = cell.CellId.ToString(),
                plate = plateIndex[cell.PlateId], plateId = cell.PlateId.ToString(), family = cell.Family.ToString(),
                familyCode = (int)cell.Family }).ToArray(),
            basinTerminals = terminalIds,
            oracle = new { finiteHeight = true, repeatedSamples = true, physicalReliefUnmodified = true,
                receiverRoutingNonAscending = true, waterConservation = true, cells = count,
                oceanCells = ocean.Count(value => value), balance = discharge.Balance }
        }, new JsonSerializerOptions { WriteIndented = true });
        WriteNew(Path.Combine(output, "manifest.json"), metadata);
        TestContext.WriteLine("SPATIAL_DIAGNOSTICS=" + output);
        TestContext.WriteLine("SPATIAL_FIELDS_SHA256=" + sourceHash);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void WriteNew(string path, byte[] data)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(data);
    }

    private static IEnumerable<long> Neighbours(int id, bool diagonal)
    {
        int x = id % Side, z = id / Side;
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if ((dx == 0 && dz == 0) || (!diagonal && Math.Abs(dx) + Math.Abs(dz) != 1)) continue;
            int nx = x + dx, nz = z + dz;
            if (nx >= 0 && nx < Side && nz >= 0 && nz < Side) yield return nz * Side + nx;
        }
    }

    private static bool[] OceanMask(double[] height, double sea)
    {
        var result = new bool[height.Length];
        var queue = new Queue<int>();
        for (int i = 0; i < height.Length; i++)
            if ((i % Side == 0 || i % Side == Side - 1 || i / Side == 0 || i / Side == Side - 1) && height[i] < sea)
            { result[i] = true; queue.Enqueue(i); }
        while (queue.TryDequeue(out int id))
            foreach (long neighbour in Neighbours(id, false))
                if (!result[(int)neighbour] && height[(int)neighbour] < sea)
                { result[(int)neighbour] = true; queue.Enqueue((int)neighbour); }
        return result;
    }

    private static int[] DistanceFromOcean(bool[] ocean)
    {
        int[] result = Enumerable.Repeat(int.MaxValue, ocean.Length).ToArray();
        var queue = new Queue<int>();
        for (int i = 0; i < ocean.Length; i++) if (ocean[i]) { result[i] = 0; queue.Enqueue(i); }
        while (queue.TryDequeue(out int id))
            foreach (long neighbour in Neighbours(id, false))
                if (result[(int)neighbour] == int.MaxValue)
                { result[(int)neighbour] = result[id] + 1; queue.Enqueue((int)neighbour); }
        return result;
    }
}
