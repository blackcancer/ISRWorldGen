using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ISRWorldGen.Core.Geology.Evolution;

public sealed record MaterialMeshNode(int Id, double X, double Z);
public sealed record MaterialMeshFace(int Id, int OriginId, double CrustKm, int A, int B, int C);
/// <summary>Failure supplied by an upstream mechanical history. The date is NOT
/// inferred from yield, a sea mask, or a sampled strain threshold in this module.</summary>
public sealed record MaterialLinkFailure(int NodeA, int NodeB, double TimeMyr, string EvidenceId);
public sealed record FragmentMotionPhase(int EventId, double StartMyr, double EndMyr,
    double FirstX, double FirstZ, double SecondX, double SecondZ);
public sealed record MaterialFragment(int Id, ReadOnlyCollection<int> FaceIds, double AreaReference2);
public sealed record MaterialCutEdge(int A, int B, int FirstFace, int SecondFace, string EvidenceId);
public sealed record OriginVolumeReceipt(int OriginId, double InitialKm3, double ProjectedKm3);
public sealed record MaterialOpeningProjection(MaterialCutSnapshot Topology, RiftMaterialRaster Raster,
    ReadOnlyCollection<ResolvedMaterialTriangle> Sources, ReadOnlyCollection<OriginVolumeReceipt> Volumes,
    double ExpectedOceanAreaKm2, double ExpectedOceanAgeMomentKm3Myr, string Scope);

/// <summary>A graph snapshot is NOT an open rift: material components can have
/// identical velocities. Incidence, not visual proximity, determines connectivity.</summary>
public sealed class MaterialCutSnapshot
{
    public string MeshChecksum { get; }
    public double TimeMyr { get; }
    public double? FirstSeparationTimeMyr { get; }
    public ReadOnlyCollection<MaterialFragment> Fragments { get; }
    public ReadOnlyCollection<MaterialCutEdge> SeparatingEdges { get; }
    public int InternalFailedLinks { get; }
    public string Checksum { get; }
    internal readonly int[] Component;
    internal MaterialCutSnapshot(string mesh, double time, double? first, MaterialFragment[] fragments,
        MaterialCutEdge[] separating, int internalFailed, int[] component)
    {
        MeshChecksum = mesh; TimeMyr = time; FirstSeparationTimeMyr = first;
        Fragments = Array.AsReadOnly(fragments); SeparatingEdges = Array.AsReadOnly(separating);
        InternalFailedLinks = internalFailed; Component = component;
        Checksum = MaterialCutTopology.Hash(new { mesh, time, first, fragments, separating, internalFailed });
    }
}

/// <summary>
/// Resolve connectivity of a finite unwrapped triangulated material domain after
/// dated link failures. Then project a TWO-fragment translational opening through
/// the existing conservative cut-cell projector and chronological inversion.
/// Curved, multi-segment seams are supported; merely internal cracks do not split
/// a domain. This is not a law that creates failure events or solves fracture-tip
/// propagation. Ductile/plastic flow, pressure, toughness and post-cut force balance
/// must supply those events/motions upstream. Unsupported branches, enclosed cuts,
/// closure or overlapping material are refused rather than inventing subduction.
/// No continental triangle is removed, thinned, or replaced by new ocean.
/// </summary>
public sealed class MaterialCutTopology
{
    public const string AlgorithmId = "dated-material-cut-connectivity-and-conjugate-opening-v1";
    public ReadOnlyCollection<MaterialMeshNode> Nodes { get; }
    public ReadOnlyCollection<MaterialMeshFace> Faces { get; }
    public ReadOnlyCollection<MaterialLinkFailure> Failures { get; }
    public string Checksum { get; }
    private readonly Dictionary<int, MaterialMeshNode> nodes;
    private readonly MaterialMeshFace[] faces;
    private readonly Edge[] edges;
    private readonly MaterialLinkFailure[] failures;
    private readonly Dictionary<(int, int), MaterialLinkFailure> failed;
    private readonly HashSet<int> boundaryNodes;
    private readonly double[] areas;
    private sealed record Edge(int A, int B, int[] Incident);

    public MaterialCutTopology(IReadOnlyList<MaterialMeshNode> inputNodes,
        IReadOnlyList<MaterialMeshFace> inputFaces, IReadOnlyList<MaterialLinkFailure> inputFailures)
    {
        ArgumentNullException.ThrowIfNull(inputNodes); ArgumentNullException.ThrowIfNull(inputFaces);
        ArgumentNullException.ThrowIfNull(inputFailures);
        if (inputNodes.Count is < 3 or > 4096 || inputFaces.Count is < 1 or > 2048 || inputFailures.Count > 4096)
            throw new ArgumentException("Unsupported finite material topology budget.");
        var ns = inputNodes.OrderBy(n => n?.Id).ToArray();
        if (ns.Any(n => n is null || n.Id < 0 || !double.IsFinite(n.X) || !double.IsFinite(n.Z))
            || ns.Select(n => n.Id).Distinct().Count() != ns.Length
            || ns.Select(n => (n.X, n.Z)).Distinct().Count() != ns.Length)
            throw new ArgumentException("Nonfinite or duplicate material vertex; shared coordinates need shared identity.");
        nodes = ns.ToDictionary(n => n.Id);
        faces = inputFaces.OrderBy(f => f?.Id).ToArray();
        if (faces.Any(f => f is null || f.Id < 0 || f.OriginId < 0 || !double.IsFinite(f.CrustKm) || f.CrustKm <= 0
            || !nodes.ContainsKey(f.A) || !nodes.ContainsKey(f.B) || !nodes.ContainsKey(f.C))
            || faces.Select(f => f.Id).Distinct().Count() != faces.Length)
            throw new ArgumentException("Invalid material faces or origins.");
        var incidence = new Dictionary<(int, int), List<int>>(); areas = new double[faces.Length];
        var used = new HashSet<int>();
        for (int i = 0; i < faces.Length; i++)
        {
            var f = faces[i]; double cross = Cross(nodes[f.A], nodes[f.B], nodes[f.C]);
            if (!double.IsFinite(cross) || cross == 0) throw new ArgumentException("Degenerate material triangle.");
            if (cross < 0) f = f with { B = f.C, C = f.B };
            // Canonical cyclic order: orientation remains CCW, caller order is irrelevant.
            if (f.B < f.A && f.B < f.C) f = f with { A = f.B, B = f.C, C = f.A };
            else if (f.C < f.A && f.C < f.B) f = f with { A = f.C, B = f.A, C = f.B };
            faces[i] = f; areas[i] = Math.Abs(cross) / 2;
            foreach (var (a, b) in Directed(f))
            {
                used.Add(a); used.Add(b); var key = Key(a, b);
                if (!incidence.TryGetValue(key, out var list)) incidence[key] = list = new();
                list.Add(i); if (list.Count > 2) throw new ArgumentException("Nonmanifold material edge.");
            }
        }
        if (used.Count != ns.Length) throw new ArgumentException("Unused material node.");
        edges = incidence.OrderBy(x => x.Key.Item1).ThenBy(x => x.Key.Item2)
            .Select(x => new Edge(x.Key.Item1, x.Key.Item2, x.Value.ToArray())).ToArray();
        boundaryNodes = new();
        foreach (var e in edges)
        {
            if (e.Incident.Length == 1) { boundaryNodes.Add(e.A); boundaryNodes.Add(e.B); }
            else if (Directed(faces[e.Incident[0]]).Contains((e.A, e.B))
                == Directed(faces[e.Incident[1]]).Contains((e.A, e.B)))
                throw new ArgumentException("Adjacent faces occupy the same side of a material edge.");
        }
        failures = inputFailures.OrderBy(f => f?.TimeMyr).ThenBy(f => f is null ? -1 : Math.Min(f.NodeA, f.NodeB))
            .ThenBy(f => f is null ? -1 : Math.Max(f.NodeA, f.NodeB)).ToArray();
        failed = new(); var evidence = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in failures)
        {
            if (f is null || !double.IsFinite(f.TimeMyr) || f.TimeMyr < 0 || string.IsNullOrWhiteSpace(f.EvidenceId)
                || f.EvidenceId.Length > 256 || !evidence.Add(f.EvidenceId)
                || !incidence.TryGetValue(Key(f.NodeA, f.NodeB), out var incident) || incident.Count != 2
                || !failed.TryAdd(Key(f.NodeA, f.NodeB), f))
                throw new ArgumentException("Failure needs a unique interior link, finite date and mechanical evidence.");
        }
        if (Components(double.NegativeInfinity).Distinct().Count() != 1)
            throw new ArgumentException("Initial mesh is not one connected material domain.");
        Nodes = Array.AsReadOnly(ns); Faces = Array.AsReadOnly(faces); Failures = Array.AsReadOnly(failures);
        Checksum = Hash(new { AlgorithmId, ns, faces, failures = failures.Select(f =>
            new { a = Math.Min(f.NodeA, f.NodeB), b = Math.Max(f.NodeA, f.NodeB), f.TimeMyr, f.EvidenceId }) });
    }

    public MaterialCutSnapshot At(double timeMyr)
    {
        if (!double.IsFinite(timeMyr) || timeMyr < 0) throw new ArgumentOutOfRangeException(nameof(timeMyr));
        var component = Components(timeMyr);
        double? first = null;
        foreach (double date in failures.Where(f => f.TimeMyr <= timeMyr).Select(f => f.TimeMyr).Distinct())
            if (Components(date).Distinct().Count() > 1) { first = date; break; }
        var fragments = component.Distinct().Order().Select(id => new MaterialFragment(id,
            Array.AsReadOnly(Enumerable.Range(0, faces.Length).Where(i => component[i] == id).Select(i => faces[i].Id).ToArray()),
            CrustTransport.Sum(Enumerable.Range(0, faces.Length).Where(i => component[i] == id).Select(i => areas[i]).ToArray()))).ToArray();
        var separated = new List<MaterialCutEdge>(); int internalFailed = 0;
        foreach (var e in edges)
        {
            if (!failed.TryGetValue((e.A, e.B), out var f) || f.TimeMyr > timeMyr) continue;
            int a = e.Incident[0], b = e.Incident[1];
            if (component[a] == component[b]) { internalFailed++; continue; }
            if (component[a] > component[b]) (a, b) = (b, a);
            // First fragment is to the LEFT of its CCW edge; outward normal is (tz,-tx).
            var edge = Directed(faces[a]).Single(x => Key(x.Item1, x.Item2) == (e.A, e.B));
            separated.Add(new(edge.Item1, edge.Item2, faces[a].Id, faces[b].Id, f.EvidenceId));
        }
        return new(Checksum, timeMyr, first, fragments, separated.ToArray(), internalFailed, component);
    }

    /// <summary>Geometric opening from a resolved two-fragment topology. Motion
    /// phases start AT first disconnection; waiting for unrecorded motion is not
    /// allowed. Pure common translation or tangential slip produces no ocean area.</summary>
    public MaterialOpeningProjection Open(MaterialCutSnapshot cut, IReadOnlyList<FragmentMotionPhase> loading,
        TectonicScalePlan scale, int side, double kmPerReference, double oceanThicknessKm,
        double ridgeFraction = .5, bool periodic = true)
    {
        ArgumentNullException.ThrowIfNull(cut); ArgumentNullException.ThrowIfNull(loading); ArgumentNullException.ThrowIfNull(scale);
        if (cut.MeshChecksum != Checksum || cut.Fragments.Count != 2 || cut.FirstSeparationTimeMyr != cut.TimeMyr
            || loading.Count is < 1 or > 64 || !double.IsFinite(ridgeFraction) || ridgeFraction <= 0 || ridgeFraction >= 1
            || !double.IsFinite(oceanThicknessKm) || oceanThicknessKm <= 0)
            throw new ArgumentException("Opening requires exactly two resolved fragments at their recorded first separation.");
        var phases = loading.ToArray(); var eventIds = new HashSet<int>();
        for (int i = 0; i < phases.Length; i++)
        {
            var p = phases[i];
            if (p is null || p.EventId < 0 || !eventIds.Add(p.EventId)
                || new[] { p.StartMyr, p.EndMyr, p.FirstX, p.FirstZ, p.SecondX, p.SecondZ }.Any(v => !double.IsFinite(v))
                || p.EndMyr <= p.StartMyr || p.StartMyr != (i == 0 ? cut.TimeMyr : phases[i - 1].EndMyr))
                throw new ArgumentException("Incomplete or ambiguous post-cut material motion.");
        }
        // This bounded opening does not incorporate a subsequent new disconnection.
        if (failures.Any(f => f.TimeMyr > cut.TimeMyr && f.TimeMyr <= phases[^1].EndMyr))
            throw new ArgumentException("Later fracture events require a new topology interval.");
        ValidateOpenChain(cut.SeparatingEdges);
        double time = phases[^1].EndMyr;
        string provenance = Hash(new { AlgorithmId, cut.Checksum, phases, kmPerReference, oceanThicknessKm, ridgeFraction });
        var initial = new List<ResolvedMaterialTriangle>();
        for (int i = 0; i < faces.Length; i++) initial.Add(FacePatch(i, 0, 0, 0));
        // Reject spatial intersections/nonconforming geometry before publishing a moved state.
        var before = RiftMaterialRaster.ProjectTriangles(initial, scale, side, cut.TimeMyr, kmPerReference, periodic);
        double firstX = 0, firstZ = 0, secondX = 0, secondZ = 0;
        foreach (var p in phases)
        {
            double dt = p.EndMyr - p.StartMyr;
            firstX += p.FirstX * dt; firstZ += p.FirstZ * dt;
            secondX += p.SecondX * dt; secondZ += p.SecondZ * dt;
        }
        var sources = new List<ResolvedMaterialTriangle>();
        int firstId = cut.Fragments[0].Id;
        for (int i = 0; i < faces.Length; i++)
        {
            bool first = cut.Component[i] == firstId;
            sources.Add(FacePatch(i, first ? firstX : secondX, first ? firstZ : secondZ, first ? -1 : 1));
        }
        double expectedArea = 0, expectedMoment = 0;
        int seamIndex = 0;
        foreach (var edge in cut.SeparatingEdges)
        {
            var a = nodes[edge.A]; var b = nodes[edge.B]; double tx = b.X - a.X, tz = b.Z - a.Z;
            var left = new List<SpreadingPhase>(); var right = new List<SpreadingPhase>();
            double rx = 0, rz = 0;
            foreach (var p in phases)
            {
                double opening = tz * (p.SecondX - p.FirstX) - tx * (p.SecondZ - p.FirstZ);
                if (!double.IsFinite(opening) || opening < 0)
                    throw new ArgumentException("Closing or folded seam; contact mechanics is required, not new basalt.");
                bool active = opening > 0; double dt = p.EndMyr - p.StartMyr;
                double vx = (1 - ridgeFraction) * p.FirstX + ridgeFraction * p.SecondX;
                double vz = (1 - ridgeFraction) * p.FirstZ + ridgeFraction * p.SecondZ;
                left.Add(Make(p.FirstX, p.FirstZ)); right.Add(Make(p.SecondX, p.SecondZ));
                expectedArea += opening * dt * kmPerReference * kmPerReference;
                expectedMoment += opening * dt * kmPerReference * kmPerReference * oceanThicknessKm
                    * (time - (p.StartMyr + p.EndMyr) / 2);
                rx += vx * dt; rz += vz * dt;
                SpreadingPhase Make(double mx, double mz) => new(active ? p.EventId : -1,
                    p.StartMyr - time, p.EndMyr - time, a.X + rx, a.Z + rz, b.X + rx, b.Z + rz,
                    vx, vz, mx, mz, active);
            }
            AddOcean(new SpreadingTimeline("mesh/" + seamIndex + "/left", left), -1);
            AddOcean(new SpreadingTimeline("mesh/" + seamIndex + "/right", right), 1);
            seamIndex++;
        }
        var raster = RiftMaterialRaster.ProjectTriangles(sources, scale, side, time, kmPerReference, periodic);
        var volumes = faces.Select(f => f.OriginId).Distinct().Order().Select(id =>
        {
            double original = CrustTransport.Sum(Enumerable.Range(0, faces.Length).Where(i => faces[i].OriginId == id)
                .Select(i => areas[i] * faces[i].CrustKm * kmPerReference * kmPerReference).ToArray());
            double projected = CrustTransport.Sum(raster.Packets.Where(p => raster.Patches[p.Patch].Material == "continental"
                && raster.Patches[p.Patch].OriginOrEventId == id).Select(p => p.AreaFraction * raster.Patches[p.Patch].ThicknessKm * raster.CellAreaKm2).ToArray());
            Balance(original, projected); return new OriginVolumeReceipt(id, original, projected);
        }).ToArray();
        Balance(CrustTransport.Sum(before.ContinentalKm), CrustTransport.Sum(raster.ContinentalKm));
        Balance(expectedArea, CrustTransport.Sum(raster.OceanFraction) * raster.CellAreaKm2);
        Balance(expectedArea * oceanThicknessKm, CrustTransport.Sum(raster.OceanicKm) * raster.CellAreaKm2);
        Balance(expectedMoment, CrustTransport.Sum(raster.OceanAgeMomentKmMyr) * raster.CellAreaKm2);
        return new(cut, raster, sources.AsReadOnly(), Array.AsReadOnly(volumes), expectedArea, expectedMoment,
            "DATED_INPUT_CUTS_AND_PRESCRIBED_TRANSLATIONS_NOT_SPONTANEOUS_WORLD_FRACTURE");

        ResolvedMaterialTriangle FacePatch(int i, double sx, double sz, int flank)
        {
            var f = faces[i]; RiftSourceVertex V(int id) => new(nodes[id].X + sx, nodes[id].Z + sz, 0);
            return new("continent/" + f.Id, provenance, "continental", f.OriginId, flank, f.CrustKm, V(f.A), V(f.B), V(f.C));
        }
        void AddOcean(SpreadingTimeline timeline, int flank)
        {
            foreach (var e in timeline.Episodes)
            {
                double vx = e.MaterialVelocityX - e.RidgeVelocityX, vz = e.MaterialVelocityZ - e.RidgeVelocityZ;
                double old = -e.FirstBirthTimeMyr, young = -e.LastBirthTimeMyr;
                RiftSourceVertex V(double x, double z, double age) => new(x + vx * age, z + vz * age, age);
                var v = new[] { V(e.Ax, e.Az, old), V(e.Bx, e.Bz, old), V(e.Bx, e.Bz, young), V(e.Ax, e.Az, young) };
                string id = FormattableString.Invariant($"ocean/{seamIndex}/{flank}/{e.SourceEventId}");
                sources.Add(new(id + "/0", provenance, "new-ocean", e.SourceEventId, flank, oceanThicknessKm, v[0], v[1], v[2]));
                sources.Add(new(id + "/1", provenance, "new-ocean", e.SourceEventId, flank, oceanThicknessKm, v[0], v[2], v[3]));
            }
        }
    }

    private void ValidateOpenChain(IReadOnlyList<MaterialCutEdge> seam)
    {
        var links = new Dictionary<int, List<int>>();
        foreach (var e in seam)
        {
            if (!links.ContainsKey(e.A)) links[e.A] = new(); if (!links.ContainsKey(e.B)) links[e.B] = new();
            links[e.A].Add(e.B); links[e.B].Add(e.A);
        }
        var ends = links.Where(x => x.Value.Count == 1).Select(x => x.Key).Order().ToArray();
        if (ends.Length != 2 || links.Any(x => x.Value.Count > 2) || ends.Any(x => !boundaryNodes.Contains(x)))
            throw new ArgumentException("Opening supports one boundary-to-boundary seam, not an enclosed or branched cut.");
        var seen = new HashSet<int>(); var queue = new Queue<int>(); queue.Enqueue(ends[0]);
        while (queue.TryDequeue(out int v)) if (seen.Add(v)) foreach (int next in links[v]) queue.Enqueue(next);
        if (seen.Count != links.Count) throw new ArgumentException("Disconnected separating seams require explicit topology.");
    }
    private int[] Components(double time)
    {
        var parent = Enumerable.Range(0, faces.Length).ToArray();
        int Root(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
        foreach (var e in edges)
        {
            if (e.Incident.Length != 2 || (failed.TryGetValue((e.A, e.B), out var f) && f.TimeMyr <= time)) continue;
            int a = Root(e.Incident[0]), b = Root(e.Incident[1]); if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
        }
        return Enumerable.Range(0, faces.Length).Select(i => faces[Root(i)].Id).ToArray();
    }
    private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);
    private static (int, int)[] Directed(MaterialMeshFace f) => [(f.A, f.B), (f.B, f.C), (f.C, f.A)];
    private static double Cross(MaterialMeshNode a, MaterialMeshNode b, MaterialMeshNode c)
        => (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);
    private static void Balance(double a, double b)
    {
        if (!double.IsFinite(a) || !double.IsFinite(b) || Math.Abs(a - b) > 1e-9 * Math.Max(1, Math.Abs(a)))
            throw new ArithmeticException("Material breakup area/volume/age balance failed.");
    }
    internal static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();
}
