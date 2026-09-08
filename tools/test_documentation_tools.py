"""Self-tests of the documentation validator, not tests of the Vintage Story mod."""
import json
import shutil
import tempfile
import unittest
from pathlib import Path
import validate_spec as validator
ROOT = Path(__file__).resolve().parent.parent

class DocumentationToolTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name) / "spec"
        shutil.copytree(
            ROOT,
            self.root,
            ignore=shutil.ignore_patterns(".git", ".vs", ".local", "artifacts", "bin", "obj", "__pycache__"),
        )
        # Preserve the small evidence files referenced by delivery notes, but not
        # generated maps, contexts or large future game artifacts.
        for name in ("spec-validation.json", "documentation-tool-tests.txt", "plan-update-tool-tests.txt"):
            source = ROOT / "artifacts" / name
            if source.is_file():
                target = self.root / "artifacts" / name
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(source, target)
    def tearDown(self): self.temp.cleanup()
    def edit(self, path, change):
        p = self.root / path
        obj = json.loads(p.read_text(encoding="utf-8")); change(obj)
        p.write_text(json.dumps(obj, ensure_ascii=False), encoding="utf-8")
    def errors(self): return "\n".join(validator.validate(self.root)["errors"])
    def test_clean_package(self): self.assertEqual(self.errors(), "")
    def test_missing_source(self):
        (self.root / "contracts/C00.md").unlink()
        self.assertIn("Missing document", self.errors())
    def test_cycle(self):
        self.edit("registry/tasks.json", lambda x: x["tasks"][0]["depends_on"].append(x["tasks"][0]["id"]))
        self.assertIn("Dependency cycle", self.errors())
    def test_unknown_test(self):
        self.edit("registry/tasks.json", lambda x: x["tasks"][0]["tests"].append("T99-99"))
        self.assertIn("Unknown test", self.errors())
    def test_requirement_without_owner(self):
        self.edit("registry/tasks.json", lambda x: x["tasks"][0]["requirements"].remove("R00-01"))
        self.assertIn("Requirement has no owner task", self.errors())
    def test_context_budget(self):
        self.edit("registry/tasks.json", lambda x: x["tasks"][0].update(context_max_bytes=64))
        self.assertIn("Context budget exceeded", self.errors())
    def test_broken_link(self):
        with (self.root / "README.md").open("a", encoding="utf-8") as f: f.write("\n[Broken](does-not-exist.md)\n")
        self.assertIn("Broken link", self.errors())
    def test_generated_local_output_is_ignored(self):
        foreign = self.root / ".local" / "foreign"
        foreign.mkdir(parents=True)
        (foreign / "README.md").write_text("[Broken](does-not-exist.md)\n", encoding="utf-8")
        self.assertEqual(self.errors(), "")
    def test_duplicate_seeds(self):
        self.edit("registry/fixtures.json", lambda x: x["full_seeds"].append(0))
        self.assertIn("Duplicate corpus seeds", self.errors())
    def test_post_v1_task_requires_g5(self):
        def change(x):
            next(t for t in x["tasks"] if t["id"] == "L20-A")["requires_gates"] = []
        self.edit("registry/tasks.json", change)
        self.assertIn("Post-V1 task lacks required gate G5: L20-A", self.errors())
    def test_post_v1_task_cannot_enter_v1_gate(self):
        self.edit("registry/gates.json", lambda x: next(g for g in x["gates"] if g["id"] == "G5")["tasks"].append("L20-A"))
        self.assertIn("Post-V1 task included in V1 gate G5", self.errors())
    def test_v1_task_cannot_depend_on_post_v1_task(self):
        self.edit("registry/tasks.json", lambda x: next(t for t in x["tasks"] if t["id"] == "L13-A")["depends_on"].append("L20-A"))
        self.assertIn("V1 task depends on Post-V1 task: L13-A", self.errors())

if __name__ == "__main__": unittest.main()
