#if L00C_STANDALONE_ORACLE
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ISRWorldGen.L00C.Laboratory;

internal static class L00CT00LifecycleValidatorOracle
{
    internal static int Run()
    {
        string temp=Path.Combine(Path.GetTempPath(),"l00c-t00-06-validator-"+Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            Fixture valid=CreateFixture(temp,"valid");
            L00CT00LifecycleValidationReport report=Validate(valid);
            Check(report.Iterations==5&&report.Sessions==15&&report.DedicatedSaves==10&&report.Observations==30&&report.RegistrationEvents==90);

            RejectRunMutation(temp,"timeout","\"timedOut\":false","\"timedOut\":true");
            RejectRunMutation(temp,"partial-stop","\"partialStop\":false","\"partialStop\":true");
            RejectRunMutation(temp,"partial-run","\"outcome\":\"RUN_COMPLETED\"","\"outcome\":\"STOPPED\"");
            RejectRunMutation(temp,"stale-counter","\"staleCallbacks\":0","\"staleCallbacks\":1");
            RejectRunMutation(temp,"duplicate-counter","\"duplicateCallbacks\":0","\"duplicateCallbacks\":1");
            RejectRunMutation(temp,"closing-counter","\"closingCallbacks\":0","\"closingCallbacks\":1");
            RejectRunMutation(temp,"unregister-counter","\"unregistrationFailures\":0","\"unregistrationFailures\":1");

            RejectObservationMutation(temp,"missing-observation",lines=>lines.Take(29).ToArray());
            RejectObservationMutation(temp,"duplicate-observation",lines=>lines.Concat(new[]{lines[0]}).ToArray());
            RejectObservationMutation(temp,"stale-path",lines=>ReplaceLine(lines,0,"iteration-01-a.vcdbs","iteration-01-b.vcdbs"));
            RejectObservationMutation(temp,"closing-event",lines=>ReplaceLine(lines,1,"\"state\":\"SaveCommitted\"","\"state\":\"Closing\""));
            RejectObservationMutation(temp,"partial-native",lines=>ReplaceLine(lines,1,"\"serverStopped\":true","\"serverStopped\":false"));
            RejectObservationMutationCode(temp,"external-guard-not-game-proof",lines=>ReplaceLine(lines,0,"\"markerEvidenceKind\":\"InternalStoreCreated\"","\"markerEvidenceKind\":\"ExternalOwnershipMarker\""),"L00C_T00_06_INTERNAL_MARKER_PROOF_MISSING");
            RejectObservationMutationCode(temp,"missing-internal-marker",lines=>ReplaceLine(lines,0,"\"markerId\":\"00000000000000000000000000000001\"","\"markerId\":\"\""),"L00C_T00_06_MARKER_INVALID");
            RejectObservationMutationCode(temp,"corrupt-internal-marker",lines=>ReplaceLine(lines,0,"\"markerId\":\"00000000000000000000000000000001\"","\"markerId\":\"zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz\""),"L00C_T00_06_MARKER_INVALID");
            RejectObservationMutationCode(temp,"copied-internal-marker",lines=>ReplaceLines(lines,new[]{4,5},"\"markerId\":\"00000000000000000000000000000002\"","\"markerId\":\"00000000000000000000000000000001\""),"L00C_T00_06_B_ISOLATION_INVALID");
            RejectObservationMutation(temp,"marker-reread",lines=>ReplaceLine(lines,2,"\"markerOpenCount\":2","\"markerOpenCount\":1"));
            RejectObservationMutation(temp,"save-collision",lines=>ReplaceLine(lines,4,"iteration-01-b.vcdbs","iteration-01-a.vcdbs"));
            RejectRegistrationMutation(temp,"missing-release",lines=>lines.Take(89).ToArray());
            RejectRegistrationMutation(temp,"failed-release",lines=>ReplaceLine(lines,4,"\"independentlyUnregistered\":true","\"independentlyUnregistered\":false"));

            Fixture missingReceipt=CreateFixture(temp,"missing-cleanup-receipt");File.Delete(missingReceipt.Storage.CleanupReceiptPath);Refuse(()=>Validate(missingReceipt));
            Fixture corruptReceipt=CreateFixture(temp,"corrupt-cleanup-receipt");File.AppendAllText(corruptReceipt.Storage.CleanupReceiptPath," ");Refuse(()=>Validate(corruptReceipt));
            Fixture copiedReceiptMarkerHash=CreateFixture(temp,"copied-receipt-marker-hash");string receipt=File.ReadAllText(copiedReceiptMarkerHash.Storage.CleanupReceiptPath);string first=ReadField(receipt,"artifact03Sha256"),second=ReadField(receipt,"artifact07Sha256");File.WriteAllText(copiedReceiptMarkerHash.Storage.CleanupReceiptPath,receipt.Replace("\"artifact07Sha256\":\""+second+"\"","\"artifact07Sha256\":\""+first+"\""));Refuse(()=>Validate(copiedReceiptMarkerHash));
            Fixture provenance=CreateFixture(temp,"provenance");File.AppendAllText(provenance.Storage.ProvenancePath," ");Refuse(()=>Validate(provenance));
            return 0;
        }
        finally{if(Directory.Exists(temp))Directory.Delete(temp,true);}
    }

    private static Fixture CreateFixture(string rootName,string label)
    {
        string container=Path.Combine(rootName,label+"-"+Guid.NewGuid().ToString("N"));string root=Path.Combine(container,"repo",".local","L00C"),saves=Path.Combine(container,"saves");Directory.CreateDirectory(root);Directory.CreateDirectory(saves);
        string runId=Guid.NewGuid().ToString("N");var storage=L00CCampaignStorage.CreateForRun(root,saves,runId);
        foreach(L00CCampaignSaveTarget target in storage.SaveTargets){storage.PrepareNativeCreate(target.Role,target.CanonicalSavePath);WriteNative(target.CanonicalSavePath,target.Role);storage.PublishFixtureMarker(target.Role,target.CanonicalSavePath);}storage.BeginCycling();
        string observations=Path.Combine(storage.EvidenceDirectory,"t00-06-observations.jsonl"),registrations=Path.Combine(storage.EvidenceDirectory,"t00-06-registrations.jsonl");
        File.WriteAllLines(observations,ObservationLines(storage));File.WriteAllLines(registrations,RegistrationLines(runId));
        WriteRun(storage,observations,registrations);
        storage.SealForExternalCleanup();int pid=Process.GetCurrentProcess().Id;L00CCampaignStorage.CleanupAfterRuntimeStopped(root,saves,runId,pid,()=>true);
        return new Fixture(root,saves,runId,pid,storage,observations,registrations);
    }

    private static string[] ObservationLines(L00CCampaignStorage storage)
    {
        var lines=new List<string>(30);
        for(int iteration=1;iteration<=5;iteration++)
        {
            string aGuid=GuidFor(iteration,'a'),bGuid=GuidFor(iteration,'b'),aMarker=(iteration*2-1).ToString("x32"),bMarker=(iteration*2).ToString("x32");
            AddPair(lines,storage.RunId,iteration,(iteration-1)*3+1,storage.SavePath(iteration,'a'),aGuid,true,aMarker,1);
            AddPair(lines,storage.RunId,iteration,(iteration-1)*3+2,storage.SavePath(iteration,'a'),aGuid,false,aMarker,2);
            AddPair(lines,storage.RunId,iteration,iteration*3,storage.SavePath(iteration,'b'),bGuid,true,bMarker,1);
        }
        return lines.ToArray();
    }
    private static void AddPair(List<string> lines,string runId,int iteration,int session,string path,string guid,bool isNew,string marker,int open)
    {
        lines.Add(Observation(runId,iteration,session,path,guid,isNew,"Ready",marker,open,isNew?"InternalStoreCreated":"InternalStoreReRead",false));
        lines.Add(Observation(runId,iteration,session,path,guid,isNew,"SaveCommitted",marker,open,"InternalStoreCaptured",true));
    }
    private static string Observation(string runId,int iteration,int session,string path,string guid,bool isNew,string eventKind,string marker,int open,string markerEvidence,bool committed)
        =>"{\"schema\":\"l00c-t00-06-observation-v1\",\"runId\":\""+runId+"\",\"iteration\":"+iteration+",\"sessionOrdinal\":"+session+",\"canonicalSavePath\":\""+Esc(path)+"\",\"canonicalSavegameGuid\":\""+guid+"\",\"isNew\":"+Bool(isNew)+",\"eventKind\":\""+eventKind+"\",\"state\":\""+eventKind+"\",\"invariant\":\"exact captured session\",\"markerId\":\""+marker+"\",\"markerOpenCount\":"+open+",\"markerEvidenceKind\":\""+markerEvidence+"\",\"nativeActionCompleted\":"+Bool(committed)+",\"serverStopped\":"+Bool(committed)+",\"mainMenuReady\":"+Bool(committed)+",\"targetExclusivelyOpenable\":"+Bool(committed)+"}";

    private static string[] RegistrationLines(string runId)
    {
        var lines=new List<string>(90);string[] kinds={"InitWorldGenerator","GameWorldSave","Tick"};
        for(int session=1;session<=15;session++)foreach(string phase in new[]{"Registered","Released"})foreach(string kind in kinds)
        {
            bool released=phase=="Released",unregistered=released&&kind!="InitWorldGenerator";int iteration=(session-1)/3+1;
            lines.Add("{\"schema\":\"l00c-t00-06-registration-v1\",\"runId\":\""+runId+"\",\"iteration\":"+iteration+",\"sessionOrdinal\":"+session+",\"registration\":\""+kind+"\",\"eventKind\":\""+phase+"\",\"state\":\""+phase+"\",\"invariant\":\"actual registration and independent release\",\"ownerReferenceReleased\":"+Bool(released)+",\"independentlyUnregistered\":"+Bool(unregistered)+"}");
        }
        return lines.ToArray();
    }

    private static void WriteRun(L00CCampaignStorage storage,string observations,string registrations)
    {
        string path=Path.Combine(storage.EvidenceDirectory,"t00-06-run.json");
        File.WriteAllText(path,"{\"schema\":\"l00c-t00-06-run-v1\",\"runId\":\""+storage.RunId+"\",\"outcome\":\"RUN_COMPLETED\",\"iterations\":5,\"sessions\":15,\"dedicatedSaves\":10,\"runtimeProcessId\":"+Process.GetCurrentProcess().Id+",\"timedOut\":false,\"partialStop\":false,\"staleCallbacks\":0,\"duplicateCallbacks\":0,\"closingCallbacks\":0,\"unregistrationFailures\":0,\"provenanceSha256\":\""+Hash(storage.ProvenancePath)+"\",\"observationsSha256\":\""+Hash(observations)+"\",\"registrationsSha256\":\""+Hash(registrations)+"\"}");
    }
    private static void RefreshRunHashes(Fixture fixture)=>WriteRun(fixture.Storage,fixture.ObservationsPath,fixture.RegistrationsPath);
    private static L00CT00LifecycleValidationReport Validate(Fixture fixture)=>L00CT00LifecycleValidator.Validate(fixture.Root,fixture.Saves,fixture.RunId,fixture.ProcessId,fixture.Storage.EvidenceDirectory);
    private static void RejectRunMutation(string temp,string label,string from,string to){Fixture x=CreateFixture(temp,label);string p=Path.Combine(x.Storage.EvidenceDirectory,"t00-06-run.json");File.WriteAllText(p,File.ReadAllText(p).Replace(from,to));Refuse(()=>Validate(x));}
    private static void RejectObservationMutation(string temp,string label,Func<string[],string[]> mutate){Fixture x=CreateFixture(temp,label);File.WriteAllLines(x.ObservationsPath,mutate(File.ReadAllLines(x.ObservationsPath)));RefreshRunHashes(x);Refuse(()=>Validate(x));}
    private static void RejectObservationMutationCode(string temp,string label,Func<string[],string[]> mutate,string code){Fixture x=CreateFixture(temp,label);File.WriteAllLines(x.ObservationsPath,mutate(File.ReadAllLines(x.ObservationsPath)));RefreshRunHashes(x);RefuseCode(()=>Validate(x),code);}
    private static void RejectRegistrationMutation(string temp,string label,Func<string[],string[]> mutate){Fixture x=CreateFixture(temp,label);File.WriteAllLines(x.RegistrationsPath,mutate(File.ReadAllLines(x.RegistrationsPath)));RefreshRunHashes(x);Refuse(()=>Validate(x));}
    private static string[] ReplaceLine(string[] lines,int index,string from,string to){string[] copy=(string[])lines.Clone();copy[index]=copy[index].Replace(from,to);return copy;}
    private static string[] ReplaceLines(string[] lines,int[] indices,string from,string to){string[] copy=(string[])lines.Clone();foreach(int index in indices)copy[index]=copy[index].Replace(from,to);return copy;}
    private static void WriteNative(string path,string text){File.WriteAllText(path,text);File.WriteAllText(L00CCampaignStorage.WalPath(path),text+"-wal");File.WriteAllText(L00CCampaignStorage.ShmPath(path),text+"-shm");}
    private static string GuidFor(int iteration,char slot)=>new Guid(iteration,(short)slot,0,new byte[]{1,2,3,4,5,6,7,8}).ToString("D");
    private static string Bool(bool value)=>value?"true":"false";
    private static string Esc(string value)=>value.Replace("\\","\\\\").Replace("\"","\\\"");
    private static string Hash(string path){using SHA256 hash=SHA256.Create();using FileStream stream=File.OpenRead(path);byte[] bytes=hash.ComputeHash(stream);var text=new StringBuilder(64);foreach(byte value in bytes)text.Append(value.ToString("X2"));return text.ToString();}
    private static string ReadField(string json,string name){string start="\""+name+"\":\"";int index=json.IndexOf(start,StringComparison.Ordinal);if(index<0)throw new InvalidOperationException("field absent");index+=start.Length;int end=json.IndexOf('"',index);return json.Substring(index,end-index);}
    private static void Refuse(Action action){try{action();}catch(InvalidOperationException){return;}throw new InvalidOperationException("L00-C T00-06 validator oracle expected refusal.");}
    private static void RefuseCode(Action action,string code){try{action();}catch(L00CT00LifecycleValidationException exception)when(exception.Code==code){return;}throw new InvalidOperationException("L00-C T00-06 validator oracle expected refusal code "+code+".");}
    private static void Check(bool value){if(!value)throw new InvalidOperationException("L00-C T00-06 validator oracle failed.");}
    private sealed class Fixture
    {
        internal Fixture(string root,string saves,string runId,int processId,L00CCampaignStorage storage,string observations,string registrations){Root=root;Saves=saves;RunId=runId;ProcessId=processId;Storage=storage;ObservationsPath=observations;RegistrationsPath=registrations;}
        internal string Root{get;}internal string Saves{get;}internal string RunId{get;}internal int ProcessId{get;}internal L00CCampaignStorage Storage{get;}internal string ObservationsPath{get;}internal string RegistrationsPath{get;}
    }
}
#endif
