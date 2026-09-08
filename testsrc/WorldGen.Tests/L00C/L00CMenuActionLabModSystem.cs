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
    private L00CMenuActionLaboratoryHost? host;
    private L00CFixtureBootstrap? bootstrap;
    private long listenerId;
    private ICoreClientAPI? clientApi;

    public override void StartClientSide(ICoreClientAPI api)
    {
#if !DEBUG
        throw new InvalidOperationException("L00-C laboratory mod is disabled outside a Debug build.");
#else
        string root = RequireLaboratoryRoot();
        string evidence = Path.Combine(root, "menu-action-evidence", DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ"));
        bootstrap = new L00CFixtureBootstrap(root, evidence);
        clientApi = api;
        listenerId = api.Event.RegisterGameTickListener(_ => AdvanceClientCycle(api), 100);
        Mod.Logger.Notification("L00C_MENU_LAB_READY: debugger-attached local test mod loaded; awaiting stable menu.");
#endif
    }

    public override void Dispose()
    {
        // The public event lifecycle owns the client callback; no game session is retained here.
        Stop();
        base.Dispose();
    }

    private void AdvanceClientCycle(ICoreClientAPI api)
    {
        if (bootstrap is not null)
        {
            try { if (bootstrap.TryAdvance(api, out L00CMenuActionLaboratoryHost? completed)) { host = completed; bootstrap = null; Mod.Logger.Notification("L00C_MENU_LAB_BOOTSTRAP_COMPLETE: native saves and live cells were confirmed."); } }
            catch (Exception exception) { Mod.Logger.Error("L00C_MENU_LAB_BOOTSTRAP_REFUSED: {0}", exception.Message); Stop(); }
            return;
        }
        if (host is null) return;
        try
        {
            if (host.TryAdvance(api))
            {
                Mod.Logger.Notification("L00C_MENU_LAB_COMPLETE: receipts were written under the marked laboratory root.");
                Stop();
            }
        }
        catch (Exception exception)
        {
            Mod.Logger.Error("L00C_MENU_LAB_REFUSED: {0}", exception.Message);
            Stop();
        }
    }

    private void Stop()
    {
        if (listenerId != 0) clientApi?.Event.UnregisterGameTickListener(listenerId);
        listenerId = 0; host = null; bootstrap = null; clientApi = null;
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
