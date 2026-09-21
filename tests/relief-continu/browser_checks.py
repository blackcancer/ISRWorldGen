"""Real Chromium smoke/regression checks, exported fixture and DOM separation.
This runs independently from numerical unit tests; never claims a world review.
"""
import base64
import hashlib
import json
from pathlib import Path
import sys
import tempfile
from playwright.sync_api import sync_playwright
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'tools'))
from export_relief_continu import export, verify_complete
from test_relief_continu import fixture

root=Path(sys.argv[1]).resolve();root.mkdir(parents=True,exist_ok=False)
checks=[]
def require(condition,name):
    if not condition:raise AssertionError(name)
    checks.append(name)
with tempfile.TemporaryDirectory() as tmp:
    fixture(Path(tmp)/'src')
    export(Path(tmp)/'src',root/'fixture',exporter_commit='BROWSER_FIXTURE')
    verify_complete(root/'fixture')
with sync_playwright() as p:
    browser=p.chromium.launch(headless=True)
    page=browser.new_page(viewport={'width':1200,'height':950},device_scale_factor=1)
    errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
    page.goto((root/'fixture/bundle/index.html').as_uri())
    page.wait_for_function("document.getElementById('base').complete && document.getElementById('base').naturalWidth>0")
    baseline=page.locator('#base').get_attribute('src')
    raw=page.evaluate('Array.from(values)')
    require(not page.locator('#levelOn').is_checked() and not page.locator('#profilesOn').is_checked(),'overlays off at load')
    require(page.evaluate('current.meta.geometry.nx')==16,'source resolution not image size')
    page.locator('#levelOn').check()
    contour1=page.evaluate("document.getElementById('overlay').toDataURL()")
    page.locator('#level').fill('190');page.locator('#level').dispatch_event('change')
    contour2=page.evaluate("document.getElementById('overlay').toDataURL()")
    require(contour1!=contour2,'reference level changes only independent canvas')
    page.locator('#profilesOn').check();page.locator('#clear').click()
    require(page.locator('#base').get_attribute('src')==baseline and page.evaluate('Array.from(values)')==raw,'overlay lifecycle preserves all source values and base')
    page.locator('#view').select_option('color')
    color=page.locator('#base').get_attribute('src')
    page.locator('#level').fill('100');page.locator('#level').dispatch_event('change')
    require(color==page.locator('#base').get_attribute('src'),'color independent from reference level')
    page.locator('#view').select_option('grey');page.locator('#zoom').select_option('4')
    box=page.locator('#base').bounding_box()
    page.mouse.move(box['x']+box['width']*2.5/16,box['y']+box['height']*3.5/10)
    require('Cellule [2, 3]' in page.locator('#readout').inner_text() and '140.000' in page.locator('#readout').inner_text(),'cursor reads expected float64 and cell-centred orientation')
    require(page.locator('#profileSelect option').count()==6,'six systematic transects')
    page.locator('#zoom').select_option('fit');page.screenshot(path=str(root/'viewer.png'),full_page=True)
    with page.expect_download() as download:
        page.locator('#raw').click()
    path=root/'downloaded-height.f64le';download.value.save_as(path)
    require(path.read_bytes()==(root/'fixture/bundle/height.f64le').read_bytes(),'browser download keeps exact little-endian source')
    require(not errors,'no JavaScript error')
    (root/'browser-report.json').write_text(json.dumps(dict(status='PASS',checks=checks,browser=browser.version,worldGeneration=False),indent=2))
    browser.close()
print(f'{len(checks)} real browser checks passed')
