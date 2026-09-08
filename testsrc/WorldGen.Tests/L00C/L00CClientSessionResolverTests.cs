// Deterministic unit oracle for the audited L00-C session resolver.  It uses
// only local surrogate types: no Vintage Story process or game assembly loads.
#nullable enable
using System;
using System.Reflection;

namespace ISRWorldGen.L00C.Laboratory;

internal static class L00CClientSessionResolverTests
{
    private static int Rid(FieldInfo field) => field.MetadataToken & 0x00ffffff;

    public static int Main()
    {
        ScreenManager manager = new ScreenManager();
        ClientCoreApi core = new ClientCoreApi(new ClientMain(new GuiScreenRunningGame(manager)));
        if (!L00CMenuActionDriver.TryResolveClientSessionChain(core, typeof(ClientCoreApi), typeof(ClientMain),
            typeof(GuiScreenRunningGame), typeof(GuiScreen), typeof(ScreenManager),
            Rid(typeof(ClientCoreApi).GetField("game", BindingFlags.Instance | BindingFlags.NonPublic)!),
            Rid(typeof(ClientMain).GetField("ScreenRunningGame", BindingFlags.Instance | BindingFlags.Public)!),
            Rid(typeof(GuiScreen).GetField("ScreenManager", BindingFlags.Instance | BindingFlags.Public)!),
            out object? clientMain, out object? resolvedManager) || !ReferenceEquals(clientMain, core.Main) || !ReferenceEquals(resolvedManager, manager))
            return 1;

        // A wrong root type and a null link are ordinary not-ready states; no graph search is permitted.
        if (L00CMenuActionDriver.TryResolveClientSessionChain(new object(), typeof(ClientCoreApi), typeof(ClientMain),
            typeof(GuiScreenRunningGame), typeof(GuiScreen), typeof(ScreenManager), 1, 1, 1, out _, out _)) return 2;
        ClientCoreApi noMain = new ClientCoreApi(null!);
        if (L00CMenuActionDriver.TryResolveClientSessionChain(noMain, typeof(ClientCoreApi), typeof(ClientMain),
            typeof(GuiScreenRunningGame), typeof(GuiScreen), typeof(ScreenManager),
            Rid(typeof(ClientCoreApi).GetField("game", BindingFlags.Instance | BindingFlags.NonPublic)!),
            Rid(typeof(ClientMain).GetField("ScreenRunningGame", BindingFlags.Instance | BindingFlags.Public)!),
            Rid(typeof(GuiScreen).GetField("ScreenManager", BindingFlags.Instance | BindingFlags.Public)!), out _, out _)) return 3;

        // Wrong metadata, visibility/staticness, and declared field type are update drift, not fallbacks.
        ExpectRefusal(() => L00CMenuActionDriver.TryResolveClientSessionChain(core, typeof(ClientCoreApi), typeof(ClientMain),
            typeof(GuiScreenRunningGame), typeof(GuiScreen), typeof(ScreenManager), 0x7fffff,
            Rid(typeof(ClientMain).GetField("ScreenRunningGame", BindingFlags.Instance | BindingFlags.Public)!),
            Rid(typeof(GuiScreen).GetField("ScreenManager", BindingFlags.Instance | BindingFlags.Public)!), out _, out _));
        ExpectRefusal(() => L00CMenuActionDriver.TryResolveClientSessionChain(new PublicCoreApi(core.Main), typeof(PublicCoreApi), typeof(ClientMain),
            typeof(GuiScreenRunningGame), typeof(GuiScreen), typeof(ScreenManager),
            Rid(typeof(PublicCoreApi).GetField("game", BindingFlags.Instance | BindingFlags.Public)!),
            Rid(typeof(ClientMain).GetField("ScreenRunningGame", BindingFlags.Instance | BindingFlags.Public)!),
            Rid(typeof(GuiScreen).GetField("ScreenManager", BindingFlags.Instance | BindingFlags.Public)!), out _, out _));
        ExpectRefusal(() => L00CMenuActionDriver.TryResolveClientSessionChain(new InternalCoreApi(core.Main), typeof(InternalCoreApi), typeof(ClientMain),
            typeof(GuiScreenRunningGame), typeof(GuiScreen), typeof(ScreenManager),
            Rid(typeof(InternalCoreApi).GetField("game", BindingFlags.Instance | BindingFlags.NonPublic)!),
            Rid(typeof(ClientMain).GetField("ScreenRunningGame", BindingFlags.Instance | BindingFlags.Public)!),
            Rid(typeof(GuiScreen).GetField("ScreenManager", BindingFlags.Instance | BindingFlags.Public)!), out _, out _));
        ExpectRefusal(() => L00CMenuActionDriver.TryResolveClientSessionChain(new ProtectedCoreApi(core.Main), typeof(ProtectedCoreApi), typeof(ClientMain),
            typeof(GuiScreenRunningGame), typeof(GuiScreen), typeof(ScreenManager),
            Rid(typeof(ProtectedCoreApi).GetField("game", BindingFlags.Instance | BindingFlags.NonPublic)!),
            Rid(typeof(ClientMain).GetField("ScreenRunningGame", BindingFlags.Instance | BindingFlags.Public)!),
            Rid(typeof(GuiScreen).GetField("ScreenManager", BindingFlags.Instance | BindingFlags.Public)!), out _, out _));
        ExpectRefusal(() => L00CMenuActionDriver.TryResolveClientSessionChain(new InternalRunningCoreApi(new InternalRunningClientMain(new GuiScreenRunningGame(manager))), typeof(InternalRunningCoreApi), typeof(InternalRunningClientMain),
            typeof(GuiScreenRunningGame), typeof(GuiScreen), typeof(ScreenManager),
            Rid(typeof(InternalRunningCoreApi).GetField("game", BindingFlags.Instance | BindingFlags.NonPublic)!),
            Rid(typeof(InternalRunningClientMain).GetField("ScreenRunningGame", BindingFlags.Instance | BindingFlags.NonPublic)!),
            Rid(typeof(GuiScreen).GetField("ScreenManager", BindingFlags.Instance | BindingFlags.Public)!), out _, out _));
        ExpectRefusal(() => L00CMenuActionDriver.TryResolveClientSessionChain(new ProtectedRunningCoreApi(new ProtectedRunningClientMain(new GuiScreenRunningGame(manager))), typeof(ProtectedRunningCoreApi), typeof(ProtectedRunningClientMain),
            typeof(GuiScreenRunningGame), typeof(GuiScreen), typeof(ScreenManager),
            Rid(typeof(ProtectedRunningCoreApi).GetField("game", BindingFlags.Instance | BindingFlags.NonPublic)!),
            Rid(typeof(ProtectedRunningClientMain).GetField("ScreenRunningGame", BindingFlags.Instance | BindingFlags.NonPublic)!),
            Rid(typeof(GuiScreen).GetField("ScreenManager", BindingFlags.Instance | BindingFlags.Public)!), out _, out _));
        ExpectRefusal(() => L00CMenuActionDriver.TryResolveClientSessionChain(new InternalManagerCoreApi(new ClientMainWithInternalManager(new RunningWithInternalManager(manager))), typeof(InternalManagerCoreApi), typeof(ClientMainWithInternalManager),
            typeof(RunningWithInternalManager), typeof(InternalGuiScreen), typeof(ScreenManager),
            Rid(typeof(InternalManagerCoreApi).GetField("game", BindingFlags.Instance | BindingFlags.NonPublic)!),
            Rid(typeof(ClientMainWithInternalManager).GetField("ScreenRunningGame", BindingFlags.Instance | BindingFlags.Public)!),
            Rid(typeof(InternalGuiScreen).GetField("ScreenManager", BindingFlags.Instance | BindingFlags.NonPublic)!), out _, out _));
        ExpectRefusal(() => L00CMenuActionDriver.TryResolveClientSessionChain(new ProtectedManagerCoreApi(new ClientMainWithProtectedManager(new RunningWithProtectedManager(manager))), typeof(ProtectedManagerCoreApi), typeof(ClientMainWithProtectedManager),
            typeof(RunningWithProtectedManager), typeof(ProtectedGuiScreen), typeof(ScreenManager),
            Rid(typeof(ProtectedManagerCoreApi).GetField("game", BindingFlags.Instance | BindingFlags.NonPublic)!),
            Rid(typeof(ClientMainWithProtectedManager).GetField("ScreenRunningGame", BindingFlags.Instance | BindingFlags.Public)!),
            Rid(typeof(ProtectedGuiScreen).GetField("ScreenManager", BindingFlags.Instance | BindingFlags.NonPublic)!), out _, out _));
        ExpectRefusal(() => L00CMenuActionDriver.TryResolveClientSessionChain(new StaticCoreApi(), typeof(StaticCoreApi), typeof(ClientMain),
            typeof(GuiScreenRunningGame), typeof(GuiScreen), typeof(ScreenManager),
            Rid(typeof(StaticCoreApi).GetField("game", BindingFlags.Static | BindingFlags.NonPublic)!),
            Rid(typeof(ClientMain).GetField("ScreenRunningGame", BindingFlags.Instance | BindingFlags.Public)!),
            Rid(typeof(GuiScreen).GetField("ScreenManager", BindingFlags.Instance | BindingFlags.Public)!), out _, out _));
        ExpectRefusal(() => L00CMenuActionDriver.TryResolveClientSessionChain(new WrongCoreApi(new WrongClientMain()), typeof(WrongCoreApi), typeof(WrongClientMain),
            typeof(GuiScreenRunningGame), typeof(GuiScreen), typeof(ScreenManager),
            Rid(typeof(WrongCoreApi).GetField("game", BindingFlags.Instance | BindingFlags.NonPublic)!),
            Rid(typeof(WrongClientMain).GetField("ScreenRunningGame", BindingFlags.Instance | BindingFlags.Public)!),
            Rid(typeof(GuiScreen).GetField("ScreenManager", BindingFlags.Instance | BindingFlags.Public)!), out _, out _));
        return 0;
    }

    private static void ExpectRefusal(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Resolver accepted reflection drift.");
    }

    private sealed class ScreenManager { }
    private class GuiScreen { public ScreenManager ScreenManager; public GuiScreen(ScreenManager manager) { ScreenManager = manager; } }
    private sealed class GuiScreenRunningGame : GuiScreen { public GuiScreenRunningGame(ScreenManager manager) : base(manager) { } }
    private sealed class ClientMain { public GuiScreenRunningGame ScreenRunningGame; public ClientMain(GuiScreenRunningGame running) { ScreenRunningGame = running; } }
    private sealed class ClientCoreApi { private ClientMain game; public ClientMain Main => game; public ClientCoreApi(ClientMain game) { this.game = game; } }
    private sealed class PublicCoreApi { public ClientMain game; public PublicCoreApi(ClientMain main) { game = main; } }
    private sealed class InternalCoreApi { internal ClientMain game; public InternalCoreApi(ClientMain main) { game = main; } }
    private class ProtectedCoreApi { protected ClientMain game; public ProtectedCoreApi(ClientMain main) { game = main; } }
    private sealed class InternalRunningClientMain { internal GuiScreenRunningGame ScreenRunningGame; public InternalRunningClientMain(GuiScreenRunningGame running) { ScreenRunningGame = running; } }
    private sealed class InternalRunningCoreApi { private InternalRunningClientMain game; public InternalRunningCoreApi(InternalRunningClientMain main) { game = main; } }
    private class ProtectedRunningClientMain { protected GuiScreenRunningGame ScreenRunningGame; public ProtectedRunningClientMain(GuiScreenRunningGame running) { ScreenRunningGame = running; } }
    private sealed class ProtectedRunningCoreApi { private ProtectedRunningClientMain game; public ProtectedRunningCoreApi(ProtectedRunningClientMain main) { game = main; } }
    private class InternalGuiScreen { internal ScreenManager ScreenManager; public InternalGuiScreen(ScreenManager manager) { ScreenManager = manager; } }
    private sealed class RunningWithInternalManager : InternalGuiScreen { public RunningWithInternalManager(ScreenManager manager) : base(manager) { } }
    private sealed class ClientMainWithInternalManager { public RunningWithInternalManager ScreenRunningGame; public ClientMainWithInternalManager(RunningWithInternalManager running) { ScreenRunningGame = running; } }
    private sealed class InternalManagerCoreApi { private ClientMainWithInternalManager game; public InternalManagerCoreApi(ClientMainWithInternalManager main) { game = main; } }
    private class ProtectedGuiScreen { protected ScreenManager ScreenManager; public ProtectedGuiScreen(ScreenManager manager) { ScreenManager = manager; } }
    private sealed class RunningWithProtectedManager : ProtectedGuiScreen { public RunningWithProtectedManager(ScreenManager manager) : base(manager) { } }
    private sealed class ClientMainWithProtectedManager { public RunningWithProtectedManager ScreenRunningGame; public ClientMainWithProtectedManager(RunningWithProtectedManager running) { ScreenRunningGame = running; } }
    private sealed class ProtectedManagerCoreApi { private ClientMainWithProtectedManager game; public ProtectedManagerCoreApi(ClientMainWithProtectedManager main) { game = main; } }
    private sealed class StaticCoreApi { private static ClientMain? game; }
    private sealed class WrongClientMain { public object ScreenRunningGame = new object(); }
    private sealed class WrongCoreApi { private WrongClientMain game; public WrongCoreApi(WrongClientMain main) { game = main; } }
}
