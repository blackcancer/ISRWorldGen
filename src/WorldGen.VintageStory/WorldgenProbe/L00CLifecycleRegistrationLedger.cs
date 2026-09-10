#if DEBUG || L00C_STANDALONE_ORACLE
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ISRWorldGen.WorldgenProbe;

internal enum L00CLifecycleRegistrationKind { InitWorldGenerator, GameWorldSave, Tick }

internal sealed class L00CLifecycleRegistrationLedger
{
    private readonly object gate=new();
    private readonly Dictionary<L00CLifecycleRegistrationKind,L00CLifecycleRegistrationState> states=new();
    private readonly List<string> trace=new();
    private bool closing;

    internal void RecordRegistered(L00CLifecycleRegistrationKind kind)
    {
        lock(gate)
        {
            if(closing||states.ContainsKey(kind))throw new InvalidOperationException("L00-C lifecycle registration is duplicate or closing.");
            states.Add(kind,new L00CLifecycleRegistrationState(true,false,false));Trace(kind,"Registered","actual registration call returned");
        }
    }

    internal void BeginClosing()
    {
        lock(gate){if(closing)return;closing=true;Trace(null,"Closing","callbacks stop dispatch before external unregistration");}
    }

    internal void RecordReleased(L00CLifecycleRegistrationKind kind,bool independentlyUnregistered)
    {
        lock(gate)
        {
            if(!closing||!states.TryGetValue(kind,out L00CLifecycleRegistrationState state)||!state.Registered||state.OwnerReferenceReleased)throw new InvalidOperationException("L00-C lifecycle registration release is missing, duplicate, or precedes closing.");
            states[kind]=new L00CLifecycleRegistrationState(true,true,independentlyUnregistered);
            Trace(kind,"Released",independentlyUnregistered?"external registration removed and owner released":"non-removable registration trampoline released its owner");
        }
    }

    internal void RecordIgnoredCallback(L00CLifecycleRegistrationKind kind,string invariant)
    {
        lock(gate){Trace(kind,closing?"IgnoredDuringClosing":"IgnoredStaleOrDuplicate",RequireInvariant(invariant));}
    }

    internal L00CLifecycleRegistrationSnapshot Snapshot()
    {
        lock(gate)
        {
            var copy=new Dictionary<L00CLifecycleRegistrationKind,L00CLifecycleRegistrationState>(states);
            return new L00CLifecycleRegistrationSnapshot(closing,copy,new ReadOnlyCollection<string>(new List<string>(trace)));
        }
    }

    private void Trace(L00CLifecycleRegistrationKind? kind,string state,string invariant)
        => trace.Add("run=unbound iteration=0 session=0 state="+state+" invariant="+RequireInvariant(invariant)+" registration="+(kind?.ToString()??"All"));
    private static string RequireInvariant(string value)=>string.IsNullOrWhiteSpace(value)?throw new ArgumentException("L00-C lifecycle registration invariant is required.",nameof(value)):value.Replace('\r',' ').Replace('\n',' ');
}

internal readonly struct L00CLifecycleRegistrationState
{
    internal L00CLifecycleRegistrationState(bool registered,bool ownerReferenceReleased,bool independentlyUnregistered){Registered=registered;OwnerReferenceReleased=ownerReferenceReleased;IndependentlyUnregistered=independentlyUnregistered;}
    internal bool Registered { get; }
    internal bool OwnerReferenceReleased { get; }
    internal bool IndependentlyUnregistered { get; }
}

internal sealed class L00CLifecycleRegistrationSnapshot
{
    internal L00CLifecycleRegistrationSnapshot(bool closing,IReadOnlyDictionary<L00CLifecycleRegistrationKind,L00CLifecycleRegistrationState> states,IReadOnlyList<string> trace){Closing=closing;States=states;Trace=trace;}
    internal bool Closing { get; }
    internal IReadOnlyDictionary<L00CLifecycleRegistrationKind,L00CLifecycleRegistrationState> States { get; }
    internal IReadOnlyList<string> Trace { get; }
    internal bool Complete
    {
        get
        {
            if(!Closing||States.Count!=3)return false;
            foreach(L00CLifecycleRegistrationKind kind in (L00CLifecycleRegistrationKind[])Enum.GetValues(typeof(L00CLifecycleRegistrationKind)))
            {
                if(!States.TryGetValue(kind,out L00CLifecycleRegistrationState state)||!state.Registered||!state.OwnerReferenceReleased)return false;
                if(kind!=L00CLifecycleRegistrationKind.InitWorldGenerator&&!state.IndependentlyUnregistered)return false;
            }
            return true;
        }
    }
}
#endif
