"""Executable exporter counterexamples; no acceptance of geographic realism."""
import hashlib
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'tools'))
import export_relief_continu as r


def fixture(root, a=None, **extra):
    root=Path(root);root.mkdir()
    if a is None:a=np.arange(160,dtype=float).reshape(10,16)+90
    a=np.asarray(a,dtype='<f8');raw=a.tobytes();(root/'height.f64le').write_bytes(raw)
    m=dict(seed=73,commit='synthetic-fixture-not-world',mode='synthetic',width=a.shape[1],height=a.shape[0],
           worldWidthBlocks=1600,worldLengthBlocks=1000,ReferenceWidth=1000000,ReferenceLength=625000,
           boundary='PERIODIC_PLANAR_TEST',waterSurfacePresent=False,seabedMasked=False,
           seaLevelReferenceBlocks=168,blocksPerModelKm=12,
           fields={'height':dict(path='height.f64le',sha256=r.sha(raw),encoding='float64 little-endian row-major X right Z down',
                                units='solid Y blocks',minimum=float(a.min()),maximum=float(a.max()))})
    m.update(extra);(root/'manifest.json').write_bytes(r.json_bytes(m));return m


class ReliefTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.root=Path(self.tmp.name)
    def tearDown(self):self.tmp.cleanup()
    def test_TRCV01_independent_numeric_decode(self):
        a=np.linspace(0,383,60).reshape(6,10);images,error=r.render(a)
        head,names=r.inspect_png(images['height-16bit.png'])
        self.assertEqual(head,(10,6,16,0,0,0,0));self.assertLessEqual(error,383/131070+1e-12)
    def test_TRCV02_ramp_has_no_sea_break(self):
        a=np.tile(np.linspace(100,230,257),(2,1));q=r.codes(a)
        self.assertTrue((np.diff(q.astype(int),axis=1)>=0).all())
        self.assertLess(np.ptp(np.diff(q.astype(int),axis=1)),2)
    def test_TRCV03_overlay_cannot_modify_height_images_or_profiles(self):
        m=fixture(self.root/'src');_,g,a,_=r.load(self.root/'src','height');before=a.tobytes()
        images,_=r.render(a);profiles=r.profiles(a,g)
        c1=r.isoline(a,g,150);c2=r.isoline(a,g,190)
        self.assertNotEqual(c1,c2);self.assertEqual(images,r.render(a)[0]);self.assertEqual(profiles,r.profiles(a,g));self.assertEqual(before,a.tobytes())
    def test_TRCV04_categories_are_not_renderer_inputs(self):
        s=self.root/'src';m=fixture(s);_,_,a,_=r.load(s,'height');before=r.render(a)[0]
        m['plateLabels']=[99,-1]*80;m['seaClasses']=[True]*160
        (s/'manifest.json').write_bytes(r.json_bytes(m));_,_,b,_=r.load(s,'height');self.assertEqual(before,r.render(b)[0])
    def test_TRCV05_fixed_normalization_despite_changed_extrema(self):
        a=np.full((3,4),168.);b=a.copy();b[0,0]=0;b[-1,-1]=383
        self.assertEqual(r.codes(a)[1,1],r.codes(b)[1,1]);self.assertEqual(r.codes(a,8)[1,1],r.codes(b,8)[1,1])
    def test_TRCV06_axes_and_offgrid_profiles(self):
        a=np.arange(60,dtype=float).reshape(6,10);m=fixture(self.root/'src',a,boundary='NON_PERIODIC');g=r.geometry(m)
        for z in range(6):
            for x in range(10):self.assertEqual(r.sample(a,g,(x+.5)*g['dx'],(z+.5)*g['dz']),a[z,x])
        x,z=2*g['dx'],2*g['dz'];self.assertEqual(r.sample(a,g,x,z),16.5)
    def test_TRCV07_nan_inf_and_range_rejected(self):
        for bad in (float('nan'),float('inf'),-1,384):
            with self.subTest(bad=bad):
                a=np.zeros((2,3));a[0,0]=bad
                with self.assertRaises(ValueError):r.render(a)
    def test_TRCV07_bad_hash_and_truncated_sources(self):
        s=self.root/'src';fixture(s);p=s/'height.f64le';p.write_bytes(p.read_bytes()[:-1])
        with self.assertRaises(ValueError):r.load(s,'height')
    def test_TRCV07_explicit_solid_flags_required(self):
        for value in (True,None):
            s=self.root/str(value);fixture(s,seabedMasked=value)
            with self.assertRaises(ValueError):r.load(s,'height')
    def test_TRCV07_path_traversal_rejected(self):
        s=self.root/'src';m=fixture(s);m['fields']['height']['path']='../outside.f64le';(s/'manifest.json').write_bytes(r.json_bytes(m))
        with self.assertRaises(ValueError):r.load(s,'height')
    def test_TRCV08_metadata_and_palette(self):
        raw=r.render(np.zeros((2,3)))[0]['height-16bit.png'];_,names=r.inspect_png(raw)
        self.assertFalse(set(names)&{'gAMA','sRGB','tRNS','PLTE','iCCP','cHRM','cICP'})
        lut=r.palette();self.assertEqual(lut.shape,(256,3));self.assertTrue((np.diff(lut.astype(int),axis=0)>=0).all())
        self.assertEqual(lut[0].tolist(),[16,24,32]);self.assertEqual(lut[-1].tolist(),[244,241,230])
    def test_TRCV08_crc_and_color_chunk_rejected(self):
        from export_spatial_png import chunk
        raw=r.render(np.zeros((2,3)))[0]['height-16bit.png']
        with self.assertRaises(ValueError):r.inspect_png(raw[:33]+chunk(b'gAMA',struct.pack('>I',45455))+raw[33:])
        broken=bytearray(raw);broken[-1]^=1
        with self.assertRaises(ValueError):r.inspect_png(bytes(broken))
    def test_TRCV09_viewer_separates_layers_and_defaults_off(self):
        template=(r.Path(r.__file__).with_name('relief_continu_viewer.html')).read_text()
        self.assertIn('<canvas id="overlay">',template);self.assertNotIn(' checked',template)
        layer=template.split('function drawOverlay(){',1)[1].split("$('map').addEventListener",1)[0]
        self.assertNotIn("$('base')",layer);self.assertNotIn('values[',layer.split('=',1)[0])
    def test_TRCV10_six_systematic_profiles(self):
        m=fixture(self.root/'src');g=r.geometry(m);a=np.arange(160,dtype=float).reshape(10,16)+90
        profiles=r.profiles(a,g);self.assertEqual([p['id'] for p in profiles],['X25','X50','X75','Z25','Z50','Z75'])
        self.assertEqual(profiles[1]['height'][0],float((a[4,0]+a[5,0])/2))
    def test_TRCV11_periodic_samples_and_gradients(self):
        m=fixture(self.root/'src');g=r.geometry(m);a=np.arange(160,dtype=float).reshape(10,16)+90
        self.assertEqual(r.sample(a,g,13,24),r.sample(a,g,1613,1024))
        gx,gz=r.gradients(a,g);self.assertAlmostEqual(float(gx[0,0]),(a[0,1]-a[0,-1])/(2*g['dx']))
        self.assertEqual(g['width']/g['length'],1.6)
    def test_TRCV11_seam_isoline_is_not_dropped(self):
        a=np.tile(np.array([100,200,200,200]),(4,1));m=fixture(self.root/'src',a);g=r.geometry(m)
        segments=r.isoline(a,g,150)
        self.assertTrue(any(p[0]>3 for segment in segments for p in segment))
    def test_TRCV11_open_plane_gradient(self):
        a=10+np.arange(6)[:,None]*3+np.arange(10)[None,:]*2;m=fixture(self.root/'src',a,boundary='NON_PERIODIC');g=r.geometry(m)
        gx,gz=r.gradients(a,g);np.testing.assert_allclose(gx,2/g['dx']);np.testing.assert_allclose(gz,3/g['dz'])
    def test_TRCV12_success_reservation_and_source_integrity(self):
        s=self.root/'src';fixture(s);before={p.name:p.read_bytes() for p in s.iterdir()};out=self.root/'out'
        r.export(s,out);r.verify_complete(out)
        self.assertFalse((out/'INCOMPLETE.json').exists())
        with self.assertRaises(FileExistsError):r.export(s,out)
        self.assertEqual(before,{p.name:p.read_bytes() for p in s.iterdir()})
    def test_TRCV12_interruption_every_frontier(self):
        s=self.root/'src';fixture(s)
        for point in ['reserved','sources-copied','derived-written','viewer-written','verified','bundle-moved','completion-pending']:
            out=self.root/point
            def fail(name):
                if name==point:raise InterruptedError(point)
            with self.subTest(point=point):
                with self.assertRaises(InterruptedError):r.export(s,out,fault=fail)
                self.assertFalse((out/'COMPLETE.json').exists());self.assertTrue((out/'INCOMPLETE.json').exists())
                before={str(p.relative_to(out)):p.read_bytes() for p in out.rglob('*') if p.is_file()}
                with self.assertRaises(FileExistsError):r.export(s,out)
                self.assertEqual(before,{str(p.relative_to(out)):p.read_bytes() for p in out.rglob('*') if p.is_file()})
        r.export(s,self.root/'fresh-recovery');r.verify_complete(self.root/'fresh-recovery')
    def test_TRCV12_source_mutation_refuses_completion(self):
        s=self.root/'src';fixture(s)
        def mutate(point):
            if point=='viewer-written':(s/'height.f64le').write_bytes(b'changed-by-test')
        with self.assertRaises(ValueError):r.export(s,self.root/'out',fault=mutate)
        self.assertFalse((self.root/'out/COMPLETE.json').exists())
    def test_TRCV12_corrupted_output_and_nested_output_rejected(self):
        s=self.root/'src';fixture(s)
        with self.assertRaises(ValueError):r.export(s,s/'out')
        out=self.root/'out';r.export(s,out);(out/'bundle/height.f64le').write_bytes(b'corrupted')
        with self.assertRaises(ValueError):r.verify_complete(out)
    def test_numeric_metrics_not_sea_threshold_scores(self):
        a=np.full((32,32),120.);m=fixture(self.root/'src',a);report=r.metrics(a,r.geometry(m))
        self.assertEqual(report['mostFrequentExactCount'],1024);self.assertFalse(report['seaClassificationUsed'])
    def test_direct_grey_rounding_not_double_quantized(self):
        a=np.array([[.5*383/255,1.5*383/255],[0,383]])
        self.assertEqual(r.codes(a,8)[0].tolist(),[0,2])


if __name__=='__main__':unittest.main(verbosity=2)
