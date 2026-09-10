#if L00C_STANDALONE_ORACLE
#nullable enable
using System;
using ISRWorldGen.WorldgenProbe;

namespace ISRWorldGen.L00C.Laboratory;

internal static class L00CLifecycleRegistrationLedgerOracle
{
    internal static int Run()
    {
        var ledger=new L00CLifecycleRegistrationLedger();
        foreach(L00CLifecycleRegistrationKind kind in (L00CLifecycleRegistrationKind[])Enum.GetValues(typeof(L00CLifecycleRegistrationKind)))ledger.RecordRegistered(kind);
        Refuse(()=>ledger.RecordRegistered(L00CLifecycleRegistrationKind.Tick));
        ledger.RecordIgnoredCallback(L00CLifecycleRegistrationKind.Tick,"duplicate callback ignored before close");
        ledger.BeginClosing();
        ledger.RecordIgnoredCallback(L00CLifecycleRegistrationKind.GameWorldSave,"event during closing ignored");
        ledger.RecordReleased(L00CLifecycleRegistrationKind.InitWorldGenerator,false);
        ledger.RecordReleased(L00CLifecycleRegistrationKind.GameWorldSave,true);
        ledger.RecordReleased(L00CLifecycleRegistrationKind.Tick,true);
        L00CLifecycleRegistrationSnapshot complete=ledger.Snapshot();
        if(!complete.Complete||complete.States.Count!=3)throw new InvalidOperationException("L00-C exact registration release proof was incomplete.");
        foreach(string diagnostic in complete.Trace)if(!diagnostic.Contains("run=")||!diagnostic.Contains("iteration=")||!diagnostic.Contains("session=")||!diagnostic.Contains("state=")||!diagnostic.Contains("invariant="))throw new InvalidOperationException("L00-C registration diagnostic omitted identity/state/invariant.");

        var missing=new L00CLifecycleRegistrationLedger();
        foreach(L00CLifecycleRegistrationKind kind in (L00CLifecycleRegistrationKind[])Enum.GetValues(typeof(L00CLifecycleRegistrationKind)))missing.RecordRegistered(kind);
        missing.BeginClosing();missing.RecordReleased(L00CLifecycleRegistrationKind.InitWorldGenerator,false);missing.RecordReleased(L00CLifecycleRegistrationKind.GameWorldSave,false);missing.RecordReleased(L00CLifecycleRegistrationKind.Tick,true);
        if(missing.Snapshot().Complete)throw new InvalidOperationException("L00-C missing GameWorldSave unregistration was accepted.");
        return 0;
    }
    private static void Refuse(Action action){try{action();}catch(InvalidOperationException){return;}throw new InvalidOperationException("L00-C registration oracle expected refusal.");}
}
#endif
