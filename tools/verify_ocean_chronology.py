#!/usr/bin/env python3
"""Compare exact, complete C# qualification evidence, not geographic acceptance."""
import hashlib,json,math,struct,sys
from pathlib import Path

def compare(root, output):
    roots=sorted(root.glob('ocean-chronology-*'))
    if len(roots)!=2: raise ValueError('Expected both platforms')
    cases=[]
    for p in roots:
        d=p/'ocean-checks'
        if not (d/'COMPLETE.json').is_file(): raise ValueError('Incomplete qualification')
        c=json.loads((d/'checks.json').read_text())
        if c['status']!='PASS' or c['failed']: raise ValueError('Failed checks')
        cases.append(c['passed'])
    if cases[0]!=cases[1] or len(cases[0])<62: raise ValueError('Mismatched/missing checks')
    reports=[]
    for name in ['age','height','thermal-subsidence-metres']:
        arrays=[]
        for p in roots:
            d=p/'ocean-checks/controlled-basin';m=json.loads((d/'manifest.json').read_text())
            f=m['fields'][name]; b=(d/f['path']).read_bytes()
            if hashlib.sha256(b).hexdigest()!=f['sha256'] or len(b)!=512*512*8: raise ValueError('Invalid field')
            a=[v[0] for v in struct.iter_unpack('<d',b)]
            if not all(math.isfinite(v) for v in a): raise ValueError('Nonfinite field')
            arrays.append(a)
        error=max(abs(a-b) for a,b in zip(*arrays))
        if error>1e-8: raise ValueError(f'Platform divergence {name}: {error}')
        reports.append(dict(field=name,maximumDifference=error))
    for seed in [-437287116,20260906,73]:
        pair=[json.loads((p/f'ocean-checks/regression-{seed}.json').read_text()) for p in roots]
        if any(v['status']!='PASS' or v['maximumDifference']!=0 for v in pair): raise ValueError('Regression failed')
        # Full state hashes may differ by last-bit math between platforms. This
        # regression asserts same-platform legacy vs explicit chronology identity.
    phase_receipts=[]
    for p in roots:
        r=json.loads((p/'ocean-checks/prehistory/receipts.json').read_text())
        if r['scope']!='CORE_PHASE_CONTINUITY_TEST_NOT_ACCEPTED_WORLD' or len(r['Phases'])!=2:
            raise ValueError('Missing phase continuity evidence')
        a,b=r['Phases']
        if a['FinalMaterialChecksum']!=b['InitialMaterialChecksum'] or a['EndTimeMyr']!=b['StartTimeMyr']:
            raise ValueError('Broken material/time lineage')
        if r['finalMaterialChecksum']!=r['uninterruptedMaterialChecksum']:
            raise ValueError('Aligned phase result differs from uninterrupted run')
        phase_receipts.append(r)
    for a,b in zip(phase_receipts[0]['Phases'],phase_receipts[1]['Phases']):
        if a['Label']!=b['Label'] or a['CumulativeSteps']!=b['CumulativeSteps']:
            raise ValueError('Incompatible phase schedules')
        for k in ('StartTimeMyr','EndTimeMyr','CreatedOceanicVolumeKm3','RecycledOceanicVolumeKm3'):
            if not all(math.isfinite(x[k]) for x in (a,b)) or abs(a[k]-b[k])>1e-8:
                raise ValueError('Phase platform mismatch: '+k)
        if a['Coverage']['Status']!=b['Coverage']['Status'] or a['Coverage']['HasUniformAgePriorWithoutBirthEvidence']!=b['Coverage']['HasUniformAgePriorWithoutBirthEvidence']:
            raise ValueError('Inherited provenance status changed across platforms')
    report=dict(status='PASS',checksPerPlatform=len(cases[0]),fields=reports,
        geographicAcceptance='NOT_EVALUATED',scope='CONTROLLED_BASIN_LEGACY_REGRESSION_AND_MATERIAL_CONTINUATION',erosion='NOT_RUN')
    with output.open('x') as f: json.dump(report,f,indent=2)
    print(json.dumps(report,indent=2))

if __name__=='__main__': compare(Path(sys.argv[1]),Path(sys.argv[2]))
