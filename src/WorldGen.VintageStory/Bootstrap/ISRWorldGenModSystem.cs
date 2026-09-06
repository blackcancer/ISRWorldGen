using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ISRWorldGen;

/// <summary>
/// Minimal lifecycle entry point retained from the installed Vintage Story mod template.
/// </summary>
public sealed class ISRWorldGenModSystem : ModSystem
{
    /// <inheritdoc />
    public override void Start(ICoreAPI api)
    {
        Mod.Logger.Notification("ISRWorldGen bootstrap started on {0}.", api.Side);
    }

    /// <inheritdoc />
    public override void StartServerSide(ICoreServerAPI api)
    {
        Mod.Logger.Notification("ISRWorldGen server bootstrap started.");
    }

    /// <inheritdoc />
    public override void StartClientSide(ICoreClientAPI api)
    {
        Mod.Logger.Notification("ISRWorldGen client bootstrap started.");
    }
}
