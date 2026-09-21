#!/usr/bin/env python3
"""True unmasked scalar PNG16 and auxiliary previews from actual C# bedrock."""
import argparse, hashlib, html, json, math, struct
from pathlib import Path
from export_spatial_png import png, read_pixels, check
STOPS=[(0,(10,15,35)),(88,(15,28,66)),(112,(30,64,108)),(136,(67,112,153)),(154,(126,159,145)),(168,(90,143,96)),(192,(147,167,94)),(216,(205,191,126)),(248,(166,127,87)),(280,(131,121,113)),(320,(195,192,186)),(352,(235,233,226)),(383,(255,255,255))]
def colour(y):
    for (a,ca),(b,cb) in zip(STOPS,STOPS[1:]):
        if a<=y<=b:
            t=(y-a)/(b-a);return bytes(round(x+(z-x)*t) for x,z in zip(ca,cb))
    return bytes(STOPS[-1][1])
def export(d):
    m=json.loads((d/'manifest.json').read_text());source=(d/'height.f64le').read_bytes()
    check(m['scope']=='UNERODED_RAW_BEDROCK_HEIGHT' and m['seabedMasked'] is False and m['waterSurfacePresent'] is False,'Not raw bedrock')
    check(m['erosion']=='NOT_RUN_GATED_ON_RELIEF_REVIEW' and m['geographicAcceptance']=='PENDING_EARTH_REFERENCE_REVIEW','Wrong qualification scope')
    check(hashlib.sha256(source).hexdigest()==m['fieldsSha256'],'Source hash mismatch')
    w,h=m['width'],m['height'];check(0<w<=2048 and 0<h<=2048 and len(source)==8*w*h and m['worldHeightBlocks']==384,'Invalid raster')
    a=[v for v, in struct.iter_unpack('<d',source)]
    check(all(math.isfinite(v) and 0<=v<=383 for v in a),'Out of fixed range; no clamping allowed')
    q=[round(v*65535/383) for v in a]
    error=max(abs(v-code*383/65535) for v,code in zip(a,q))
    check(error<=383/131070+1e-12,'Quantization failed')
    out=d/'png';out.mkdir(exist_ok=False);files=[]
    note=f"UNERODED SOLID HEIGHT including seafloor; no water; seed={m['seed']}; commit={m['commit']}"
    def save(name,data,depth,kind):
        encoded=png(w,h,data,depth,kind,note)
        check(read_pixels(encoded)==(w,h,depth,kind,data),'Pixel roundtrip failed')
        (out/name).write_bytes(encoded);files.append(dict(path=name,sha256=hashlib.sha256(encoded).hexdigest(),bitDepth=depth,width=w,height=h))
    save('heightmap-16bit.png',b''.join(struct.pack('>H',v) for v in q),16,0)
    save('heightmap-grey-preview.png',b''.join(bytes([round(v*255/383)])*3 for v in a),8,2)
    save('heightmap-hypsometric.png',b''.join(colour(v) for v in a),8,2)
    paths=[]
    for row in (h//3,2*h//3):
        points=' '.join(f'{60+1100*i/(w-1):.3f},{430-v:.3f}' for i,v in enumerate(a[row*w:(row+1)*w]))
        paths.append(f'<polyline fill="none" stroke="{("#333" if row==h//3 else "#a45926")}" stroke-width="1.5" points="{points}"/>')
    svg='<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="480"><rect width="1200" height="480" fill="white"/><g font-family="sans-serif" font-size="14"><text x="60" y="24">Coupes du monde entier : Z = 1/3 et 2/3 ; fonds marins conservés</text><text x="5" y="44">Y 383</text><text x="20" y="432">Y 0</text><text x="60" y="462">X = 0 à WORLD_WIDTH blocs</text><line x1="60" x2="1160" y1="262" y2="262" stroke="#777" stroke-dasharray="5,5"/>'+''.join(paths)+'</g></svg>'
    (out/'transects.svg').write_text(svg.replace('WORLD_WIDTH',str(m['worldWidthBlocks'])),encoding='utf-8')
    r=dict(sourceSha256=m['fieldsSha256'],commit=m['commit'],encodingStatus='PASS',geographicAcceptance='REQUIRES_EARTH_REFERENCE_REVIEW',erosion='NOT_RUN',
           decode='height Y = uint16 * 383 / 65535',maximumQuantizationError=error,noSeaMask=True,noAutoContrast=True,paletteStops=STOPS,files=files)
    (out/'manifest-png.json').write_text(json.dumps(r,indent=2),encoding='utf-8')
    check((d/'height.f64le').read_bytes()==source,'Source was mutated')
    return r

def self_test():
    values=[0,88,104.123456,139.8,167.99,168,168.01,352,383]
    codes=[round(v*65535/383) for v in values]
    check(len(set(codes[:5]))==5,'Submarine levels collapsed')
    payload=b''.join(struct.pack('>H',v) for v in codes);encoded=png(9,1,payload,16,0,'test')
    check(read_pixels(encoded)==(9,1,16,0,payload),'PNG roundtrip failed')
    check(all(abs(v-q*383/65535)<=383/131070+1e-12 for v,q in zip(values,codes)),'Quantization equation failed')
    corrupt=bytearray(encoded);corrupt[-1]^=1
    try:read_pixels(bytes(corrupt))
    except ValueError:pass
    else:raise AssertionError('Corrupt PNG accepted')
    check(colour(112)!=colour(136)!=colour(154),'Coloured seabed flattened')
    print('Raw encoding checks PASS; geographic approval NOT granted')

def main():
    p=argparse.ArgumentParser();p.add_argument('--root',required=True,type=Path);args=p.parse_args();self_test()
    dirs=sorted(args.root.glob('seed-*'));check(len(dirs)==3,'Whole declared seed corpus required')
    reports=[export(d) for d in dirs];cards=[]
    for d in dirs:
        m=json.loads((d/'manifest.json').read_text());label=f"Seed {m['seed']} — {m['worldWidthBlocks']:,} × {m['worldLengthBlocks']:,} blocs — {m['width']} × {m['height']} vraies mesures"
        cards.append(f'<section><h2>{html.escape(label)}</h2><p><a href="{d.name}/png/heightmap-16bit.png">Heightmap numérique 16 bits</a> · <a href="{d.name}/height.f64le">Valeurs float64 exactes</a></p><img src="{d.name}/png/heightmap-grey-preview.png"/><details><summary>Hypsométrie complémentaire, fond marin non masqué</summary><img src="{d.name}/png/heightmap-hypsometric.png"/></details><img src="{d.name}/png/transects.svg"/></section>')
    page='<!doctype html><html lang="fr"><meta charset="utf-8"><title>ISRWorldGen relief brut</title><style>body{font:17px system-ui;max-width:1400px;margin:30px auto;padding:0 20px;line-height:1.5}img{width:100%;height:auto;image-rendering:pixelated}section{border-top:2px solid #888;margin-top:30px}</style><h1>Reliefs bruts — examen avant érosion</h1><p>Aucune érosion. Altitude solide au-dessus ET sous le niveau marin. Noir Y=0, blanc Y=383 pour toutes les seeds ; Y=168 est seulement une référence. Le PNG 16 bits est numérique, les aperçus sont des copies de lecture. Aucun fond marin remplacé par une couleur unie.</p><p>L’acceptation géographique relève de la comparaison par l’assistant aux données terrestres, pas de la réussite des tests ni d’une approbation attendue de l’utilisateur.</p>'+''.join(cards)+'</html>'
    (args.root/'index.html').write_text(page,encoding='utf-8');(args.root/'export-report.json').write_text(json.dumps(reports,indent=2),encoding='utf-8')
if __name__=='__main__':main()
