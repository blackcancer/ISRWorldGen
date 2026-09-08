// This assembly is intentionally outside the product solution and package.
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ISRWorldGen.L00C.Laboratory;

public sealed class L00CMenuActionLabModSystem : ModSystem
{
    public override void StartClientSide(ICoreClientAPI api)
    {
#if !DEBUG
        throw new InvalidOperationException("L00-C laboratory mod is disabled outside a Debug build.");
#else
        string root = RequireLaboratoryRoot();
        L00CProcessCampaignController.InstallOrSignal(api, root);
        Mod.Logger.Notification("L00C_MENU_LAB_READY: process-lifetime ScreenManager pump installed or signalled.");
#endif
    }

    private static string RequireLaboratoryRoot()
    {
        if (!Debugger.IsAttached || !string.Equals(Environment.GetEnvironmentVariable("ISR_L00C_LAB"), "1", StringComparison.Ordinal))
            throw new InvalidOperationException("L00-C laboratory mod requires Debugger.IsAttached and ISR_L00C_LAB=1.");
        string? root = Environment.GetEnvironmentVariable("ISR_L00C_LAB_ROOT");
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("L00-C laboratory mod requires ISR_L00C_LAB_ROOT; it never chooses a profile or reads a session.");
        return root;
    }
}
