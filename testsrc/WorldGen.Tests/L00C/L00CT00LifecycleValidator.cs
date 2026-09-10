#if L00C_STANDALONE_ORACLE
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ISRWorldGen.L00C.Laboratory;

/// <summary>
/// Read-only external validator for T00-06 evidence. It deliberately has no
/// spatial input and never upgrades RUN_COMPLETED into a test PASS by itself.
/// </summary>
internal static class L00CT00LifecycleValidator
{
    private const string RunFileName="t00-06-run.json";
    private const string ObservationsFileName="t00-06-observations.jsonl";
    private const string RegistrationsFileName="t00-06-registrations.jsonl";

    internal static L00CT00LifecycleValidationReport Validate(string laboratoryRoot,string gamePathsSaves,string runId,int runtimeProcessId,string evidenceDirectory)
    {
        string root=CanonicalDirectory(laboratoryRoot,"laboratory root");string saves=CanonicalDirectory(gamePathsSaves,"GamePaths.Saves");
        string evidence=CanonicalDirectory(evidenceDirectory,"evidence directory");RequireRunId(runId);
        string campaign=Path.GetFullPath(Path.Combine(root,"campaigns",runId));
        if(!IsUnder(evidence,campaign)||!string.Equals(Path.GetFileName(evidence),"evidence",StringComparison.Ordinal))throw new L00CT00LifecycleValidationException("L00C_T00_06_EVIDENCE_PATH_INVALID",0,0,"evidence must be the exact campaign evidence directory");
        if(runtimeProcessId<=0)throw new L00CT00LifecycleValidationException("L00C_T00_06_PROCESS_ID_INVALID",0,0,"runtime process identity must be positive");

        string runPath=ExactEvidenceFile(evidence,RunFileName),observationsPath=ExactEvidenceFile(evidence,ObservationsFileName),registrationsPath=ExactEvidenceFile(evidence,RegistrationsFileName);
        L00CStrictEvidenceJson run=L00CStrictEvidenceJson.Parse(File.ReadAllText(runPath));
        run.Exactly("schema","runId","outcome","iterations","sessions","dedicatedSaves","runtimeProcessId","timedOut","partialStop","staleCallbacks","duplicateCallbacks","closingCallbacks","unregistrationFailures","provenanceSha256","observationsSha256","registrationsSha256");
        Require(run.StringValue("schema")=="l00c-t00-06-run-v1","L00C_T00_06_RUN_SCHEMA_INVALID",0,0,"run schema");
        Require(run.StringValue("runId")==runId,"L00C_T00_06_RUN_ID_MISMATCH",0,0,"run provenance");
        Require(run.StringValue("outcome")=="RUN_COMPLETED","L00C_T00_06_RUN_NOT_COMPLETED",0,0,"RUN_COMPLETED required but never sufficient");
        Require(run.Int("iterations")==5&&run.Int("sessions")==15&&run.Int("dedicatedSaves")==10,"L00C_T00_06_CARDINALITY_INVALID",0,0,"exact 5 iterations, 15 sessions, and 10 saves required");
        Require(run.Int("runtimeProcessId")==runtimeProcessId,"L00C_T00_06_PROCESS_ID_MISMATCH",0,0,"runtime process provenance");
        Require(!run.Bool("timedOut"),"L00C_T00_06_TIMEOUT",0,0,"terminal timeout is monotonic and rejects completion");
        Require(!run.Bool("partialStop"),"L00C_T00_06_PARTIAL_STOP",0,0,"partial native stop rejects completion");
        Require(run.Int("staleCallbacks")==0,"L00C_T00_06_STALE_CALLBACK",0,0,"stale callbacks must be zero");
        Require(run.Int("duplicateCallbacks")==0,"L00C_T00_06_DUPLICATE_CALLBACK",0,0,"duplicate callbacks must be zero");
        Require(run.Int("closingCallbacks")==0,"L00C_T00_06_EVENT_DURING_CLOSING",0,0,"events during closing must be zero");
        Require(run.Int("unregistrationFailures")==0,"L00C_T00_06_UNREGISTRATION_FAILURE",0,0,"unregistration failures must be zero");
        Require(run.StringValue("observationsSha256")==Hash(observationsPath),"L00C_T00_06_OBSERVATIONS_TAMPERED",0,0,"observation file hash");
        Require(run.StringValue("registrationsSha256")==Hash(registrationsPath),"L00C_T00_06_REGISTRATIONS_TAMPERED",0,0,"registration file hash");
        string provenancePath=Path.Combine(campaign,"campaign-provenance.json");
        Require(File.Exists(provenancePath)&&run.StringValue("provenanceSha256")==Hash(provenancePath),"L00C_T00_06_PROVENANCE_TAMPERED",0,0,"campaign provenance hash");

        List<Observation> observations=ReadObservations(observationsPath,runId,saves);
        ValidateSessionAndMarkerInvariants(observations,runId,saves);
        ValidateRegistrations(registrationsPath,runId);
        try{L00CCampaignStorage.ValidateCompletedLifecycleCleanup(root,saves,runId,runtimeProcessId);}
        catch(Exception exception){throw new L00CT00LifecycleValidationException("L00C_T00_06_CLEANUP_INCOMPLETE",0,0,"exact sealed cleanup, markers, sidecars, paths, provenance, and interruption state",exception);}
        return new L00CT00LifecycleValidationReport(runId,5,15,10,30,90);
    }

    private static List<Observation> ReadObservations(string path,string runId,string saves)
    {
        string[] lines=File.ReadAllLines(path);Require(lines.Length==30,"L00C_T00_06_OBSERVATION_COUNT_INVALID",0,0,"exact Ready and SaveCommitted event for fifteen sessions");
        var result=new List<Observation>(30);
        for(int index=0;index<lines.Length;index++)
        {
            Require(!string.IsNullOrWhiteSpace(lines[index]),"L00C_T00_06_OBSERVATION_EMPTY",0,0,"no blank observation");
            var json=L00CStrictEvidenceJson.Parse(lines[index]);
            json.Exactly("schema","runId","iteration","sessionOrdinal","canonicalSavePath","canonicalSavegameGuid","isNew","eventKind","state","invariant","markerId","markerOpenCount","markerEvidenceKind","nativeActionCompleted","serverStopped","mainMenuReady","targetExclusivelyOpenable");
            int iteration=json.Int("iteration"),session=json.Int("sessionOrdinal");string eventKind=json.StringValue("eventKind");
            Require(json.StringValue("schema")=="l00c-t00-06-observation-v1"&&json.StringValue("runId")==runId,"L00C_T00_06_OBSERVATION_PROVENANCE_INVALID",iteration,session,"observation schema/run");
            Require(iteration>=1&&iteration<=5&&session>=1&&session<=15,"L00C_T00_06_OBSERVATION_IDENTITY_INVALID",iteration,session,"bounded iteration/session");
            string savePath=json.StringValue("canonicalSavePath");Require(Path.IsPathRooted(savePath)&&string.Equals(savePath,Path.GetFullPath(savePath),StringComparison.OrdinalIgnoreCase)&&IsUnder(savePath,saves),"L00C_T00_06_SAVE_PATH_INVALID",iteration,session,"canonical direct save path");
            string guid=json.StringValue("canonicalSavegameGuid");Require(Guid.TryParseExact(guid,"D",out Guid parsed)&&guid==parsed.ToString("D"),"L00C_T00_06_GUID_INVALID",iteration,session,"canonical save GUID");
            string state=json.StringValue("state"),invariant=json.StringValue("invariant");Require(!string.IsNullOrWhiteSpace(invariant),"L00C_T00_06_INVARIANT_MISSING",iteration,session,"event diagnostic invariant");
            Require((eventKind=="Ready"&&state=="Ready")||(eventKind=="SaveCommitted"&&state=="SaveCommitted"),"L00C_T00_06_EVENT_STATE_INVALID",iteration,session,"event diagnostic state");
            bool native=json.Bool("nativeActionCompleted"),stopped=json.Bool("serverStopped"),menu=json.Bool("mainMenuReady"),exclusive=json.Bool("targetExclusivelyOpenable");
            string markerEvidence=json.StringValue("markerEvidenceKind");bool isNew=json.Bool("isNew");
            if(eventKind=="Ready")
            {
                Require(!native&&!stopped&&!menu&&!exclusive,"L00C_T00_06_READY_CLAIMS_COMMIT",iteration,session,"Ready is not native close proof");
                Require(markerEvidence==(isNew?"InternalStoreCreated":"InternalStoreReRead"),"L00C_T00_06_INTERNAL_MARKER_PROOF_MISSING",iteration,session,"external ownership guard cannot substitute for SaveGame internal marker creation/re-read");
            }
            else
            {
                Require(native&&stopped&&menu&&exclusive,"L00C_T00_06_NATIVE_CLOSE_PROOF_MISSING",iteration,session,"SaveCommitted requires all native return proofs");
                Require(markerEvidence=="InternalStoreCaptured","L00C_T00_06_INTERNAL_MARKER_PROOF_MISSING",iteration,session,"SaveCommitted must retain the captured internal SaveGame marker proof");
            }
            string marker=json.StringValue("markerId");Require(marker.Length==32&&marker.All(IsLowerHex),"L00C_T00_06_MARKER_INVALID",iteration,session,"internal marker identity");
            result.Add(new Observation(iteration,session,savePath,guid,isNew,eventKind,marker,json.Int("markerOpenCount")));
        }
        return result;
    }

    private static void ValidateSessionAndMarkerInvariants(IReadOnlyList<Observation> observations,string runId,string saves)
    {
        var saveGuids=new HashSet<string>(StringComparer.Ordinal);var markerIds=new HashSet<string>(StringComparer.Ordinal);var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for(int iteration=1;iteration<=5;iteration++)
        {
            int aCreate=(iteration-1)*3+1,aReopen=aCreate+1,bCreate=aCreate+2;
            Observation a1=ExactPair(observations,iteration,aCreate,ExpectedPath(saves,runId,iteration,'a'),true);
            Observation a2=ExactPair(observations,iteration,aReopen,ExpectedPath(saves,runId,iteration,'a'),false);
            Observation b=ExactPair(observations,iteration,bCreate,ExpectedPath(saves,runId,iteration,'b'),true);
            Require(a1.Guid==a2.Guid&&a1.MarkerId==a2.MarkerId&&a1.MarkerOpenCount==1&&a2.MarkerOpenCount==2,"L00C_T00_06_A_REOPEN_INVALID",iteration,aReopen,"A path/GUID/marker stable and counter 1 then 2");
            Require(b.MarkerOpenCount==1&&b.Guid!=a1.Guid&&b.MarkerId!=a1.MarkerId,"L00C_T00_06_B_ISOLATION_INVALID",iteration,bCreate,"B is a distinct new save with counter 1");
            Require(paths.Add(a1.Path)&&paths.Add(b.Path)&&saveGuids.Add(a1.Guid)&&saveGuids.Add(b.Guid)&&markerIds.Add(a1.MarkerId)&&markerIds.Add(b.MarkerId),"L00C_T00_06_CROSS_ITERATION_COLLISION",iteration,bCreate,"ten paths/GUIDs/internal markers must be distinct");
        }
        Require(paths.Count==10&&saveGuids.Count==10&&markerIds.Count==10,"L00C_T00_06_TEN_SAVE_IDENTITY_INVALID",0,0,"ten distinct save identities");
    }

    private static Observation ExactPair(IReadOnlyList<Observation> observations,int iteration,int session,string path,bool isNew)
    {
        int offset=(session-1)*2;Require(offset+1<observations.Count,"L00C_T00_06_SESSION_MISSING",iteration,session,"ordered session pair");
        Observation ready=observations[offset],committed=observations[offset+1];
        Require(ready.Iteration==iteration&&committed.Iteration==iteration&&ready.Session==session&&committed.Session==session&&ready.EventKind=="Ready"&&committed.EventKind=="SaveCommitted","L00C_T00_06_EVENT_ORDER_INVALID",iteration,session,"one Ready then one SaveCommitted");
        Require(string.Equals(ready.Path,path,StringComparison.OrdinalIgnoreCase)&&string.Equals(committed.Path,path,StringComparison.OrdinalIgnoreCase)&&ready.Guid==committed.Guid&&ready.IsNew==isNew&&committed.IsNew==isNew&&ready.MarkerId==committed.MarkerId&&ready.MarkerOpenCount==committed.MarkerOpenCount,"L00C_T00_06_CAPTURED_SESSION_MISMATCH",iteration,session,"SaveCommitted must retain captured Ready identity");
        return ready;
    }

    private static void ValidateRegistrations(string path,string runId)
    {
        string[] lines=File.ReadAllLines(path);Require(lines.Length==90,"L00C_T00_06_REGISTRATION_COUNT_INVALID",0,0,"six registration/release events per session");
        string[] kinds={"InitWorldGenerator","GameWorldSave","Tick"};int index=0;
        for(int session=1;session<=15;session++)
        {
            int iteration=(session-1)/3+1;
            foreach(string phase in new[]{"Registered","Released"})foreach(string kind in kinds)
            {
                var json=L00CStrictEvidenceJson.Parse(lines[index++]);json.Exactly("schema","runId","iteration","sessionOrdinal","registration","eventKind","state","invariant","ownerReferenceReleased","independentlyUnregistered");
                Require(json.StringValue("schema")=="l00c-t00-06-registration-v1"&&json.StringValue("runId")==runId&&json.Int("iteration")==iteration&&json.Int("sessionOrdinal")==session&&json.StringValue("registration")==kind&&json.StringValue("eventKind")==phase&&json.StringValue("state")==phase&&!string.IsNullOrWhiteSpace(json.StringValue("invariant")),"L00C_T00_06_REGISTRATION_IDENTITY_INVALID",iteration,session,"registration diagnostic exact order/identity/state/invariant");
                bool released=json.Bool("ownerReferenceReleased"),unregistered=json.Bool("independentlyUnregistered");
                if(phase=="Registered")Require(!released&&!unregistered,"L00C_T00_06_REGISTRATION_PREMATURE_RELEASE",iteration,session,"registration flags false before release");
                else Require(released&&(kind=="InitWorldGenerator"?!unregistered:unregistered),"L00C_T00_06_REGISTRATION_RELEASE_MISSING",iteration,session,"owner released; removable event/listener independently unregistered");
            }
        }
    }

    private static string ExpectedPath(string saves,string runId,int iteration,char slot)=>Path.GetFullPath(Path.Combine(saves,"ISRWorldGen-L00C-"+runId+"-iteration-"+iteration.ToString("D2")+"-"+slot+".vcdbs"));
    private static string ExactEvidenceFile(string directory,string name){string path=Path.GetFullPath(Path.Combine(directory,name));if(!File.Exists(path)||Directory.Exists(path))throw new L00CT00LifecycleValidationException("L00C_T00_06_EVIDENCE_FILE_MISSING",0,0,name);return path;}
    private static string CanonicalDirectory(string path,string label){if(string.IsNullOrWhiteSpace(path)||!Path.IsPathRooted(path))throw new L00CT00LifecycleValidationException("L00C_T00_06_DIRECTORY_INVALID",0,0,label);string full=Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);if(!string.Equals(path.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),full,StringComparison.OrdinalIgnoreCase)||!Directory.Exists(full))throw new L00CT00LifecycleValidationException("L00C_T00_06_DIRECTORY_INVALID",0,0,label);return full;}
    private static bool IsUnder(string path,string parent)=>string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)),Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),StringComparison.OrdinalIgnoreCase);
    private static void RequireRunId(string value){if(value is null||value.Length!=32||!value.All(IsLowerHex))throw new L00CT00LifecycleValidationException("L00C_T00_06_RUN_ID_INVALID",0,0,"32 lower-case hex");}
    private static bool IsLowerHex(char value)=>(value>='0'&&value<='9')||(value>='a'&&value<='f');
    private static string Hash(string path){using SHA256 hash=SHA256.Create();using FileStream stream=new(path,FileMode.Open,FileAccess.Read,FileShare.Read);byte[] bytes=hash.ComputeHash(stream);var text=new StringBuilder(64);foreach(byte value in bytes)text.Append(value.ToString("X2"));return text.ToString();}
    private static void Require(bool condition,string code,int iteration,int session,string invariant){if(!condition)throw new L00CT00LifecycleValidationException(code,iteration,session,invariant);}

    private sealed class Observation
    {
        internal Observation(int iteration,int session,string path,string guid,bool isNew,string eventKind,string markerId,int markerOpenCount){Iteration=iteration;Session=session;Path=path;Guid=guid;IsNew=isNew;EventKind=eventKind;MarkerId=markerId;MarkerOpenCount=markerOpenCount;}
        internal int Iteration{get;}internal int Session{get;}internal string Path{get;}internal string Guid{get;}internal bool IsNew{get;}internal string EventKind{get;}internal string MarkerId{get;}internal int MarkerOpenCount{get;}
    }
}

internal sealed class L00CT00LifecycleValidationReport
{
    internal L00CT00LifecycleValidationReport(string runId,int iterations,int sessions,int saves,int observations,int registrationEvents){RunId=runId;Iterations=iterations;Sessions=sessions;DedicatedSaves=saves;Observations=observations;RegistrationEvents=registrationEvents;}
    internal string RunId{get;}internal int Iterations{get;}internal int Sessions{get;}internal int DedicatedSaves{get;}internal int Observations{get;}internal int RegistrationEvents{get;}
}

internal sealed class L00CT00LifecycleValidationException:InvalidOperationException
{
    internal L00CT00LifecycleValidationException(string code,int iteration,int session,string invariant,Exception? inner=null):base("["+code+"] run=evidence iteration="+iteration+" session="+session+" state=Rejected invariant="+invariant,inner){Code=code;Iteration=iteration;Session=session;Invariant=invariant;}
    internal string Code{get;}internal int Iteration{get;}internal int Session{get;}internal string Invariant{get;}
}
#endif
