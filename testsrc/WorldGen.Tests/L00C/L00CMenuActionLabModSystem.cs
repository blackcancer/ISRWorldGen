// This assembly is intentionally outside the product solution and package.
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ISRWorldGen.L00C.Laboratory;

/// <summary>
/// Debug-only L00-C client harness linked into the existing ISRWorldGen mod.
/// It is inert unless the explicit laboratory switch is enabled.
/// </summary>
public sealed class L00CMenuActionLabModSystem : ModSystem
{
    // This token never exposes an API. It is only a cancellation capability for
    // the short bootstrap listener and becomes inert before controller handoff.
    private L00CManagerLeaseToken? pendingLease;
    private ICoreClientAPI? levelFinalizeApi;

    /// <inheritdoc />
    public override void StartClientSide(ICoreClientAPI api)
    {
#if !DEBUG
        return;
#else
        // This must remain the first observable behaviour.  Ordinary Debug
        // sessions load the same production package but never touch a root,
        // reflection lock, debugger state, save, profile, or session.
        if (!string.Equals(Environment.GetEnvironmentVariable("ISR_L00C_LAB"), "1", StringComparison.Ordinal)) return;
        string root = RequireLaboratoryRoot();
        api.Event.LevelFinalize += OnLevelFinalize;
        levelFinalizeApi = api;
        pendingLease = L00CProcessCampaignController.InstallOrSignal(api, root);
        Mod.Logger.Notification("L00C_INPROCESS_HARNESS_READY: process-lifetime ScreenManager pump installed or signalled.");
#endif
    }

    /// <inheritdoc />
    public override void Dispose()
    {
#if DEBUG
        ICoreClientAPI? subscribed = levelFinalizeApi;
        levelFinalizeApi = null;
        if (subscribed is not null) subscribed.Event.LevelFinalize -= OnLevelFinalize;
        L00CManagerLeaseToken? retained = pendingLease;
        pendingLease = null;
        retained?.Cancel();
#endif
        base.Dispose();
    }

    private void OnLevelFinalize() => L00CProcessCampaignController.SignalLevelFinalize();

    private static string RequireLaboratoryRoot()
    {
        if (!Debugger.IsAttached || !string.Equals(Environment.GetEnvironmentVariable("ISR_L00C_LAB"), "1", StringComparison.Ordinal))
            throw new InvalidOperationException("L00-C in-process harness requires Debugger.IsAttached and ISR_L00C_LAB=1.");
        string? root = Environment.GetEnvironmentVariable("ISR_L00C_LAB_ROOT");
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("L00-C laboratory mod requires ISR_L00C_LAB_ROOT; it never chooses a profile or reads a session.");
        return root;
    }
}
