// Debug-only host for the immutable L00-C S2 scenario. External state changes
// are delegated through a narrow port so the ten-save manifest and S3
// observation owner remain explicit integration contracts.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ISRWorldGen.WorldgenProbe;

namespace ISRWorldGen.L00C.Laboratory;

internal abstract class L00CScenarioHostAdapter
{
    internal abstract long ReadMonotonicTimestamp();
    internal abstract string? ReadStopCode();
    internal abstract bool TryObserve(object screenManager, L00CScenarioState state,
        int readyEventSessionOrdinal, out L00CScenarioObservation? observation);
    internal abstract void ExecuteNativeAction(object screenManager, L00CScenarioState state);
    internal abstract void CompleteRun(L00CScenarioState state);
}

/// <summary>
/// Native S2 implementation. The injected callbacks are the only integration
/// seams for storage/open/return ownership; all game-object access remains a
/// transient driver local and observations are immutable values.
/// </summary>
internal sealed class L00CNativeScenarioHostAdapter : L00CScenarioHostAdapter
{
    private readonly Func<string?> readStopCode;
    private readonly Action<L00CScenarioState> beginNativeOpen;
    private readonly Action<L00CScenarioState> beginNativeReturn;
    private readonly Action<L00CScenarioState> completeRun;
    private readonly L00CProductionScenarioComposition? production;

    internal L00CNativeScenarioHostAdapter(Func<string?> stopCode,
        Action<L00CScenarioState> nativeOpenBoundary,
        Action<L00CScenarioState> nativeReturnBoundary,
        Action<L00CScenarioState> completionBoundary)
    {
        readStopCode = stopCode ?? throw new ArgumentNullException(nameof(stopCode));
        beginNativeOpen = nativeOpenBoundary ?? throw new ArgumentNullException(nameof(nativeOpenBoundary));
        beginNativeReturn = nativeReturnBoundary ?? throw new ArgumentNullException(nameof(nativeReturnBoundary));
        completeRun = completionBoundary ?? throw new ArgumentNullException(nameof(completionBoundary));
    }

    internal L00CNativeScenarioHostAdapter(L00CProductionScenarioComposition composition)
    {
        production = composition ?? throw new ArgumentNullException(nameof(composition));
        readStopCode = composition.ReadStopCode;
        beginNativeOpen = composition.BeginNativeOpen;
        beginNativeReturn = composition.BeginNativeReturn;
        completeRun = composition.CompleteRun;
    }

    internal override long ReadMonotonicTimestamp() => Stopwatch.GetTimestamp();
    internal override string? ReadStopCode() => readStopCode();

    internal override bool TryObserve(object screenManager, L00CScenarioState state,
        int readyEventSessionOrdinal, out L00CScenarioObservation? observation)
    {
        observation = null;
        switch (state.Stage)
        {
            case L00CScenarioStage.WaitMenuBeforeCreateA:
                if (!L00CMenuActionDriver.TryObserveMenuReady(screenManager)) return false;
                observation = L00CScenarioObservation.MenuReady();
                return true;
            case L00CScenarioStage.WaitCreatedAReady:
            case L00CScenarioStage.WaitReopenedAReady:
            case L00CScenarioStage.WaitCreatedBReady:
                bool isNew = state.Stage != L00CScenarioStage.WaitReopenedAReady;
                bool observed = L00CMenuActionDriver.TryObserveReadySession(screenManager,
                    state.ExpectedSessionOrdinal, state.ExpectedTarget, isNew,
                    readyEventSessionOrdinal, out observation);
                if (observed && observation is not null) production?.AcceptReady(state, observation);
                return observed;
            case L00CScenarioStage.WaitMenuAfterCreatedASave:
            case L00CScenarioStage.WaitMenuBeforeCreateB:
            case L00CScenarioStage.WaitMenuAfterCreatedBSave:
                if (state.ActiveSession is null)
                    throw new L00CScenarioException("L00C_S2_COMMIT_SESSION_STATE_ABSENT", state.Stage,
                        "save/menu observation requires the immutable just-closed session");
                if (production is not null)
                    return production.TryObserveSaveCommitted(screenManager, state, state.ActiveSession, out observation);
                return L00CMenuActionDriver.TryObserveSaveCommitted(screenManager, state.ActiveSession, out observation, out _);
            default:
                throw new L00CScenarioException("L00C_S2_NATIVE_OBSERVATION_STAGE_INVALID", state.Stage,
                    "native adapter received a non-wait stage");
        }
    }

    internal override void ExecuteNativeAction(object screenManager, L00CScenarioState state)
    {
        if (state.ActionProgress != L00CScenarioActionProgress.Started)
            throw new L00CScenarioException("L00C_S2_NATIVE_ACTION_BOUNDARY_INVALID", state.Stage,
                "native adapter requires Started progress");
        switch (state.PendingAction)
        {
            case L00CScenarioActionKind.Create:
                ExecuteNativeOpen(screenManager, state, isNew: true);
                return;
            case L00CScenarioActionKind.Reopen:
                ExecuteNativeOpen(screenManager, state, isNew: false);
                return;
            case L00CScenarioActionKind.SaveQuit:
                if (state.ActiveSession is null)
                    throw new L00CScenarioException("L00C_S2_SAVEQUIT_SESSION_STATE_ABSENT", state.Stage,
                        "SaveQuit requires the immutable ready session");
                production?.PrepareNativeReturn(state);
                try
                {
                    _ = L00CMenuActionDriver.SaveAndQuit(screenManager, state.ActiveSession,
                        () => beginNativeReturn(state));
                }
                catch
                {
                    production?.AbortBeforeNativeReturn();
                    throw;
                }
                return;
            default:
                throw new L00CScenarioException("L00C_S2_NATIVE_ACTION_KIND_INVALID", state.Stage,
                    "native adapter received action None");
        }
    }

    private void ExecuteNativeOpen(object screenManager,L00CScenarioState state,bool isNew)
    {
        try
        {
            _ = isNew
                ? L00CMenuActionDriver.CreateWorld(screenManager,state.ExpectedTarget,()=>beginNativeOpen(state))
                : L00CMenuActionDriver.ReopenWorld(screenManager,state.ExpectedTarget,()=>beginNativeOpen(state));
            production?.CompleteNativeOpen();
        }
        catch
        {
            production?.AbortNativeOpen();
            throw;
        }
    }

    internal override void CompleteRun(L00CScenarioState state)
    {
        if (state.Outcome != L00CScenarioOutcome.RunCompleted || state.Stage != L00CScenarioStage.RunCompleted)
            throw new L00CScenarioException("L00C_S2_COMPLETION_BOUNDARY_INVALID", state.Stage,
                "completion callback requires RUN_COMPLETED");
        completeRun(state);
    }
}

/// <summary>
/// Process-local glue between the immutable S2 scheduler and the S3 server
/// shutdown barrier. It owns no Vintage Story object and permits the scheduler
/// to see SaveCommitted only after the exact server lease and registrations are
/// released.
/// </summary>
internal sealed class L00CProductionScenarioComposition
{
    private readonly L00CCampaignStorage campaign;
    private readonly L00CT00LifecycleEvidenceWriter evidence;
    private L00CNativeOpenReservation? pendingOpen;
    private L00CLifecycleSessionObservation? ready;
    private L00CLifecycleInternalMarkerProof? marker;
    private L00CLifecycleReturnReservation? pendingReturn;

    internal L00CProductionScenarioComposition(L00CCampaignStorage campaignStorage)
    {
        campaign = campaignStorage ?? throw new ArgumentNullException(nameof(campaignStorage));
        if(campaign.SaveTargets.Count!=L00CScenarioDefinition.RequiredDedicatedSaves)
            throw new InvalidOperationException("L00-C production composition requires the exact ten-save campaign manifest.");
        evidence = new L00CT00LifecycleEvidenceWriter(campaign);
    }

    internal string? ReadStopCode() => null;

    internal void BeginNativeOpen(L00CScenarioState state)
    {
        if(state is null)throw new ArgumentNullException(nameof(state));
        if(pendingOpen is not null||pendingReturn is not null||ready is not null)
            throw new InvalidOperationException("L00-C production native open overlaps an active session or reservation.");
        L00CScenarioTarget target=state.ExpectedTarget;
        if(state.PendingAction==L00CScenarioActionKind.Create)
        {
            campaign.RequireVacantNativeCreateTarget(target.Role,target.CanonicalSavePath);
            campaign.PrepareNativeCreate(target.Role,target.CanonicalSavePath);
            try{campaign.RequireVacantNativeCreateTarget(target.Role,target.CanonicalSavePath);}
            catch{campaign.RecordNativeCreateRefusal(target.Role,target.CanonicalSavePath);throw;}
        }
        else if(state.PendingAction!=L00CScenarioActionKind.Reopen)
            throw new InvalidOperationException("L00-C production native open requires Create or Reopen.");
        pendingOpen=L00CProcessCampaignController.BeginNativeOpen(state.ExpectedSessionOrdinal);
    }

    internal void CompleteNativeOpen()
    {
        L00CNativeOpenReservation opening=pendingOpen??throw new InvalidOperationException("L00-C production native open completion has no reservation.");
        L00CProcessCampaignController.CompleteNativeOpen(opening);pendingOpen=null;
    }

    internal void AbortNativeOpen()
    {
        L00CNativeOpenReservation? opening=pendingOpen;pendingOpen=null;
        if(opening is not null)_=L00CProcessCampaignController.AbortNativeOpen(opening);
    }

    internal void AcceptReady(L00CScenarioState state,L00CScenarioObservation observation)
    {
        if(state is null)throw new ArgumentNullException(nameof(state));if(observation is null)throw new ArgumentNullException(nameof(observation));
        if(ready is not null||marker is not null||pendingReturn is not null||observation.Kind!=L00CScenarioObservationKind.SessionReady)
            throw new InvalidOperationException("L00-C production Ready observation is duplicate or overlaps another session.");
        if(observation.IsNew)campaign.PublishFixtureMarker(state.ExpectedTarget.Role,observation.CanonicalSavePath!);
        var captured=L00CLifecycleSessionObservation.Ready(campaign.RunId,state.Iteration,observation.SessionOrdinal,
            observation.CanonicalSavePath!,observation.CanonicalSavegameGuid!,observation.IsNew);
        L00CLifecycleInternalMarkerProof internalMarker=L00CLifecycleShutdownBarrier.BindReadyCurrent(captured);
        int expectedOpenCount=observation.IsNew?1:2;
        if(internalMarker.OpenCount!=expectedOpenCount)
            throw new InvalidOperationException("L00-C production Ready marker count does not match create/reopen semantics.");
        evidence.RecordReady(captured,internalMarker);ready=captured;marker=internalMarker;
    }

    internal void PrepareNativeReturn(L00CScenarioState state)
    {
        if(state is null)throw new ArgumentNullException(nameof(state));
        L00CLifecycleSessionObservation captured=ready??throw new InvalidOperationException("L00-C production return has no captured Ready observation.");
        if(pendingReturn is not null||state.ActiveSession is null||state.ActiveSession.Ordinal!=captured.SessionOrdinal)
            throw new InvalidOperationException("L00-C production return overlaps or differs from the captured S2 session.");
        pendingReturn=L00CLifecycleShutdownBarrier.PrepareReturn(captured,state.ActiveSession.CanonicalSavePath);
    }

    internal void BeginNativeReturn(L00CScenarioState state)
    {
        if(state is null)throw new ArgumentNullException(nameof(state));
        L00CLifecycleReturnReservation reservation=pendingReturn??throw new InvalidOperationException("L00-C production native return has no prepared reservation.");
        if(state.ActiveSession is null||state.ActiveSession.Ordinal!=reservation.ReadyObservation.SessionOrdinal)
            throw new InvalidOperationException("L00-C production native return changed the captured S2 session.");
        L00CLifecycleShutdownBarrier.BeginNativeReturn(reservation);
    }

    internal void AbortBeforeNativeReturn()
    {
        L00CLifecycleReturnReservation? reservation=pendingReturn;
        if(reservation is not null&&L00CLifecycleShutdownBarrier.AbortBeforeNativeReturn(reservation))pendingReturn=null;
    }

    internal bool TryObserveSaveCommitted(object screenManager,L00CScenarioState state,L00CScenarioSession session,
        out L00CScenarioObservation? observation)
    {
        observation=null;
        L00CLifecycleReturnReservation reservation=pendingReturn??throw new InvalidOperationException("L00-C production SaveCommitted poll has no native return reservation.");
        if(!L00CMenuActionDriver.TryObserveSaveCommitted(screenManager,session,out L00CScenarioObservation? s2,
                out L00CNativeSaveQuitReturnProof? nativeProof)||s2 is null||nativeProof is null)return false;
        return AcceptSaveCommitted(state,session,nativeProof,out observation);
    }

    internal bool AcceptSaveCommitted(L00CScenarioState state,L00CScenarioSession session,
        L00CNativeSaveQuitReturnProof nativeProof,out L00CScenarioObservation? observation)
    {
        if(state is null)throw new ArgumentNullException(nameof(state));if(session is null)throw new ArgumentNullException(nameof(session));if(nativeProof is null)throw new ArgumentNullException(nameof(nativeProof));
        observation=null;
        L00CLifecycleReturnReservation reservation=pendingReturn??throw new InvalidOperationException("L00-C production SaveCommitted acceptance has no native return reservation.");
        if(!L00CLifecycleShutdownBarrier.TryGetCompletedEvidence(reservation,out L00CLifecycleCommittedEvidence? completed)||completed is null)return false;
        L00CLifecycleSessionObservation captured=ready??throw new InvalidOperationException("L00-C production SaveCommitted lost its Ready identity.");
        L00CLifecycleInternalMarkerProof capturedMarker=marker??throw new InvalidOperationException("L00-C production SaveCommitted lost its marker identity.");
        if(completed.InternalMarker.MarkerId!=capturedMarker.MarkerId||completed.InternalMarker.OpenCount!=capturedMarker.OpenCount||
            completed.InternalMarker.CanonicalSavegameGuid!=capturedMarker.CanonicalSavegameGuid)
            throw new InvalidOperationException("L00-C production SaveCommitted reattributed its internal marker.");
        var committed=L00CLifecycleSessionObservation.SaveCommitted(campaign.RunId,state.Iteration,session.Ordinal,
            session.CanonicalSavePath,session.CanonicalSavegameGuid,captured.IsNew);
        L00CLifecycleShutdownBarrier.ConfirmSaveCommitted(reservation,committed,nativeProof);
        evidence.RecordSaveCommitted(committed,capturedMarker,nativeProof,completed.Registrations);
        pendingReturn=null;ready=null;marker=null;
        observation=L00CScenarioObservation.SaveCommitted(session.Ordinal,session.CanonicalSavePath,session.CanonicalSavegameGuid,commitEventObserved:true);
        return true;
    }

    internal void CompleteRun(L00CScenarioState state)
    {
        if(state is null)throw new ArgumentNullException(nameof(state));
        if(state.Outcome!=L00CScenarioOutcome.RunCompleted||pendingOpen is not null||pendingReturn is not null||ready is not null||marker is not null)
            throw new InvalidOperationException("L00-C production completion has pending native ownership.");
        campaign.BeginCycling();evidence.Complete(state);campaign.SealForExternalCleanup();
    }
}

internal sealed class L00CT00LifecycleEvidenceWriter
{
    private readonly L00CCampaignStorage campaign;
    private readonly string observationsPath;
    private readonly string registrationsPath;
    private int nextSession=1;
    private int readySession;
    private int observationCount;
    private int registrationCount;

    internal L00CT00LifecycleEvidenceWriter(L00CCampaignStorage campaignStorage)
    {
        campaign=campaignStorage??throw new ArgumentNullException(nameof(campaignStorage));
        observationsPath=Path.Combine(campaign.EvidenceDirectory,"t00-06-observations.jsonl");
        registrationsPath=Path.Combine(campaign.EvidenceDirectory,"t00-06-registrations.jsonl");
        CreateEmpty(observationsPath);CreateEmpty(registrationsPath);
    }

    internal void RecordReady(L00CLifecycleSessionObservation observation,L00CLifecycleInternalMarkerProof marker)
    {
        if(observation is null)throw new ArgumentNullException(nameof(observation));if(marker is null)throw new ArgumentNullException(nameof(marker));
        if(observation.EventKind!=L00CLifecycleEventKind.Ready||observation.SessionOrdinal!=nextSession||readySession!=0||
            marker.CanonicalSavegameGuid!=observation.CanonicalSavegameGuid)
            throw new InvalidOperationException("L00-C JSONL Ready event is stale, duplicate, or reattributed.");
        Append(observationsPath,ObservationJson(observation,marker,observation.IsNew?"InternalStoreCreated":"InternalStoreReRead",false));
        readySession=observation.SessionOrdinal;observationCount++;
    }

    internal void RecordSaveCommitted(L00CLifecycleSessionObservation observation,L00CLifecycleInternalMarkerProof marker,
        L00CNativeSaveQuitReturnProof nativeProof,L00CLifecycleRegistrationProof registrations)
    {
        if(observation is null)throw new ArgumentNullException(nameof(observation));if(marker is null)throw new ArgumentNullException(nameof(marker));
        if(nativeProof is null)throw new ArgumentNullException(nameof(nativeProof));if(registrations is null)throw new ArgumentNullException(nameof(registrations));
        if(observation.EventKind!=L00CLifecycleEventKind.SaveCommitted||observation.SessionOrdinal!=nextSession||readySession!=nextSession||
            marker.CanonicalSavegameGuid!=observation.CanonicalSavegameGuid)
            throw new InvalidOperationException("L00-C JSONL SaveCommitted event is stale, duplicate, or reattributed.");
        nativeProof.RequireActualNativeReturn(observation);registrations.RequireComplete();
        Append(observationsPath,ObservationJson(observation,marker,"InternalStoreCaptured",true));observationCount++;
        foreach(string phase in new[]{"Registered","Released"})
            foreach(string kind in new[]{"InitWorldGenerator","GameWorldSave","Tick"})
            {
                bool released=phase=="Released";
                bool independent=released&&kind!="InitWorldGenerator";
                string line="{\"schema\":\"l00c-t00-06-registration-v1\",\"runId\":\""+campaign.RunId+
                    "\",\"iteration\":"+observation.Iteration+",\"sessionOrdinal\":"+observation.SessionOrdinal+
                    ",\"registration\":\""+kind+"\",\"eventKind\":\""+phase+"\",\"state\":\""+phase+
                    "\",\"invariant\":\"actual registration and independent release captured from server ledger\",\"ownerReferenceReleased\":"+
                    Bool(released)+",\"independentlyUnregistered\":"+Bool(independent)+"}";
                Append(registrationsPath,line);registrationCount++;
            }
        readySession=0;nextSession++;
    }

    internal void Complete(L00CScenarioState state)
    {
        if(state is null)throw new ArgumentNullException(nameof(state));
        if(state.Outcome!=L00CScenarioOutcome.RunCompleted||nextSession!=16||readySession!=0||observationCount!=30||registrationCount!=90)
            throw new InvalidOperationException("L00-C JSONL completion requires exactly fifteen committed sessions.");
        string runPath=Path.Combine(campaign.EvidenceDirectory,"t00-06-run.json");
        string json="{\"schema\":\"l00c-t00-06-run-v1\",\"runId\":\""+campaign.RunId+
            "\",\"outcome\":\"RUN_COMPLETED\",\"iterations\":5,\"sessions\":15,\"dedicatedSaves\":10,\"runtimeProcessId\":"+
            Process.GetCurrentProcess().Id+",\"timedOut\":false,\"partialStop\":false,\"staleCallbacks\":0,\"duplicateCallbacks\":0,\"closingCallbacks\":0,\"unregistrationFailures\":0,\"provenanceSha256\":\""+
            Hash(campaign.ProvenancePath)+"\",\"observationsSha256\":\""+Hash(observationsPath)+"\",\"registrationsSha256\":\""+Hash(registrationsPath)+"\"}";
        WriteNew(runPath,json);
    }

    private static string ObservationJson(L00CLifecycleSessionObservation observation,L00CLifecycleInternalMarkerProof marker,string markerEvidence,bool committed)
        =>"{\"schema\":\"l00c-t00-06-observation-v1\",\"runId\":\""+observation.RunId+"\",\"iteration\":"+observation.Iteration+
            ",\"sessionOrdinal\":"+observation.SessionOrdinal+",\"canonicalSavePath\":\""+Escape(observation.CanonicalSavePath)+
            "\",\"canonicalSavegameGuid\":\""+observation.CanonicalSavegameGuid+"\",\"isNew\":"+Bool(observation.IsNew)+
            ",\"eventKind\":\""+observation.EventKind+"\",\"state\":\""+observation.EventKind+
            "\",\"invariant\":\"exact immutable session, internal marker, native return, and registration ledger\",\"markerId\":\""+
            marker.MarkerId+"\",\"markerOpenCount\":"+marker.OpenCount+",\"markerEvidenceKind\":\""+markerEvidence+
            "\",\"nativeActionCompleted\":"+Bool(committed)+",\"serverStopped\":"+Bool(committed)+",\"mainMenuReady\":"+
            Bool(committed)+",\"targetExclusivelyOpenable\":"+Bool(committed)+"}";
    private static void CreateEmpty(string path){using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough);stream.Flush(true);}
    private static void Append(string path,string line){using var stream=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read,4096,FileOptions.WriteThrough);byte[] bytes=Encoding.UTF8.GetBytes(line+"\n");stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
    private static void WriteNew(string path,string text){using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough);byte[] bytes=Encoding.UTF8.GetBytes(text);stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
    private static string Hash(string path){using SHA256 hash=SHA256.Create();using FileStream stream=new(path,FileMode.Open,FileAccess.Read,FileShare.Read);byte[] bytes=hash.ComputeHash(stream);var text=new StringBuilder(64);foreach(byte value in bytes)text.Append(value.ToString("X2"));return text.ToString();}
    private static string Bool(bool value)=>value?"true":"false";
    private static string Escape(string value)=>value.Replace("\\","\\\\").Replace("\"","\\\"");
}

/// <summary>
/// Drives one exact five-iteration scenario and retains only immutable state,
/// a monotonic ready-event ordinal, and append-only progress evidence.
/// </summary>
public sealed class L00CMenuActionLaboratoryHost
{
    private readonly string evidenceDirectory;
    private readonly L00CScenarioHostAdapter adapter;
    private L00CScenarioState state;
    private int readyEventSessionOrdinal;
    private int writtenTraceEntries;
    private bool completionPublished;

    private L00CMenuActionLaboratoryHost(L00CScenarioDefinition definition, string evidence,
        L00CScenarioHostAdapter scenarioAdapter)
    {
        evidenceDirectory = evidence;
        adapter = scenarioAdapter;
        state = L00CScenarioState.Start(definition, adapter.ReadMonotonicTimestamp());
        WriteNewTraceEntries();
    }

    internal static L00CMenuActionLaboratoryHost OpenIntegrated(L00CScenarioDefinition definition,
        string evidenceDirectory, L00CScenarioHostAdapter adapter)
    {
        RequireDebugLaboratory();
        if (definition is null) throw new ArgumentNullException(nameof(definition));
        if (adapter is null) throw new ArgumentNullException(nameof(adapter));
        string evidence = RequireNewEvidenceDirectory(evidenceDirectory);
        Directory.CreateDirectory(evidence);
        return new L00CMenuActionLaboratoryHost(definition, evidence, adapter);
    }

    /// <summary>
    /// Advances at most one observation or one native action per main-thread
    /// pump. A terminal stop/failure always throws its exact diagnostic code;
    /// RunCompleted returns true without claiming T00-06 PASS.
    /// </summary>
    public bool TryAdvance(object screenManager)
    {
        RequireDebugLaboratory();
        if (screenManager is null) throw new ArgumentNullException(nameof(screenManager));
        if (state.IsTerminal && state.Outcome != L00CScenarioOutcome.RunCompleted)
            throw state.CreateTerminalException();

        try
        {
            if (state.Outcome == L00CScenarioOutcome.RunCompleted) return PublishCompletionOnce();
            if (TryStop()) return ThrowStopped();

            if (state.Stage == L00CScenarioStage.IterationVerified)
            {
                state = state.AdvanceVerifiedIteration(adapter.ReadMonotonicTimestamp());
                WriteNewTraceEntries();
                return state.Outcome == L00CScenarioOutcome.RunCompleted && PublishCompletionOnce();
            }

            if (state.IsWaiting)
            {
                long timestamp = adapter.ReadMonotonicTimestamp();
                if (adapter.TryObserve(screenManager, state, readyEventSessionOrdinal,
                        out L00CScenarioObservation? observation) && observation is not null)
                {
                    state = state.AcceptObservation(observation, timestamp);
                    if (observation.Kind == L00CScenarioObservationKind.SessionReady)
                        readyEventSessionOrdinal = 0;
                }
                else state = state.Poll(timestamp);
                WriteNewTraceEntries();
                if (state.IsTerminal) throw state.CreateTerminalException();
                return false;
            }

            if (state.PendingAction == L00CScenarioActionKind.None)
                throw new L00CScenarioException("L00C_S2_HOST_STAGE_UNMAPPED", state.Stage,
                    "running state is neither wait, action, iteration verification, nor completion");

            state = state.BeginAction(adapter.ReadMonotonicTimestamp());
            WriteNewTraceEntries();
            if (TryStop()) return ThrowStopped();

            adapter.ExecuteNativeAction(screenManager, state);
            state = state.MarkNativeEffectCompleted(adapter.ReadMonotonicTimestamp());
            WriteNewTraceEntries();
            if (TryStop()) return ThrowStopped();

            state = state.CompleteAction(adapter.ReadMonotonicTimestamp());
            WriteNewTraceEntries();
            if (state.IsTerminal) throw state.CreateTerminalException();
            return false;
        }
        catch (L00CScenarioException exception)
        {
            state = state.FailExternal(state.LastTimestamp, exception.Code, exception.Invariant);
            WriteNewTraceEntries();
            WriteTerminalDiagnostic(state.TerminalCode!, state.Stage, state.TerminalInvariant!);
            throw new L00CScenarioException(state.TerminalCode!, state.Stage, state.TerminalInvariant!,
                exception.InnerException ?? exception);
        }
        catch (InvalidOperationException exception)
        {
            string code = "L00C_S2_ADAPTER_INVALID_OPERATION_AT_" + state.Stage.ToString().ToUpperInvariant() +
                "_" + state.ActionProgress.ToString().ToUpperInvariant();
            string invariant = "adapter invalid operation: " + exception.Message;
            state = state.FailExternal(state.LastTimestamp, code, invariant);
            WriteNewTraceEntries();
            WriteTerminalDiagnostic(state.TerminalCode!, state.Stage, state.TerminalInvariant!);
            throw new L00CScenarioException(state.TerminalCode!, state.Stage, state.TerminalInvariant!, exception);
        }
        catch (Exception exception)
        {
            string type = exception.GetType().Name.ToUpperInvariant();
            string code = "L00C_S2_UNMAPPED_" + type + "_AT_" + state.Stage.ToString().ToUpperInvariant() +
                "_" + state.ActionProgress.ToString().ToUpperInvariant();
            string invariant = "adapter exception type=" + exception.GetType().FullName + ";message=" + exception.Message;
            state = state.FailExternal(state.LastTimestamp, code, invariant);
            WriteNewTraceEntries();
            WriteTerminalDiagnostic(state.TerminalCode!, state.Stage, state.TerminalInvariant!);
            throw new L00CScenarioException(state.TerminalCode!, state.Stage, state.TerminalInvariant!, exception);
        }
    }

    // The process-wide controller forwards only the generation-correlated
    // ready event ordinal. No API/world/client object is captured or retained.
    internal void SignalLevelFinalize(int sessionOrdinal)
    {
        if (state.IsTerminal)
            throw new L00CScenarioException("L00C_S2_READY_EVENT_AFTER_TERMINAL", state.Stage,
                "terminal host cannot receive another ready event");
        if (sessionOrdinal != state.ExpectedSessionOrdinal ||
            state.Stage is not (L00CScenarioStage.WaitCreatedAReady or
                L00CScenarioStage.WaitReopenedAReady or L00CScenarioStage.WaitCreatedBReady))
            throw new L00CScenarioException("L00C_S2_READY_EVENT_STATE_MISMATCH", state.Stage,
                "expectedSession=" + state.ExpectedSessionOrdinal + ";actualSession=" + sessionOrdinal);
        if (readyEventSessionOrdinal != 0)
            throw new L00CScenarioException("L00C_S2_READY_EVENT_DUPLICATE", state.Stage,
                "one ready event is already pending for this session");
        readyEventSessionOrdinal = sessionOrdinal;
    }

    internal L00CScenarioState SnapshotForTests() => state;

    private bool TryStop()
    {
        string? code = adapter.ReadStopCode();
        if (code is null) return false;
        state = state.Stop(adapter.ReadMonotonicTimestamp(), code);
        WriteNewTraceEntries();
        return true;
    }

    private bool ThrowStopped() => throw state.CreateTerminalException();

    private bool PublishCompletionOnce()
    {
        if (!completionPublished)
        {
            adapter.CompleteRun(state);
            completionPublished = true;
            WriteTerminalDiagnostic("L00C_S2_RUN_COMPLETED_NOT_T00_06_PASS", state.Stage,
                "five iterations and final menu observed; independent T00-06 acceptance remains external");
        }
        return true;
    }

    private void WriteNewTraceEntries()
    {
        while (writtenTraceEntries < state.Trace.Count)
        {
            L00CScenarioTraceEntry entry = state.Trace[writtenTraceEntries];
            string path = Path.Combine(evidenceDirectory, "progress-" + entry.Sequence.ToString("D4") + ".json");
            string json = "{\"schema\":\"l00c-s2-progress-v1\",\"sequence\":" + entry.Sequence +
                ",\"monotonicTimestamp\":" + entry.Timestamp + ",\"iteration\":" + state.Iteration +
                ",\"stage\":\"" + entry.Stage + "\",\"outcome\":\"" + state.Outcome +
                "\",\"code\":\"" + Escape(entry.Code) + "\",\"detail\":\"" + Escape(entry.Detail) + "\"}";
            WriteNew(path, json);
            writtenTraceEntries++;
        }
    }

    private void WriteTerminalDiagnostic(string code, L00CScenarioStage stage, string invariant)
    {
        string path = Path.Combine(evidenceDirectory, "terminal-" + code + ".json");
        if (File.Exists(path)) return;
        string json = "{\"schema\":\"l00c-s2-terminal-v1\",\"code\":\"" + Escape(code) +
            "\",\"state\":\"" + stage + "\",\"outcome\":\"" + state.Outcome +
            "\",\"iteration\":" + state.Iteration + ",\"invariant\":\"" + Escape(invariant) + "\"}";
        WriteNew(path, json);
    }

    private static string RequireNewEvidenceDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            throw new L00CScenarioException("L00C_S2_EVIDENCE_PATH_INVALID", null,
                "evidence directory must be absolute");
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), full,
                StringComparison.OrdinalIgnoreCase) || Directory.Exists(full) || File.Exists(full))
            throw new L00CScenarioException("L00C_S2_EVIDENCE_PATH_NOT_NEW_CANONICAL", null,
                "evidence directory must be a new canonical path");
        return full;
    }

    private static void WriteNew(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4096, FileOptions.WriteThrough);
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(true);
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static void RequireDebugLaboratory()
    {
#if L00C_STANDALONE_ORACLE
        return;
#elif !DEBUG
        throw new L00CScenarioException("L00C_S2_DEBUG_BUILD_REQUIRED", null,
            "laboratory host is disabled outside Debug");
#else
        if (!Debugger.IsAttached || !string.Equals(Environment.GetEnvironmentVariable("ISR_L00C_LAB"), "1", StringComparison.Ordinal))
            throw new L00CScenarioException("L00C_S2_DEBUG_LAB_AUTHORITY_REQUIRED", null,
                "Debugger.IsAttached and ISR_L00C_LAB=1 are both required");
#endif
    }
}
