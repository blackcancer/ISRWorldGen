#!/usr/bin/env python3
"""Complete-world raw fields and PNGs. NO image synthesizer/terrain retouching."""
import sys,json,hashlib,struct,math
from pathlib import Path
from export_tectonic_history import png,read_pixels

NAMES=("initial-height","height","legacy-height","thermal-only-height")
def load(d):
    d=Path(d);m=json.loads((d/"manifest.json").read_text(encoding="utf-8-sig"))
    complete=json.loads((d/"COMPLETE.json").read_text(encoding="utf-8-sig"))
    if complete["status"]!="EXECUTED_WORLD_NOT_GEOGRAPHIC_PASS" or complete["commit"]!=m["commit"]:
        raise ValueError("Incomplete/mismatched world")
    f={}
    for name,r in m["fields"].items():
        b=(d/r["path"]).read_bytes()
        if len(b)!=m["width"]*m["height"]*8 or hashlib.sha256(b).hexdigest()!=r["sha256"]:
            raise ValueError("Invalid field source "+name)
        v=[x[0] for x in struct.iter_unpack("<d",b)]
        if not all(math.isfinite(x) for x in v):raise ValueError("Nonfinite source "+name)
        f[name]=v
    return m,f
def export(d):
    d=Path(d);m,f=load(d)
    if (d/"png").exists():raise FileExistsError("Refuse PNG overwrite")
    ready={}
    for name in NAMES:
        a=f[name]
        if not all(0<=x<=383 for x in a):raise ValueError("Out-of-domain solid height, never clip")
        b=b"".join(struct.pack(">H",round(x*65535/383)) for x in a)
        ready[name+"-16bit.png"]=png(m["width"],m["height"],b,16,0,"TRUE solid Y=code*383/65535; includes seabed")
        if read_pixels(ready[name+"-16bit.png"])!=(m["width"],m["height"],16,0,b):raise ValueError("PNG roundtrip")
        ready[name+"-preview.png"]=png(m["width"],m["height"],bytes(round(x*255/383) for x in a for _ in range(3)),8,2,"Fixed Y0..383; no seabed mask")
    (d/"png").mkdir()
    for name,b in ready.items():(d/"png"/name).write_bytes(b)
def compare(root,out):
    out=Path(out)
    if out.exists():raise FileExistsError("Never overwrite comparison")
    records=[]
    for seed in (-437287116,20260906,73):
        for mode in ("control","coupled"):
            ds=[]
            for os in ("ubuntu-latest","windows-latest"):
                found=list(Path(root).glob(f"lithostatic-world-{os}-{mode}-{seed}-*"))
                if len(found)!=1:raise ValueError("Ambiguous/missing execution")
                receipt=json.loads((found[0]/"reuse.json").read_text(encoding="utf-8-sig"))
                if receipt["preserved"] is not True or receipt["nativeExitCode"]==0:raise ValueError("Refusal was not verified")
                ds.append(found[0]/"world")
            (ma,a),(mb,b)=[load(d) for d in ds]
            if ma["commit"]!=mb["commit"] or a.keys()!=b.keys() or ma["InitialMaterialChecksum"]!=mb["InitialMaterialChecksum"]:
                raise ValueError("Mismatch provenance")
            errors={}
            for name in a:
                errors[name]=max(abs(x-y) for x,y in zip(a[name],b[name]))
                if errors[name]>1e-8:raise ValueError("Platform field failure "+name)
            for name in NAMES:
                # Pixel identity, metadata/file timestamps excluded.
                if read_pixels((ds[0]/"png"/(name+"-16bit.png")).read_bytes())!=read_pixels((ds[1]/"png"/(name+"-16bit.png")).read_bytes()):
                    raise ValueError("PNG numeric pixel divergence")
            records.append(dict(seed=seed,mode=mode,errors=errors))
    out.write_text(json.dumps(dict(status="PASS_NUMERIC",records=records,geography="NOT_ACCEPTED",erosion="NOT_RUN"),indent=2))
if __name__=="__main__":
    if sys.argv[1]=="export":export(Path(sys.argv[2]))
    else:compare(Path(sys.argv[2]),Path(sys.argv[3]))
