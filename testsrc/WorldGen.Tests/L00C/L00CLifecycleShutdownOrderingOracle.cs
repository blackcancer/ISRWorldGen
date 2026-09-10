#if L00C_STANDALONE_ORACLE
#nullable enable
using System;
using System.IO;
using ISRWorldGen.WorldgenProbe;

namespace ISRWorldGen.L00C.Laboratory;

internal static class L00CLifecycleShutdownOrderingOracle
{
    internal static int Run()
    {
        const string run="1234567890abcdef1234567890abcdef";
        string saves=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"l00c-s3-observation-saves"));
        foreach(string malformed in new[]{"","not-guid","11111111111111111111111111111111","{11111111-1111-1111-1111-111111111111}"})
            Refuse(()=>L00CLifecycleShutdownIdentity.Create(1,"instance",malformed),"malformed server GUID");

        int committed=0;
        for(int iteration=1;iteration<=5;iteration++)
        {
            string guidA=GuidFor(iteration,'a'),guidB=GuidFor(iteration,'b');
            committed+=RunSession(run,saves,iteration,(iteration-1)*3+1,'a',guidA,true);
            committed+=RunSession(run,saves,iteration,(iteration-1)*3+2,'a',guidA,false);
            committed+=RunSession(run,saves,iteration,iteration*3,'b',guidB,true);
        }
        if(committed!=15)throw new InvalidOperationException("L00-C lifecycle barrier did not commit all fifteen exact sessions.");

        string path=SavePath(saves,run,1,'a'),guid=GuidFor(1,'a');
        var state=new L00CLifecycleShutdownState();var identity=L00CLifecycleShutdownIdentity.Create(90,"negative",guid);var lease=state.Open(identity);
        var ready=L00CLifecycleSessionObservation.Ready(run,1,1,path,guid,true);
        Refuse(()=>state.BindReady(lease,ready),"Ready before stable");state.Arm(lease);state.BindReady(lease,ready);
        Refuse(()=>state.BindReady(lease,ready),"duplicate Ready");
        var copiedReady=L00CLifecycleSessionObservation.Ready(run,1,1,path,guid,true);
        Refuse(()=>state.PrepareReturn(copiedReady,path),"reattributed equal-value callback");
        var reservation=state.PrepareReturn(ready,path);Refuse(()=>state.PrepareReturn(ready,path),"duplicate reservation");
        if(!state.AbortBeforeNativeReturn(reservation))throw new InvalidOperationException("L00-C pre-native abort did not cancel its exact reservation.");
        Refuse(()=>state.BeginNativeReturn(reservation),"aborted reservation");
        var next=state.PrepareReturn(ready,path);state.BeginNativeReturn(next);Refuse(()=>state.BeginNativeReturn(next),"duplicate native return");
        if(state.AbortBeforeNativeReturn(next))throw new InvalidOperationException("L00-C started native return was made replayable.");
        Refuse(()=>state.ConfirmSaveCommitted(next,L00CLifecycleSessionObservation.SaveCommitted(run,1,1,path,guid,true),Proof(path,guid,true)),"commit before server release");
        if(!state.Close(lease))throw new InvalidOperationException("L00-C current owner did not close.");
        Refuse(()=>state.Open(L00CLifecycleShutdownIdentity.Create(2,"next-server",GuidFor(1,'b'))),"next Open before SaveCommitted");
        Refuse(()=>state.BindReady(lease,ready),"event during closing");
        foreach(int missing in new[]{0,1,2,3})
        {
            var proof=new L00CNativeSaveQuitReturnProof(missing!=0,missing!=1,missing!=2,missing!=3,path,guid);
            Refuse(()=>state.ConfirmSaveCommitted(next,L00CLifecycleSessionObservation.SaveCommitted(run,1,1,path,guid,true),proof),"partial native proof");
        }
        var committedObservation=state.ConfirmSaveCommitted(next,L00CLifecycleSessionObservation.SaveCommitted(run,1,1,path,guid,true),Proof(path,guid,true));
        if(committedObservation.EventKind!=L00CLifecycleEventKind.SaveCommitted)throw new InvalidOperationException("L00-C commit event kind changed.");
        Refuse(()=>state.ConfirmSaveCommitted(next,committedObservation,Proof(path,guid,true)),"duplicate SaveCommitted");
        L00CLifecycleShutdownLease successor=state.Open(L00CLifecycleShutdownIdentity.Create(2,"next-server",GuidFor(1,'b')));
        if(!state.Close(successor))throw new InvalidOperationException("L00-C next Open after SaveCommitted could not close.");
        foreach(string diagnostic in state.Trace)
            if(!diagnostic.Contains("run=")||!diagnostic.Contains("iteration=")||!diagnostic.Contains("session=")||!diagnostic.Contains("state=")||!diagnostic.Contains("invariant="))throw new InvalidOperationException("L00-C lifecycle diagnostic omitted identity/state/invariant.");
        return 0;
    }

    private static int RunSession(string run,string saves,int iteration,int ordinal,char slot,string guid,bool isNew)
    {
        string path=SavePath(saves,run,iteration,slot);var state=new L00CLifecycleShutdownState();
        var identity=L00CLifecycleShutdownIdentity.Create(ordinal,"instance-"+ordinal,guid);var lease=state.Open(identity);state.Arm(lease);
        var ready=L00CLifecycleSessionObservation.Ready(run,iteration,ordinal,path,guid,isNew);state.BindReady(lease,ready);
        var reservation=state.PrepareReturn(ready,path);state.BeginNativeReturn(reservation);state.Close(lease);
        state.ConfirmSaveCommitted(reservation,L00CLifecycleSessionObservation.SaveCommitted(run,iteration,ordinal,path,guid,isNew),Proof(path,guid,true));
        return 1;
    }
    private static L00CNativeSaveQuitReturnProof Proof(string path,string guid,bool value)=>new(value,value,value,value,path,guid);
    private static string SavePath(string saves,string run,int iteration,char slot)=>Path.GetFullPath(Path.Combine(saves,"ISRWorldGen-L00C-"+run+"-iteration-"+iteration.ToString("D2")+"-"+slot+".vcdbs"));
    private static string GuidFor(int iteration,char slot)=>new Guid(iteration,(short)slot,0,new byte[]{1,2,3,4,5,6,7,8}).ToString("D");
    private static void Refuse(Action action,string label){try{action();}catch(ArgumentException){return;}catch(InvalidOperationException){return;}throw new InvalidOperationException("Expected lifecycle refusal: "+label);}
}
#endif
