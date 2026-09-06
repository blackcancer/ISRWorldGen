#if DEBUG
using System.Threading;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ISRWorldGen;

/// <summary>
/// Bounded, server-only probe used to prove that the real Vintage Story process is
/// debuggable through Visual Studio. It observes world generation and never writes
/// blocks, maps, save data, or shared configuration.
/// </summary>
public sealed class L00BDebugProbeModSystem : ModSystem
{
    private int exceptionIssued;

    /// <inheritdoc />
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    /// <inheritdoc />
    public override void StartServerSide(ICoreServerAPI api)
    {
        // T00-03 mutates this local through the debugger. Normal launches never
        // request a probe column and therefore retain vanilla loading behavior.
        bool generateProbeColumn = false;
        int processId = Environment.ProcessId;
        string modulePath = typeof(L00BDebugProbeModSystem).Assembly.Location;

        api.Event.ChunkColumnGeneration(OnChunkColumnGeneration, EnumWorldGenPass.Terrain, "standard");
        Mod.Logger.Notification(
            "L00B_DEBUG_PROBE_READY pid={0} module={1} pass={2} worldtype={3}",
            processId,
            Path.GetFileName(modulePath),
            EnumWorldGenPass.Terrain,
            "standard");

        if (generateProbeColumn)
        {
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => RequestProbeColumn(api));
        }
    }

    private void RequestProbeColumn(ICoreServerAPI api)
    {
        int chunkCountX = api.WorldManager.MapSizeX / api.WorldManager.ChunkSize;
        int chunkCountZ = api.WorldManager.MapSizeZ / api.WorldManager.ChunkSize;
        int probeChunkX = Math.Max(1, chunkCountX - 2);
        int probeChunkZ = Math.Max(1, chunkCountZ - 2);

        var options = new ChunkLoadOptions
        {
            KeepLoaded = false,
            OnLoaded = () => Mod.Logger.Notification(
                "L00B_PROBE_COLUMN_LOADED chunk=({0},{1})",
                probeChunkX,
                probeChunkZ)
        };

        Mod.Logger.Notification(
            "L00B_PROBE_COLUMN_REQUEST chunk=({0},{1})",
            probeChunkX,
            probeChunkZ);
        api.WorldManager.LoadChunkColumnPriority(probeChunkX, probeChunkZ, options);
    }

    private void OnChunkColumnGeneration(IChunkColumnGenerateRequest request)
    {
        int chunkX = request.ChunkX;
        int chunkZ = request.ChunkZ;
        string coordinate = $"({chunkX},{chunkZ})";

        Mod.Logger.Notification("L00B_COLUMN_CALLBACK chunk={0}", coordinate);

        // Deliberately false in every normal run. During T00-03 only, the Visual
        // Studio debugger changes this local to true while paused on the next line.
        bool throwRequested = false;
        if (throwRequested && Interlocked.CompareExchange(ref exceptionIssued, 1, 0) == 0)
        {
            Mod.Logger.Warning("L00B_CONTROLLED_EXCEPTION chunk={0}", coordinate);
            throw new InvalidOperationException($"L00-B controlled debug exception at chunk {coordinate}.");
        }
    }
}
#endif
