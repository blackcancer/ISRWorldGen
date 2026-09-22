import argparse, json
from pathlib import Path
import export_continental_assembly as exporter
import verify_continental_assembly as verifier
ALGORITHM = "carrier-reconstruction-history-v1/material-bound-history-v3-carrier-resolved-origin-fluxes"
if __name__ == "__main__":
    p=argparse.ArgumentParser();p.add_argument("--root",type=Path);p.add_argument("--compare",type=Path,nargs=3)
    a=p.parse_args()
    if a.root:
        exporter.ALGORITHM=ALGORITHM
        exporter.base.RANGES.update({"elevation-model":(-14,18),"initial-elevation-model":(-14,18),"baseline-elevation-model":(-14,18),"baseline-height":(0,383)})
        exporter.export(a.root)
        # This is only a label correction: no image or raw field changes.
        f=a.root/"index.html"
        f.write_text(f.read_text().replace("transport au premier ordre", "transport par paquets reconstruits, concentrations amont constantes"),encoding="utf-8")
    elif a.compare:
        verifier.ALGORITHM=ALGORITHM
        verifier.material.ALGORITHM=ALGORITHM
        # verifier.verify normally reassigns its module from the wrapper constant.
        result=verifier.verify(a.compare[0],a.compare[1])
        with a.compare[2].open("x",encoding="utf-8") as f: json.dump(result,f,indent=2)
    else:p.error("--root or --compare required")
