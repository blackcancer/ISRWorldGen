#if DEBUG || L00C_STANDALONE_ORACLE
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace ISRWorldGen.WorldgenProbe;

internal enum L00CLifecycleEventKind { Ready, SaveCommitted }

internal sealed class L00CLifecycleSessionObservation
{
    private L00CLifecycleSessionObservation(string runId,int iteration,int sessionOrdinal,string canonicalSavePath,
        string canonicalSavegameGuid,bool isNew,L00CLifecycleEventKind eventKind)
    {
        RunId=runId;Iteration=iteration;SessionOrdinal=sessionOrdinal;CanonicalSavePath=canonicalSavePath;
        CanonicalSavegameGuid=canonicalSavegameGuid;IsNew=isNew;EventKind=eventKind;
    }

    internal string RunId { get; }
    internal int Iteration { get; }
    internal int SessionOrdinal { get; }
    internal string CanonicalSavePath { get; }
    internal string CanonicalSavegameGuid { get; }
    internal bool IsNew { get; }
    internal L00CLifecycleEventKind EventKind { get; }

    internal static L00CLifecycleSessionObservation Ready(string runId,int iteration,int sessionOrdinal,
        string canonicalSavePath,string canonicalSavegameGuid,bool isNew)
        => Create(runId,iteration,sessionOrdinal,canonicalSavePath,canonicalSavegameGuid,isNew,L00CLifecycleEventKind.Ready);
    internal static L00CLifecycleSessionObservation SaveCommitted(string runId,int iteration,int sessionOrdinal,
        string canonicalSavePath,string canonicalSavegameGuid,bool isNew)
        => Create(runId,iteration,sessionOrdinal,canonicalSavePath,canonicalSavegameGuid,isNew,L00CLifecycleEventKind.SaveCommitted);

    private static L00CLifecycleSessionObservation Create(string runId,int iteration,int sessionOrdinal,
        string savePath,string savegameGuid,bool isNew,L00CLifecycleEventKind eventKind)
    {
        RequireRunId(runId);
        if(iteration<1||iteration>5)throw new InvalidOperationException("L00-C lifecycle observation iteration must be within 1..5.");
        string path=NormalizeSavePath(savePath,nameof(savePath));
        string prefix="ISRWorldGen-L00C-"+runId+"-iteration-"+iteration.ToString("D2")+"-";
        string file=Path.GetFileName(path);
        char slot=file==prefix+"a.vcdbs"?'a':file==prefix+"b.vcdbs"?'b':'\0';
        if(slot=='\0')throw new InvalidOperationException("L00-C lifecycle observation path does not bind its run/iteration slot.");
        int expected=checked((iteration-1)*3+(slot=='a'?(isNew?1:2):3));
        if(slot=='b'&&!isNew)throw new InvalidOperationException("L00-C lifecycle B observation cannot be a reopen.");
        if(sessionOrdinal!=expected)throw new InvalidOperationException("L00-C lifecycle observation ordinal does not bind the exact A-create/A-reopen/B-create session.");
        return new L00CLifecycleSessionObservation(runId,iteration,sessionOrdinal,path,
            L00CLifecycleShutdownIdentity.NormalizeGuid(savegameGuid,nameof(savegameGuid)),isNew,eventKind);
    }

    internal string Diagnostic(string state,string invariant)
        => "run="+RunId+" iteration="+Iteration+" session="+SessionOrdinal+" state="+RequireText(state,nameof(state))+" invariant="+RequireText(invariant,nameof(invariant))+" event="+EventKind;
    internal bool SameCapturedSession(L00CLifecycleSessionObservation other)
        => other is not null&&RunId==other.RunId&&Iteration==other.Iteration&&SessionOrdinal==other.SessionOrdinal&&
            string.Equals(CanonicalSavePath,other.CanonicalSavePath,StringComparison.OrdinalIgnoreCase)&&
            CanonicalSavegameGuid==other.CanonicalSavegameGuid&&IsNew==other.IsNew;

    private static void RequireRunId(string value)
    {
        if(value is null||value.Length!=32)throw new InvalidOperationException("L00-C lifecycle run id must be 32 lower-case hexadecimal characters.");
        foreach(char item in value)if(!((item>='0'&&item<='9')||(item>='a'&&item<='f')))throw new InvalidOperationException("L00-C lifecycle run id must be 32 lower-case hexadecimal characters.");
    }
    private static string RequireText(string value,string name)=>string.IsNullOrWhiteSpace(value)?throw new ArgumentException("L00-C lifecycle diagnostic value is required.",name):value.Replace('\r',' ').Replace('\n',' ');
    internal static string NormalizeSavePath(string path,string parameterName)
    {
        if(string.IsNullOrWhiteSpace(path)||!Path.IsPathRooted(path))throw new ArgumentException("L00-C lifecycle save path is required and absolute.",parameterName);
        string full=Path.GetFullPath(path);
        if(!string.Equals(path,full,StringComparison.OrdinalIgnoreCase)||!string.Equals(Path.GetExtension(full),".vcdbs",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("L00-C lifecycle save path must already be canonical and target .vcdbs.");
        return full;
    }
}

internal static class L00CLifecycleShutdownBarrier
{
    private static readonly L00CLifecycleShutdownState State=new();
    internal static L00CLifecycleShutdownLease Open(L00CLifecycleShutdownIdentity identity)=>State.Open(identity);
    internal static bool Close(L00CLifecycleShutdownLease lease)=>State.Close(lease);
    internal static void Arm(L00CLifecycleShutdownLease lease)=>State.Arm(lease);
    internal static void BindReady(L00CLifecycleShutdownLease lease,L00CLifecycleSessionObservation observation)=>State.BindReady(lease,observation);
    internal static L00CLifecycleShutdownIdentity RequireCurrentIdentity()=>State.RequireCurrentIdentity();
    internal static L00CLifecycleReturnReservation PrepareReturn(L00CLifecycleSessionObservation observation,string observedStartServerSavePath)=>State.PrepareReturn(observation,observedStartServerSavePath);
    internal static void BeginNativeReturn(L00CLifecycleReturnReservation reservation)=>State.BeginNativeReturn(reservation);
    internal static L00CLifecycleSessionObservation ConfirmSaveCommitted(L00CLifecycleReturnReservation reservation,L00CLifecycleSessionObservation observation,L00CNativeSaveQuitReturnProof proof)=>State.ConfirmSaveCommitted(reservation,observation,proof);
    internal static bool AbortBeforeNativeReturn(L00CLifecycleReturnReservation reservation)=>State.AbortBeforeNativeReturn(reservation);
}

internal sealed class L00CLifecycleShutdownState
{
    private readonly object gate=new();
    private readonly List<string> trace=new();
    private L00CLifecycleShutdownLease? activeLease;
    private L00CLifecycleSessionObservation? ready;
    private L00CLifecycleReturnReservation? pendingReturn;
    private bool stable;
    private long generation;

    internal IReadOnlyList<string> Trace { get { lock(gate)return new ReadOnlyCollection<string>(new List<string>(trace)); } }

    internal L00CLifecycleShutdownLease Open(L00CLifecycleShutdownIdentity identity)
    {
        if(identity is null)throw new ArgumentNullException(nameof(identity));
        lock(gate)
        {
            if(activeLease is not null||pendingReturn is not null)
            {
                TraceEvent(pendingReturn?.ReadyObservation??ready,"OpenRejected","prior server or native return reservation is still active");
                throw new InvalidOperationException("L00-C lifecycle shutdown barrier already has an active owner or uncommitted native return.");
            }
            var lease=new L00CLifecycleShutdownLease(identity,checked(++generation));activeLease=lease;ready=null;pendingReturn=null;stable=false;
            TraceEvent(null,"ServerOpen","active immutable server identity");return lease;
        }
    }

    internal bool Close(L00CLifecycleShutdownLease lease)
    {
        if(lease is null)throw new ArgumentNullException(nameof(lease));
        lock(gate)
        {
            if(!ReferenceEquals(activeLease,lease)){TraceEvent(ready,"StaleClose","stale owner ignored");return false;}
            L00CLifecycleSessionObservation? captured=pendingReturn?.ReadyObservation??ready;
            lease.Closed=true;lease.ServerReleased=true;activeLease=null;stable=false;ready=null;
            if(pendingReturn is not null&&!pendingReturn.Started){pendingReturn.Cancelled=true;pendingReturn=null;}
            TraceEvent(captured,"ServerClosed","server stopped and owner released");return true;
        }
    }

    internal void Arm(L00CLifecycleShutdownLease lease)
    {
        if(lease is null)throw new ArgumentNullException(nameof(lease));
        lock(gate)
        {
            if(!ReferenceEquals(activeLease,lease)||lease.Closed||pendingReturn is not null)throw new InvalidOperationException("L00-C lifecycle shutdown barrier was armed by a stale or closing owner.");
            stable=true;TraceEvent(ready,"ServerStable","stable lifecycle session");
        }
    }

    internal void BindReady(L00CLifecycleShutdownLease lease,L00CLifecycleSessionObservation observation)
    {
        if(lease is null)throw new ArgumentNullException(nameof(lease));if(observation is null)throw new ArgumentNullException(nameof(observation));
        lock(gate)
        {
            if(observation.EventKind!=L00CLifecycleEventKind.Ready||!ReferenceEquals(activeLease,lease)||lease.Closed||!stable||ready is not null||pendingReturn is not null)throw new InvalidOperationException("L00-C lifecycle Ready callback is stale, duplicate, closing, or premature.");
            if(lease.Identity.SavegameGuid!=observation.CanonicalSavegameGuid)throw new InvalidOperationException("L00-C lifecycle Ready server/client GUID mismatch.");
            ready=observation;TraceEvent(observation,"ReadyBound","captured session bound exactly once");
        }
    }

    internal L00CLifecycleShutdownIdentity RequireCurrentIdentity()
    {
        lock(gate){if(activeLease is null||activeLease.Closed)throw new InvalidOperationException("L00-C lifecycle controller has no active immutable server identity.");return activeLease.Identity;}
    }

    internal L00CLifecycleReturnReservation PrepareReturn(L00CLifecycleSessionObservation observation,string observedStartServerSavePath)
    {
        if(observation is null)throw new ArgumentNullException(nameof(observation));string observed=L00CLifecycleSessionObservation.NormalizeSavePath(observedStartServerSavePath,nameof(observedStartServerSavePath));
        lock(gate)
        {
            if(observation.EventKind!=L00CLifecycleEventKind.Ready||activeLease is null||activeLease.Closed||!stable||!ReferenceEquals(ready,observation)||pendingReturn is not null)throw new InvalidOperationException("L00-C lifecycle return reservation is stale, premature, duplicate, closing, or reattributed.");
            if(!string.Equals(observation.CanonicalSavePath,observed,StringComparison.OrdinalIgnoreCase)||activeLease.Identity.SavegameGuid!=observation.CanonicalSavegameGuid)throw new InvalidOperationException("L00-C lifecycle return path/GUID differs from the captured session.");
            var reservation=new L00CLifecycleReturnReservation(activeLease,observation);pendingReturn=reservation;TraceEvent(observation,"ReturnPrepared","captured session authorized");return reservation;
        }
    }

    internal void BeginNativeReturn(L00CLifecycleReturnReservation reservation)
    {
        if(reservation is null)throw new ArgumentNullException(nameof(reservation));
        lock(gate)
        {
            if(!ReferenceEquals(pendingReturn,reservation)||activeLease is null||!ReferenceEquals(activeLease,reservation.Lease)||activeLease.Closed||reservation.Cancelled||reservation.Started||!ReferenceEquals(ready,reservation.ReadyObservation))throw new InvalidOperationException("L00-C lifecycle native return authorization is stale, closed, duplicate, or mismatched.");
            reservation.Started=true;stable=false;TraceEvent(reservation.ReadyObservation,"NativeReturnStarted","audited Save&Quit chain entered");
        }
    }

    internal L00CLifecycleSessionObservation ConfirmSaveCommitted(L00CLifecycleReturnReservation reservation,L00CLifecycleSessionObservation observation,L00CNativeSaveQuitReturnProof proof)
    {
        if(reservation is null)throw new ArgumentNullException(nameof(reservation));if(observation is null)throw new ArgumentNullException(nameof(observation));if(proof is null)throw new ArgumentNullException(nameof(proof));
        lock(gate)
        {
            if(!ReferenceEquals(pendingReturn,reservation)||!reservation.Started||reservation.Cancelled||reservation.Committed||!reservation.Lease.Closed||!reservation.Lease.ServerReleased)throw new InvalidOperationException("L00-C lifecycle SaveCommitted callback is stale, duplicate, or precedes server release.");
            if(observation.EventKind!=L00CLifecycleEventKind.SaveCommitted||!reservation.ReadyObservation.SameCapturedSession(observation))throw new InvalidOperationException("L00-C lifecycle SaveCommitted callback reattributed the captured session.");
            proof.RequireActualNativeReturn(observation);
            reservation.Committed=true;pendingReturn=null;TraceEvent(observation,"SaveCommitted","native server stop, main menu, and exclusive target proof accepted");return observation;
        }
    }

    internal bool AbortBeforeNativeReturn(L00CLifecycleReturnReservation reservation)
    {
        if(reservation is null)throw new ArgumentNullException(nameof(reservation));
        lock(gate){if(reservation.Started||!ReferenceEquals(pendingReturn,reservation))return false;reservation.Cancelled=true;pendingReturn=null;TraceEvent(reservation.ReadyObservation,"ReturnAborted","no native return effect started");return true;}
    }

    private void TraceEvent(L00CLifecycleSessionObservation? observation,string state,string invariant)
        => trace.Add(observation?.Diagnostic(state,invariant)??"run=unbound iteration=0 session=0 state="+state+" invariant="+invariant+" event=None");
}

internal sealed class L00CLifecycleShutdownIdentity
{
    private L00CLifecycleShutdownIdentity(long runId,string instanceId,string savegameGuid){RunId=runId;InstanceId=instanceId;SavegameGuid=savegameGuid;}
    internal long RunId { get; }
    internal string InstanceId { get; }
    internal string SavegameGuid { get; }
    internal static L00CLifecycleShutdownIdentity Create(long runId,string instanceId,string savegameGuid)
    {
        if(runId<=0||string.IsNullOrWhiteSpace(instanceId))throw new InvalidOperationException("L00-C lifecycle shutdown identity has an invalid run or instance.");
        return new L00CLifecycleShutdownIdentity(runId,instanceId,NormalizeGuid(savegameGuid,nameof(savegameGuid)));
    }
    internal static string NormalizeGuid(string value,string parameterName)
    {
        if(!Guid.TryParseExact(value,"D",out Guid parsed)||!string.Equals(value,parsed.ToString("D"),StringComparison.Ordinal))throw new ArgumentException("L00-C lifecycle savegame identity must be a canonical lower-case D-format GUID.",parameterName);return value;
    }
}

internal sealed class L00CLifecycleShutdownLease
{
    internal L00CLifecycleShutdownLease(L00CLifecycleShutdownIdentity identity,long generation){Identity=identity;Generation=generation;}
    internal L00CLifecycleShutdownIdentity Identity { get; }
    internal long Generation { get; }
    internal bool Closed { get; set; }
    internal bool ServerReleased { get; set; }
}

internal sealed class L00CLifecycleReturnReservation
{
    internal L00CLifecycleReturnReservation(L00CLifecycleShutdownLease lease,L00CLifecycleSessionObservation observation){Lease=lease;ReadyObservation=observation;}
    internal L00CLifecycleShutdownLease Lease { get; }
    internal L00CLifecycleSessionObservation ReadyObservation { get; }
    internal bool Started { get; set; }
    internal bool Cancelled { get; set; }
    internal bool Committed { get; set; }
}

internal sealed class L00CNativeSaveQuitReturnProof
{
    internal L00CNativeSaveQuitReturnProof(bool nativeActionCompleted,bool serverStopped,bool mainMenuReady,bool targetExclusivelyOpenable,string canonicalSavePath,string canonicalSavegameGuid)
    {
        NativeActionCompleted=nativeActionCompleted;ServerStopped=serverStopped;MainMenuReady=mainMenuReady;TargetExclusivelyOpenable=targetExclusivelyOpenable;
        CanonicalSavePath=L00CLifecycleSessionObservation.NormalizeSavePath(canonicalSavePath,nameof(canonicalSavePath));CanonicalSavegameGuid=L00CLifecycleShutdownIdentity.NormalizeGuid(canonicalSavegameGuid,nameof(canonicalSavegameGuid));
    }
    internal bool NativeActionCompleted { get; }
    internal bool ServerStopped { get; }
    internal bool MainMenuReady { get; }
    internal bool TargetExclusivelyOpenable { get; }
    internal string CanonicalSavePath { get; }
    internal string CanonicalSavegameGuid { get; }
    internal void RequireActualNativeReturn(L00CLifecycleSessionObservation observation)
    {
        if(!NativeActionCompleted||!ServerStopped||!MainMenuReady||!TargetExclusivelyOpenable||!string.Equals(CanonicalSavePath,observation.CanonicalSavePath,StringComparison.OrdinalIgnoreCase)||CanonicalSavegameGuid!=observation.CanonicalSavegameGuid)throw new InvalidOperationException("L00-C SaveCommitted requires actual native Save&Quit completion, stopped server, main menu, exclusive target, and exact identity.");
    }
}
#endif
