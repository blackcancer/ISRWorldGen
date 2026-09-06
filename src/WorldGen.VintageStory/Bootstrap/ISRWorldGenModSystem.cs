using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ISRWorldGen;

/// <summary>
/// Minimal lifecycle entry point retained from the installed Vintage Story mod template.
/// </summary>
public sealed class ISRWorldGenModSystem : ModSystem
{
    private static int nextInstanceId;
    private readonly int instanceId = Interlocked.Increment(ref nextInstanceId);

    /// <inheritdoc />
    public override void Start(ICoreAPI api)
    {
        Assembly assembly = typeof(ISRWorldGenModSystem).Assembly;
        string assemblyPath = assembly.Location;
        string assemblyHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath)));

        Mod.Logger.Notification(
            "L00A_BOOTSTRAP modid=isrworldgen instance={0} pid={1} side={2} assembly={3} sha256={4} runtime={5} architecture={6}",
            instanceId,
            Environment.ProcessId,
            api.Side,
            Path.GetFileName(assemblyPath),
            assemblyHash,
            RuntimeInformation.FrameworkDescription.Replace(' ', '_'),
            RuntimeInformation.ProcessArchitecture);
    }

    /// <inheritdoc />
    public override void StartServerSide(ICoreServerAPI api)
    {
        Mod.Logger.Notification("L00A_SERVER_READY modid=isrworldgen instance={0}", instanceId);
    }

    /// <inheritdoc />
    public override void StartClientSide(ICoreClientAPI api)
    {
        Mod.Logger.Notification("L00A_CLIENT_READY modid=isrworldgen instance={0}", instanceId);
    }
}
