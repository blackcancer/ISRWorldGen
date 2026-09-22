#!/usr/bin/env python3
"""Compare exact C# outputs. No height generation or image normalization."""
import argparse, hashlib, json, math, struct
from pathlib import Path

def main(root, out):
    report=[]
    for seed in (-437287116,20260906,73):
        matches={os: list(root.glob(f"spreading-{os}-latest-{seed}-*")) for os in ("ubuntu","windows")}
        assert all(len(v)==1 for v in matches.values()),matches
        left,right=matches["ubuntu"][0]/"experiment",matches["windows"][0]/"experiment"
        for d in (left,right):
            m=json.loads((d/"COMPLETE.json").read_text())
            assert m["seed"]==seed and m["checks"]==20
            assert not (d/"INCOMPLETE.json").exists() and not (d/"FAILED.json").exists()
        maximum=0;fields=0;pixels=0
        for stage in ("stage-000","stage-036"):
            a=json.loads((left/stage/"manifest.json").read_text())
            b=json.loads((right/stage/"manifest.json").read_text())
            assert a["fields"].keys()==b["fields"].keys()
            assert a["waterSurfacePresent"] is b["waterSurfacePresent"] is False
            assert a["seabedMasked"] is b["seabedMasked"] is False
            for field in a["fields"]:
                arrays=[]
                for d,m in ((left,a),(right,b)):
                    info=m["fields"][field]; raw=(d/stage/info["path"]).read_bytes()
                    assert hashlib.sha256(raw).hexdigest()==info["sha256"]
                    assert len(raw)==m["width"]*m["height"]*8
                    values=[q[0] for q in struct.iter_unpack("<d",raw)]
                    assert all(map(math.isfinite,values))
                    arrays.append(values)
                delta=max(abs(x-y) for x,y in zip(*arrays));maximum=max(maximum,delta);fields+=1
                assert delta<=1e-8,(seed,stage,field,delta)
                if field=="height":
                    assert all(0<=x<=383 for ar in arrays for x in ar)
                    assert all(round(x*65535/383)==round(y*65535/383) for x,y in zip(*arrays))
                    pixels+=len(arrays[0])
        report.append(dict(seed=seed,maximumDifference=maximum,fields=fields,matchingHeightCodes=pixels))
    out.write_text(json.dumps(dict(status="PASS",scope="CONTROLLED_PALEORIDGE_NUMERICAL_COMPARISON_NOT_GEOGRAPHY",
                                   records=report,fieldTolerance=1e-8),indent=2))
if __name__=="__main__":
    p=argparse.ArgumentParser();p.add_argument("root",type=Path);p.add_argument("out",type=Path)
    a=p.parse_args();main(a.root,a.out)
