#if L00C_STANDALONE_ORACLE
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using ISRWorldGen.WorldgenProbe;

namespace ISRWorldGen.L00C.Laboratory;

internal static class L00CScenarioCompositionOracle
{
    internal static int Run()
    {
        string container=Path.Combine(Path.GetTempPath(),"l00c-s4-composition-"+Guid.NewGuid().ToString("N"));
        try
        {
            string root=Path.Combine(container,"repo",".local","L00C"),saves=Path.Combine(container,"saves");
            Directory.CreateDirectory(root);Directory.CreateDirectory(saves);
            ExecuteNominal(root,saves);
            VerifyInterruptedCleanup(root,saves);
            RejectDuplicateJsonl(root,saves);
            RejectStaleDuplicateAndClosingEvents(saves);
            return 0;
        }
        finally{if(Directory.Exists(container))Directory.Delete(container,true);}
    }

    private static void VerifyInterruptedCleanup(string root,string saves)
    {
        L00CCampaignStorage storage=L00CCampaignStorage.CreateForRun(root,saves,Guid.NewGuid().ToString("N"));
        L00CCampaignSaveTarget target=storage.SaveTargets[0];
        storage.PrepareNativeCreate(target.Role,target.CanonicalSavePath);
        WriteNative(target.CanonicalSavePath,target.Role);storage.PublishFixtureMarker(target.Role,target.CanonicalSavePath);
        int pid=Process.GetCurrentProcess().Id;
        L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,storage.RunId,pid,()=>true);
        if(!File.Exists(storage.AbortCleanedPath)||File.Exists(target.CanonicalSavePath)||
            File.Exists(L00CCampaignStorage.WalPath(target.CanonicalSavePath))||
            File.Exists(L00CCampaignStorage.ShmPath(target.CanonicalSavePath))||
            File.Exists(L00CCampaignStorage.MarkerPath(target.CanonicalSavePath)))
            throw new InvalidOperationException("L00-C interrupted composition did not produce exact abort cleanup proof.");
    }

    private static void ExecuteNominal(string root,string saves)
    {
        string runId=Guid.NewGuid().ToString("N");
        L00CCampaignStorage storage=L00CCampaignStorage.CreateForRun(root,saves,runId);
        var composition=new L00CProductionScenarioComposition(storage);
        L00CScenarioDefinition definition=L00CScenarioDefinition.CreateOwned(runId,saves,Stopwatch.Frequency);
        L00CScenarioState state=L00CScenarioState.Start(definition,1);
        long now=1;int withheldDispatches=0;
        for(int iteration=1;iteration<=5;iteration++)
        {
            state=state.AcceptObservation(L00CScenarioObservation.MenuReady(),++now);
            string aGuid=GuidFor(iteration,'a'),bGuid=GuidFor(iteration,'b');
            string aMarker=(iteration*2-1).ToString("x32"),bMarker=(iteration*2).ToString("x32");
            RunSession(composition,storage,ref state,ref now,aGuid,aMarker,true,ref withheldDispatches);
            RunSession(composition,storage,ref state,ref now,aGuid,aMarker,false,ref withheldDispatches);
            RunSession(composition,storage,ref state,ref now,bGuid,bMarker,true,ref withheldDispatches);
            if(state.Stage!=L00CScenarioStage.IterationVerified)throw new InvalidOperationException("L00-C composition did not verify the exact iteration.");
            state=state.AdvanceVerifiedIteration(++now);
        }
        if(state.Outcome!=L00CScenarioOutcome.RunCompleted||withheldDispatches!=15)
            throw new InvalidOperationException("L00-C composition did not gate all fifteen scheduler returns.");
        composition.CompleteRun(state);

        int pid=Process.GetCurrentProcess().Id;
        RefuseCode(()=>L00CT00LifecycleValidator.Validate(root,saves,runId,pid,storage.EvidenceDirectory),"L00C_T00_06_CLEANUP_INCOMPLETE");
        L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,runId,pid,()=>true);
        L00CT00LifecycleValidationReport report=L00CT00LifecycleValidator.Validate(root,saves,runId,pid,storage.EvidenceDirectory);
        if(report.Iterations!=5||report.Sessions!=15||report.DedicatedSaves!=10||report.Observations!=30||report.RegistrationEvents!=90)
            throw new InvalidOperationException("L00-C independent validator rejected production composition cardinality.");
    }

    private static void RunSession(L00CProductionScenarioComposition composition,L00CCampaignStorage storage,
        ref L00CScenarioState state,ref long now,string guid,string markerId,bool isNew,ref int withheldDispatches)
    {
        state=state.BeginAction(++now);composition.BeginNativeOpen(state);
        if(isNew)WriteNative(state.ExpectedTarget.CanonicalSavePath,state.ExpectedTarget.Role);
        composition.CompleteNativeOpen();L00CProcessCampaignController.ResetForTests();
        state=state.MarkNativeEffectCompleted(++now).CompleteAction(++now);

        L00CLifecycleShutdownLease lease=L00CLifecycleShutdownBarrier.Open(L00CLifecycleShutdownIdentity.Create(state.ExpectedSessionOrdinal,"server-"+state.ExpectedSessionOrdinal,guid));
        L00CLifecycleShutdownBarrier.CaptureInternalMarker(lease,new L00CLifecycleInternalMarkerProof(markerId,guid,isNew?1:2));L00CLifecycleShutdownBarrier.Arm(lease);
        L00CScenarioObservation ready=L00CScenarioObservation.SessionReady(state.ExpectedSessionOrdinal,
            state.ExpectedTarget.CanonicalSavePath,guid,isNew,true);
        composition.AcceptReady(state,ready);state=state.AcceptObservation(ready,++now);

        state=state.BeginAction(++now);composition.PrepareNativeReturn(state);composition.BeginNativeReturn(state);
        state=state.MarkNativeEffectCompleted(++now).CompleteAction(++now);
        if(!L00CLifecycleShutdownBarrier.Close(lease))throw new InvalidOperationException("L00-C controlled server lease did not close.");
        var proof=new L00CNativeSaveQuitReturnProof(true,true,true,true,state.ActiveSession!.CanonicalSavePath,guid);
        if(composition.AcceptSaveCommitted(state,state.ActiveSession,proof,out _))
            throw new InvalidOperationException("L00-C scheduler observed SaveCommitted before registration-release proof.");
        withheldDispatches++;
        L00CLifecycleShutdownBarrier.CaptureRegistrationRelease(lease,CompleteRegistrations());
        if(!composition.AcceptSaveCommitted(state,state.ActiveSession,proof,out L00CScenarioObservation? committed)||committed is null)
            throw new InvalidOperationException("L00-C scheduler did not observe SaveCommitted after complete proof.");
        state=state.AcceptObservation(committed,++now);
    }

    private static void RejectDuplicateJsonl(string root,string saves)
    {
        L00CCampaignStorage storage=L00CCampaignStorage.CreateForRun(root,saves,Guid.NewGuid().ToString("N"));
        var writer=new L00CT00LifecycleEvidenceWriter(storage);string guid=GuidFor(1,'a');
        var observation=L00CLifecycleSessionObservation.Ready(storage.RunId,1,1,storage.SavePath(1,'a'),guid,true);
        var marker=new L00CLifecycleInternalMarkerProof("a".PadLeft(32,'0'),guid,1);
        writer.RecordReady(observation,marker);Refuse(()=>writer.RecordReady(observation,marker));
        int pid=Process.GetCurrentProcess().Id;L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,storage.RunId,pid,()=>true);
    }

    private static void RejectStaleDuplicateAndClosingEvents(string saves)
    {
        string run=Guid.NewGuid().ToString("N"),guid=GuidFor(1,'a'),path=Path.GetFullPath(Path.Combine(saves,"ISRWorldGen-L00C-"+run+"-iteration-01-a.vcdbs"));
        var state=new L00CLifecycleShutdownState();var lease=state.Open(L00CLifecycleShutdownIdentity.Create(1,"negative",guid));
        state.CaptureInternalMarker(lease,new L00CLifecycleInternalMarkerProof("b".PadLeft(32,'0'),guid,1));state.Arm(lease);
        var ready=L00CLifecycleSessionObservation.Ready(run,1,1,path,guid,true);state.BindReady(lease,ready);
        Refuse(()=>state.BindReady(lease,ready));
        var copied=L00CLifecycleSessionObservation.Ready(run,1,1,path,guid,true);Refuse(()=>state.PrepareReturn(copied,path));
        var reservation=state.PrepareReturn(ready,path);state.BeginNativeReturn(reservation);state.Close(lease);
        Refuse(()=>state.BindReady(lease,ready));
    }

    private static L00CLifecycleRegistrationProof CompleteRegistrations()=>new(true,true,true,true,true,true,true,true,0,0,0,0);
    private static void WriteNative(string path,string text){File.WriteAllText(path,text);File.WriteAllText(L00CCampaignStorage.WalPath(path),text+"-wal");File.WriteAllText(L00CCampaignStorage.ShmPath(path),text+"-shm");}
    private static string GuidFor(int iteration,char slot)=>new Guid(iteration,(short)slot,0,new byte[]{1,2,3,4,5,6,7,8}).ToString("D");
    private static void Refuse(Action action){try{action();}catch(InvalidOperationException){return;}throw new InvalidOperationException("L00-C composition oracle expected refusal.");}
    private static void RefuseCode(Action action,string code){try{action();}catch(L00CT00LifecycleValidationException exception)when(exception.Code==code){return;}throw new InvalidOperationException("L00-C composition oracle expected "+code+".");}
}
#endif
