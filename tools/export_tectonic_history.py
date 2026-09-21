#!/usr/bin/env python3
"""Loss-bounded actual C# elevation and material diagnostics; no generated imagery."""
import argparse, hashlib, html, json, math, struct
from pathlib import Path
from export_spatial_png import png, read_pixels

RANGES = {"height": (0, 383), "initial-height": (0, 383), "continental-thickness": (0, 150),
          "oceanic-thickness": (0, 60), "ocean-age": (0, 150), "new-ocean-fraction": (0, 1),
          "compression": (0, 5), "extension": (0, 5), "shear": (0, 5), "plates": (0, 31), "plate-confidence": (0, 1), "carrier-density": (0, 8)}

def export(root):
    cards, reports = [], []
    for directory in sorted(root.glob("seed-*")):
        m = json.loads((directory / "manifest.json").read_text(encoding="utf-8"))
        w, h = m["width"], m["height"]
        if m["waterSurfacePresent"] or m["seabedMasked"]: raise ValueError("Water substituted for solid terrain")
        out = directory / "png"
        out.mkdir(exist_ok=False)
        report = {}
        for name, field in m["fields"].items():
            raw = (directory / field["path"]).read_bytes()
            if len(raw) != w*h*8 or hashlib.sha256(raw).hexdigest() != field["sha256"]: raise ValueError("Field hash/size mismatch")
            values = [v for v, in struct.iter_unpack("<d", raw)]
            lo, hi = RANGES[name]
            # Saturation is never allowed on the true heightmap. Auxiliary strain
            # displays flag any overflow; their exact f64 data remains untouched.
            outside = sum(v < lo or v > hi for v in values)
            if name in ("height", "initial-height") and outside: raise ValueError("Height outside fixed encoding")
            codes = [round((min(hi, max(lo, v))-lo)*65535/(hi-lo)) for v in values]
            payload = b"".join(struct.pack(">H", q) for q in codes)
            encoded = png(w, h, payload, 16, 0, f"Actual C# field {name}; seed={m['seed']}; no water; range={lo}..{hi}")
            if read_pixels(encoded) != (w,h,16,0,payload): raise ValueError("PNG roundtrip mismatch")
            filename = name + "-16bit.png"
            (out / filename).write_bytes(encoded)
            preview = bytes(v for q in codes for v in (round(q * 255 / 65535),) * 3)
            (out / (name + "-preview.png")).write_bytes(png(w,h,preview,8,2,"Fixed-scale greys in RGB preview, not raw data"))
            error = max(abs(v-(lo+q*(hi-lo)/65535)) for v,q in zip(values,codes))
            report[name] = dict(pngSha256=hashlib.sha256(encoded).hexdigest(), sourceSha256=field["sha256"],
                                decode=f"{lo} + uint16 * {hi-lo} / 65535", outsideDisplayRange=outside, maxError=error)
            if name in ("height", "initial-height") and error > 383/131070 + 1e-12: raise ValueError("Height quantization mismatch")
        (out / "encoding.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
        reports.append(report)
        cards.append(f"<section><h2>Seed {m['seed']} — monde entier de 1 000 000 × 1 000 000 blocs</h2>"
          f"<p>{w} × {h} cellules réellement calculées ; pas = {1000000/w:g} blocs. "
          f"{m['largeLandComponents']} ensembles émergés ≥ 1 % de l'atlas, connexité périodique. Aucune acceptation géographique.</p>"
          f"<p><a href='{directory.name}/png/height-16bit.png'>Heightmap numérique 16 bits</a> · "
          f"<a href='{directory.name}/height.f64le'>Altitude exacte</a> · <a href='{directory.name}/history-ledger.json'>Bilan chronologique</a></p>"
          f"<h3>Avant mouvements</h3><img src='{directory.name}/png/initial-height-preview.png'>"
          f"<h3>Après évolution de la croûte — aucun ajout de massif</h3><img src='{directory.name}/png/height-preview.png'>"
          f"<details><summary>Épaisseur continentale et âge océanique réels</summary>"
          f"<img src='{directory.name}/png/continental-thickness-preview.png'><img src='{directory.name}/png/ocean-age-preview.png'></details></section>")
    page = """<!doctype html><html lang='fr'><meta charset='utf-8'><title>ISRWorldGen — histoire tectonique</title>
<style>body{font:17px system-ui;max-width:1100px;margin:30px auto;padding:15px;line-height:1.55}img{width:100%;image-rendering:pixelated}section{border-top:2px solid #999;padding-top:20px}h1,h2{line-height:1.25}</style>
<h1>Évolution matérielle tectonique — candidat non validé</h1><p>Les images montrent l'altitude solide, fonds marins compris. Noir Y=0, blanc Y=383, Y=168 est seulement une référence. Aucun masque d'eau, contraste automatique, érosion ni retouche géographique.</p>
<p>Le domaine tectonique de référence mesure 1 000 000 unités de côté. Les mondes de 131 072 et 262 144 blocs utilisent ce même atlas ENTIER à une échelle réduite, jamais un recadrage. Les diagnostics sont des calculs grossiers de socle, pas du détail à l'échelle du bloc.</p>
<p>Limites : cinématique imposée, domaine préparatoire périodique, transport au premier ordre, pas encore de mécanique de flexure des fosses/arcs. La conservation des volumes n'établit pas le réalisme du relief.</p>""" + ''.join(cards) + '</html>'
    (root / 'index.html').write_text(page, encoding='utf-8')
    (root / 'encoding-report.json').write_text(json.dumps(reports,indent=2), encoding='utf-8')
    print(f"PNG encoding verified for {len(reports)} full histories; geographic acceptance NOT_GRANTED")

if __name__ == '__main__':
    p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);a=p.parse_args();export(a.root)
