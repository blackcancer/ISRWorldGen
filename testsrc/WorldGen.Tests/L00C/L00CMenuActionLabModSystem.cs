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
    private long listenerId;

    public override void StartClientSide(ICoreClientAPI api)
    {
#if !DEBUG
        throw new InvalidOperationException("L00-C laboratory mod is disabled outside a Debug build.");
#else
        string root = RequireLaboratoryRoot();
        string saves = Path.Combine(root, "saves");
        string evidence = Path.Combine(root, "menu-action-evidence", DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ"));
        host = L00CMenuActionLaboratoryHost.Open(
            root,
            evidence,
            Path.Combine(saves, "activated-primary.vcdbs"),
            Path.Combine(saves, "activated-secondary.vcdbs"));
        listenerId = api.Event.RegisterGameTickListener(_ => AdvanceClientCycle(api), 100);
        Mod.Logger.Notification("L00C_MENU_LAB_READY: debugger-attached local test mod loaded; awaiting stable menu.");
#endif
    }

    public override void Dispose()
    {
        // The public event lifecycle owns the client callback; no game session is retained here.
        host = null;
        base.Dispose();
    }

    private void AdvanceClientCycle(ICoreClientAPI api)
    {
        if (host is null) return;
        try
        {
            if (host.TryAdvance(api))
            {
                Mod.Logger.Notification("L00C_MENU_LAB_COMPLETE: receipts were written under the marked laboratory root.");
                host = null;
            }
        }
        catch (Exception exception)
        {
            Mod.Logger.Error("L00C_MENU_LAB_REFUSED: {0}", exception.Message);
            host = null;
        }
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
