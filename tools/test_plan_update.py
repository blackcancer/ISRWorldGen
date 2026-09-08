"""Tests of plan adoption preparation; no Vintage Story execution or code build."""
from __future__ import annotations
import copy
import hashlib
import json
import shutil
import tempfile
import unittest
from pathlib import Path
import prepare_plan_update as prep

ROOT = Path(__file__).resolve().parent.parent

def digest_tree(root: Path) -> dict[str, str]:
    return {p.relative_to(root).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in root.rglob('*') if p.is_file()}

class PlanUpdateTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.base = Path(self.tmp.name)
        self.existing = self.base / 'active'
        self.existing.mkdir()
        self.baseline = prep.read_json(ROOT / 'tools/update_baseline_r11.json')
        for path, data in self.baseline['registries'].items():
            prep.write_json(self.existing / path, data)
        self.state = {
            'schema_version': 1, 'integrated_commit': 'synthetic-commit-test',
            'custom_metadata': {'keep': True},
            'tasks': {
                'L03-C': {'status': 'DONE', 'evidence': ['proof-rocks'], 'extra': 99},
                'L05-C': {'status': 'IN_PROGRESS', 'owner': 'agent-local', 'branch': 'local-work'},
                'LOCAL-X': {'status': 'DONE', 'evidence': ['custom-proof']},
            },
        }
        prep.write_json(self.existing / 'registry/state.json', self.state)
        (self.existing / 'worklogs').mkdir()
        (self.existing / 'worklogs/L05-C.md').write_text('private local handoff\n')
        (self.existing / 'src').mkdir()
        (self.existing / 'src/existing.cs').write_text('// existing code\n')
        self.output = self.base / 'review'
    def tearDown(self):
        self.tmp.cleanup()
    def run_prepare(self):
        return prep.prepare(self.existing, self.output, ROOT)
    def test_active_repository_never_modified(self):
        before = digest_tree(self.existing)
        self.run_prepare()
        self.assertEqual(before, digest_tree(self.existing))
    def test_state_preserves_every_existing_entry_and_field(self):
        self.run_prepare()
        candidate = prep.read_json(self.output / 'state.candidate.json')
        for k, v in self.state.items():
            if k == 'tasks':
                for tid, entry in v.items(): self.assertEqual(candidate['tasks'][tid], entry)
            else:
                self.assertEqual(candidate[k], v)
        self.assertEqual(candidate['tasks']['L14-A']['status'], 'BACKLOG')
        self.assertEqual(candidate['tasks']['L00-A']['status'], 'UNVERIFIED')
        self.assertFalse((self.output / 'proposal/registry/state.json').exists())
    def test_existing_new_task_status_not_reset(self):
        state = copy.deepcopy(self.state)
        state['tasks']['L18-A'] = {'status': 'REVIEW', 'evidence': ['seasonal-proof']}
        prep.write_json(self.existing / 'registry/state.json', state)
        self.run_prepare()
        candidate = prep.read_json(self.output / 'state.candidate.json')
        self.assertEqual(candidate['tasks']['L18-A'], state['tasks']['L18-A'])
    def test_missing_state_does_not_assume_done(self):
        (self.existing / 'registry/state.json').unlink()
        self.run_prepare()
        candidate = prep.read_json(self.output / 'state.candidate.json')
        self.assertEqual(candidate['tasks']['L05-C']['status'], 'UNVERIFIED')
        self.assertNotIn('DONE', [t['status'] for t in candidate['tasks'].values()])
    def test_custom_paths_and_dependencies_preserved(self):
        p = self.existing / 'registry/tasks.json'
        data = prep.read_json(p)
        target = next(t for t in data['tasks'] if t['id'] == 'L06-A')
        target['allowed_write_paths'] = ['src/ISRWorldGen/LocalImplementation/']
        target['depends_on'].append('LOCAL-X')
        target['local_note'] = 'preserve'
        data['tasks'].append({'id': 'LOCAL-X', 'title': 'local task', 'depends_on': []})
        prep.write_json(p, data)
        self.run_prepare()
        result = prep.read_json(self.output / 'proposal/registry/tasks.json')
        merged = next(t for t in result['tasks'] if t['id'] == 'L06-A')
        self.assertEqual(merged['allowed_write_paths'], target['allowed_write_paths'])
        self.assertTrue({'LOCAL-X', 'L14-B', 'L18-A'}.issubset(merged['depends_on']))
        self.assertEqual(merged['local_note'], 'preserve')
        self.assertIn('LOCAL-X', [t['id'] for t in result['tasks']])
    def test_local_markdown_conflict_is_reported(self):
        p = self.existing / 'docs/01-PLAN.md'
        p.parent.mkdir()
        p.write_text('local architecture decisions\n')
        report = self.run_prepare()
        row = next(x for x in report['files'] if x['path'] == 'docs/01-PLAN.md')
        self.assertEqual(row['status'], 'CONFLICT')
        self.assertEqual(p.read_text(), 'local architecture decisions\n')
        self.assertTrue(report['conflicts'])
    def test_output_inside_active_repository_rejected(self):
        with self.assertRaises(ValueError):
            prep.prepare(self.existing, self.existing / 'review', ROOT)
    def test_existing_review_directory_rejected(self):
        self.output.mkdir()
        with self.assertRaises(FileExistsError): self.run_prepare()
    def test_unsupported_state_schema_rejected_without_staging(self):
        prep.write_json(self.existing / 'registry/state.json', {'tasks': []})
        with self.assertRaises(ValueError): self.run_prepare()
        self.assertFalse(self.output.exists())
    def test_new_id_collision_requires_review(self):
        base = {'schema_version': 1, 'tasks': []}
        local = {'schema_version': 1, 'tasks': [{'id': 'L14-A', 'title': 'different local work'}]}
        desired = {'schema_version': 1, 'tasks': [{'id': 'L14-A', 'title': 'new catalog task'}]}
        merged, conflicts = prep.merge_registry(local, base, desired, 'tasks')
        self.assertTrue(conflicts)
        self.assertEqual(merged['tasks'][0]['title'], 'different local work')
    def test_no_runtime_data_in_update_manifest(self):
        manifest = prep.read_json(ROOT / 'registry/update-files.json')
        self.assertTrue(manifest['files'])
        for path in manifest['files']:
            self.assertNotEqual(path, 'registry/state.json')
            self.assertFalse(path.startswith(('worklogs/', 'src/', 'testsrc/')))
    def test_original_task_ids_and_L05C_preserved(self):
        current = prep.read_json(ROOT / 'registry/tasks.json')['tasks']
        old = self.baseline['registries']['registry/tasks.json']['tasks']
        self.assertTrue({x['id'] for x in old}.issubset({x['id'] for x in current}))
        a = next(t for t in old if t['id'] == 'L05-C')
        b = next(t for t in current if t['id'] == 'L05-C')
        for field in ['id','title','depends_on','requirements','tests','allowed_write_paths']:
            self.assertEqual(a[field], b[field])
        raw = (ROOT / b['document']).read_bytes()
        self.assertEqual(hashlib.sha256(raw).hexdigest(), self.baseline['sha256'][b['document']])

    def test_powershell_selector_accepts_all_declared_tasks(self):
        # Static contract check only, not a claim of running PowerShell here.
        import re
        script = (ROOT / 'tools/Get-TaskContext.ps1').read_text()
        pattern = re.search(r"ValidatePattern\('([^']+)'\)", script).group(1)
        for task in prep.read_json(ROOT / 'registry/tasks.json')['tasks']:
            self.assertRegex(task['id'], pattern)
    def test_final_tests_not_owned_by_blocking_early_probes(self):
        records = {t['id']: t for t in prep.read_json(ROOT / 'registry/tasks.json')['tasks']}
        self.assertNotIn('T15-03', records['L15-B']['tests'])
        self.assertIn('T15-08', records['L15-B']['tests'])
        self.assertIn('T15-03', records['L15-C']['tests'])
        self.assertNotIn('T17-05', records['L17-C']['tests'])
        self.assertIn('T17-10', records['L17-C']['tests'])
        self.assertIn('T17-05', records['L17-D']['tests'])
        self.assertNotIn('T16-05', records['L16-B']['tests'])
        self.assertIn('T16-05', records['L16-D']['tests'])

if __name__ == '__main__': unittest.main()
