using System.Security.Cryptography;
using System.Text.Json;
using ISRWorldGen.Core.Climate.Precipitation;
using ISRWorldGen.Core.Climate.Temperature;
using ISRWorldGen.Core.Climate.WaterBudget;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Landscapes;
using ISRWorldGen.Core.Geology.Materials;
using ISRWorldGen.Core.Geology.Plates;
using ISRWorldGen.Core.Hydrology.Depressions;
using ISRWorldGen.Core.Hydrology.Discharge;
using ISRWorldGen.Tests.L03B;

namespace ISRWorldGen.Tests.L05A;

/// <summary>
/// Paired diagnostic integration of actual Core algorithms, not native world
/// qualification. The bounded incision experiment is not the full L06 solver.
/// </summary>
[TestClass]
public sealed class SpatialDiagnosticExportTests
{
    private const int Side = 256;
    private const int Seed = -437287116;
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void ExportActualCoreReliefRegionsClimateAndDrainageFields() => Export(Seed, false);

    [TestMethod]
    [DataRow(-437287116, true)]
    [DataRow(73, false)]
    [DataRow(73, true)]
    [DataRow(20260906, false)]
    [DataRow(20260906, true)]
    public void ExportPairedGeographicRework(int seed, bool reworked) => Export(seed, reworked);

    private void Export(int seed, bool reworked)
    {
        var profile = L03BTestSupport.FrozenProfile("balanced");
        var identity = L03BTestSupport.Identity(seed, profile);
        var (atlas, plates) = L03BTestSupport.PlateFixture(seed, profile);
        LandscapeModel model = L03BTestSupport.Success(LandscapeModelBuilder.Build(identity, atlas, plates,
            profile, new LandscapeGenerationSettings(new ReliefBudgetRequest(64, 48, 128), profile.SiteQuota, 1.25)));
        ContinentalFieldModel continents = L03BTestSupport.Success(ContinentalFieldModel.Create(identity, atlas.Bounds,
            new ContinentalFieldSettings(5, 18, 64, 1_000_000)));
        TectonicReliefModel? tectonic = reworked ? TectonicReliefModel.Build(model, atlas, plates, new(400, .96, 512, 12), continents) : null;
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
            height[i] = tectonic?.Sample(positions[i].X, positions[i].Z).AltitudeBlocks ?? samples[i].AltitudeBlocks;
            Assert.IsTrue(double.IsFinite(height[i]) && height[i] >= 0 && height[i] < profile.HeightBlocks);
            regions[i] = cellIndex[samples[i].DominantCellId];
            plate[i] = plateIndex[model.Cells[regions[i]].PlateId];
            families[i] = (int)samples[i].DominantFamily;
            if (i % 1024 == 0) Assert.AreEqual(samples[i], model.Sample(positions[i].X, positions[i].Z));
        }

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
            drainageCells[i] = new DrainageCell(i, height[i], Neighbours(i, true),
                ocean[i] ? DrainageTerminalKind.Ocean : null, ocean[i]);
        }
        WeightedWind[] annualWinds = [new(1, 0, .55), new(-1, 0, .25), new(0, 1, .12), new(0, -1, .08)];
        DistancePrecipitationSettings distanceSettings = new(1.6, 35_000, 160, 18);
        IPrecipitationFieldSource rain = reworked
            ? DistancePrecipitationSolver.Solve(precipitationCells, stepX, stepZ, annualWinds, distanceSettings)
            : PrecipitationSolver.Solve(precipitationCells, new WindVector(1, 0),
                MoistureBoundaryCondition.Closed, 0, Array.Empty<long>(), new PrecipitationSettings(1, 0.12, 0.025));
        var material = new MaterialProperties(0, 0, 0.35);
        WaterBudgetInput[] inputs = Enumerable.Range(0, count).Select(i => new WaterBudgetInput(i,
            new StableId(0, (ulong)i), (double)stepX * stepZ, 0.25, 0.3, material, evaluations[i])).ToArray();
        WaterBudgetSnapshot water = WaterBudgetSolver.Solve(inputs, rain, new WaterBudgetSettings(0.5, 0.25, 2),
            Array.Empty<GroundwaterTransfer>(), 0);
        DrainageTopology topology = reworked
            ? MetricDrainageBuilder.Build(drainageCells, Enumerable.Range(0, count).ToDictionary(i => (long)i, i => positions[i]), sea)
            : DepressionTopologyBuilder.Build(drainageCells, sea);
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

        double[] initialHeight = (double[])height.Clone();
        const int incisionSteps = 12;
        DrainageIncisionSettings incisionSettings = new(.12, 1_000, 100_000);
        double exportedSediment = 0d;
        var metricPositions = Enumerable.Range(0, count).ToDictionary(i => (long)i, i => positions[i]);
        if (reworked)
        {
            // Initial climate supplies this bounded detachment-only relaxation.
            // Final climate and discharge are recalculated on the changed relief.
            for (int iteration = 0; iteration < incisionSteps; iteration++)
            {
                DrainageIncisionSnapshot step = DrainageReliefCoupling.Step(topology, discharge, metricPositions,
                    (double)stepX * stepZ, sea, incisionSettings);
                exportedSediment += step.ExportedSedimentModelVolume;
                for (int i = 0; i < count; i++)
                {
                    height[i] = step.Samples[i].After;
                    drainageCells[i] = new DrainageCell(i, height[i], Neighbours(i, true),
                        ocean[i] ? DrainageTerminalKind.Ocean : null, ocean[i]);
                }
                topology = MetricDrainageBuilder.Build(drainageCells, metricPositions, sea);
                discharge = DischargeAccumulator.Accumulate(water, topology, Array.Empty<DischargeAdjustment>(),
                    new DischargeClassificationSettings(stepX * (double)stepZ, 8d * stepX * stepZ, 8d * stepX * stepZ));
            }
            for (int i = 0; i < count; i++)
            {
                evaluations[i] = TemperatureField.Evaluate(axis, temperatureSettings,
                    new TemperatureInput(positions[i], height[i] - sea, Math.Min(1d, coastDistance[i] / 32d)));
                precipitationCells[i] = new PrecipitationCell(i, i % Side, i / Side, positions[i], height[i], ocean[i], evaluations[i]);
                inputs[i] = new WaterBudgetInput(i, new StableId(0, (ulong)i), (double)stepX * stepZ, .25, .3, material, evaluations[i]);
                Assert.IsLessThanOrEqualTo(initialHeight[i], height[i]);
                Assert.AreEqual(height[i], topology.Cells[i].PhysicalElevation);
            }
            rain = DistancePrecipitationSolver.Solve(precipitationCells, stepX, stepZ, annualWinds, distanceSettings);
            water = WaterBudgetSolver.Solve(inputs, rain, new WaterBudgetSettings(.5, .25, 2), Array.Empty<GroundwaterTransfer>(), 0);
            discharge = DischargeAccumulator.Accumulate(water, topology, Array.Empty<DischargeAdjustment>(),
                new DischargeClassificationSettings(stepX * (double)stepZ, 8d * stepX * stepZ, 8d * stepX * stepZ));
            Assert.IsLessThanOrEqualTo(1e-9 + 1e-6 * discharge.Balance.ReferenceFlowModelVolumePerYear,
                Math.Abs(discharge.Balance.ResidualModelVolumePerYear));
            double removed = Enumerable.Range(0, count).Sum(i => (initialHeight[i] - height[i]) * stepX * stepZ);
            Assert.AreEqual(removed, exportedSediment, 1e-6 + 1e-10 * removed);
        }
        if (reworked)
        {
            foreach (RoutedCell cell in topology.Cells.Where(cell => cell.ReceiverId is not null))
            {
                int id = (int)cell.Id, receiver = (int)cell.ReceiverId!.Value;
                double chosen = Slope(id, receiver);
                double best = Neighbours(id, true).Max(other => Slope(id, (int)other));
                if (best > 0d) Assert.AreEqual(best, chosen, 1e-12, "Metric drainage must select an actual steepest neighbour.");
            }
        }
        double Slope(int from, int to)
        {
            double dx = (from % Side - to % Side) * (double)stepX;
            double dz = (from / Side - to / Side) * (double)stepZ;
            return (topology.Cells[from].RoutingElevation - topology.Cells[to].RoutingElevation) / Math.Sqrt(dx * dx + dz * dz);
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
        string output = Path.Combine(parent, (reworked ? "balanced-rework-seed-" : "balanced-seed-") + seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (Directory.Exists(output)) throw new IOException("Diagnostic output already exists; previous evidence is not overwritten.");
        Directory.CreateDirectory(output);
        WriteNew(Path.Combine(output, "fields.json"), raw);
        byte[] beforeIncision = JsonSerializer.SerializeToUtf8Bytes(initialHeight);
        if (reworked) WriteNew(Path.Combine(output, "tectonic-initial-height.json"), beforeIncision);
        string? windowHash = null;
        if (tectonic is not null)
        {
            int maximum = Enumerable.Range(0, count).MaxBy(i => height[i]);
            long minX = Math.Clamp(positions[maximum].X - 8_192, 0, profile.WidthBlocks - 16_384);
            long minZ = Math.Clamp(positions[maximum].Z - 8_192, 0, profile.LengthBlocks - 16_384);
            double[] oldWindow = new double[count], newWindow = new double[count];
            for (int z = 0; z < Side; z++) for (int x = 0; x < Side; x++)
            {
                long px = minX + x * 64 + 32, pz = minZ + z * 64 + 32;
                TectonicReliefSample sampled = tectonic.Sample(px, pz);
                oldWindow[z * Side + x] = sampled.Foundation.AltitudeBlocks;
                newWindow[z * Side + x] = sampled.AltitudeBlocks;
            }
            byte[] window = JsonSerializer.SerializeToUtf8Bytes(new { width = Side, height = Side, step = 64,
                minX, minZ, seaLevel = sea, before = oldWindow, after = newWindow,
                selection = "16,384-block window centred on the final global maximum; both samplers BEFORE the raster incision stage; no interpolation" });
            windowHash = Hash(window);
            WriteNew(Path.Combine(output, "mountain-window.json"), window);
        }
        string commit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "WORKING_TREE_UNVERIFIED";
        byte[] metadata = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, scope = "CORE_SPATIAL_DIAGNOSTIC_NOT_FINAL_WORLD", status = "PASS",
            test = reworked ? nameof(ExportPairedGeographicRework) : nameof(ExportActualCoreReliefRegionsClimateAndDrainageFields), commit, seed,
            profile = profile.Id, width = Side, height = Side, stepX, stepZ,
            extent = new { minX = 0, minZ = 0, maxXExclusive = profile.WidthBlocks, maxZExclusive = profile.LengthBlocks },
            sampling = "cell centres; row-major; X right, positive Z down; no interpolation",
            seaLevel = sea, worldHeight = profile.HeightBlocks,
            fieldsSha256 = sourceHash, coreAssemblySha256 = Hash(File.ReadAllBytes(typeof(LandscapeModel).Assembly.Location)),
            atlasChecksum = model.AtlasContentChecksum.ToString(), landscapeChecksum = tectonic?.ContentChecksum.ToString() ?? model.ContentChecksum.ToString(),
            foundationalLandscapeChecksum = model.ContentChecksum.ToString(),
            geographicRevision = reworked ? "L03-L05-geometric-rework" : "legacy-reference",
            algorithms = new { relief = reworked ? TectonicReliefModel.AlgorithmId : "legacy-landscape-v7",
                precipitation = reworked ? DistancePrecipitationSolver.AlgorithmId : "legacy-precipitation-v1",
                drainage = reworked ? MetricDrainageBuilder.AlgorithmId : "legacy-priority-flood-parent",
                incision = reworked ? DrainageReliefCoupling.AlgorithmId : "none" },
            tectonicInitialHeightSha256 = reworked ? Hash(beforeIncision) : null,
            incision = new { steps = reworked ? incisionSteps : 0, settings = incisionSettings, exportedSediment,
                sedimentPolicy = "detachment only; removed sediment is exported and counted, not deposited; full L06 NOT qualified",
                climatePolicy = "initial climate during 12 bounded steps; final climate and discharge recalculated on final relief" },
            mountainWindowSha256 = windowHash,
            tectonicBelts = tectonic?.Belts,
            atmosphere = (rain as DistancePrecipitationSnapshot)?.Balance,
            annualWinds = reworked ? annualWinds : [new WeightedWind(1, 0, 1)],
            distanceTransport = reworked ? distanceSettings : null,
            familyMeaning = "Foundational L03-B allocation; added continuous tectonic belts do not rewrite the Voronoi family catalogue",
            nativeGame = "NOT_RUN", finalErosion = "FULL_L06_NOT_QUALIFIED; bounded detachment-only coupling on reworked raster",
            nativeStrataSoilsOresVegetationSnow = "NOT_REPRESENTED",
            units = new { height = "blocks", temperature = "model Celsius", rain = "L/Ymod", runoff = "L/Ymod",
                recharge = "L/Ymod", discharge = "L^3/Ymod", drainage_area = "L^2", soil_moisture = "fraction, not fertility" },
            fixture = new { legacyAtmosphericParametersApplyOnlyToReference = true, upstreamProfile = "existing L03B analytical constraints, not a live native audit",
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
            oracle = new { finiteHeight = true, repeatedSamples = true, physicalReliefUnmodifiedByRouting = true,
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
