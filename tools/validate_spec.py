#!/usr/bin/env python3
"""Validate the documentation package only; no game, compiler or MCP is invoked."""
from __future__ import annotations
import argparse
import hashlib
import json
import re
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

def load(root: Path, relative: str) -> Any:
    return json.loads((root / relative).read_text(encoding="utf-8"))

def capsule(root: Path, task: dict[str, Any]) -> str:
    parts = [f"# Context: {task['id']}\n\nDocumentation capsule. Read relative links from each original source path. Code and upstream handoffs are not included automatically.\n"]
    for relative in dict.fromkeys(task["required_read"]):
        path = (root / relative).resolve()
        if not path.is_relative_to(root.resolve()):
            raise ValueError(f"Source outside repository: {relative}")
        data = path.read_bytes()
        parts.append(f"---\n\n## SOURCE: {relative}\nSHA256: {hashlib.sha256(data).hexdigest()}\n\n{data.decode('utf-8')}")
    return "\n".join(parts)

def validate(root: Path) -> dict[str, Any]:
    errors: list[str] = []
    warnings: list[str] = []
    try:
        tasks = load(root, "registry/tasks.json")["tasks"]
        requirements = load(root, "registry/requirements.json")["requirements"]
        tests = load(root, "registry/tests.json")["tests"]
        state_path = "registry/state.json" if (root / "registry/state.json").is_file() else "registry/state.template.json"
        state_data = load(root, state_path)
        states = state_data["tasks"]
        state_is_template = state_path.endswith("template.json") or state_data.get("template_only", False)
        gates = load(root, "registry/gates.json")["gates"]
        fixtures = load(root, "registry/fixtures.json")
    except (OSError, ValueError, KeyError) as exc:
        return {"status": "FAIL", "scope": "DOCUMENTATION_ONLY", "errors": [str(exc)], "warnings": []}
    def unique(items: list[dict[str, Any]], label: str) -> set[str]:
        ids = [v["id"] for v in items]
        if len(ids) != len(set(ids)): errors.append(f"Duplicate {label} IDs")
        return set(ids)
    tids = unique(tasks, "task")
    rids = unique(requirements, "requirement")
    testids = unique(tests, "test")
    by_task = {t["id"]: t for t in tasks}
    assigned_r: set[str] = set()
    assigned_t: set[str] = set()
    coverage: set[str] = set()
    context_sizes: dict[str, int] = {}
    for t in tasks:
        for dep in t["depends_on"]:
            if dep not in tids: errors.append(f"Unknown dependency {dep} in {t['id']}")
        assigned_r.update(t["requirements"])
        assigned_t.update(t["tests"])
        for rid in t["requirements"]:
            if rid not in rids: errors.append(f"Unknown requirement {rid} in {t['id']}")
        for test in t["tests"]:
            if test not in testids: errors.append(f"Unknown test {test} in {t['id']}")
        for path in [t["document"], *t["required_read"]]:
            if not (root / path).is_file(): errors.append(f"Missing document {path} for {t['id']}")
        try:
            size = len(capsule(root, t).encode("utf-8"))
            context_sizes[t["id"]] = size
            if size > t["context_max_bytes"]: errors.append(f"Context budget exceeded: {t['id']} ({size})")
        except (OSError, ValueError) as exc:
            errors.append(f"Context failure {t['id']}: {exc}")
    colors: dict[str, int] = {}
    def visit(tid: str) -> None:
        if colors.get(tid) == 1:
            errors.append(f"Dependency cycle at {tid}")
            return
        if colors.get(tid) == 2: return
        colors[tid] = 1
        for dep in by_task[tid]["depends_on"]:
            if dep in by_task: visit(dep)
        colors[tid] = 2
    for tid in by_task: visit(tid)
    if not tids.issubset(set(states)): errors.append("State registry is missing task IDs")
    if state_is_template: warnings.append("Active execution state unavailable. Template is not progress; reported L05-C is not assumed DONE.")
    for test in tests:
        coverage.update(test["requirements"])
        for rid in test["requirements"]:
            if rid not in rids: errors.append(f"Unknown requirement {rid} in {test['id']}")
        source = root / test["source"]
        if not source.is_file() or not re.search(r"^## " + re.escape(test["id"]) + r"\b", source.read_text(encoding="utf-8"), re.M):
            errors.append(f"Test definition missing: {test['id']}")
    for requirement in requirements:
        source = root / requirement["source"]
        if not source.is_file() or not re.search(r"^### " + re.escape(requirement["id"]) + r"\b", source.read_text(encoding="utf-8"), re.M):
            errors.append(f"Requirement definition missing: {requirement['id']}")
    for missing in sorted(rids - coverage): errors.append(f"Requirement has no test: {missing}")
    for missing in sorted(rids - assigned_r): errors.append(f"Requirement has no owner task: {missing}")
    for missing in sorted(testids - assigned_t): errors.append(f"Test has no owner task: {missing}")
    for gate in gates:
        for tid in gate["tasks"]:
            if tid not in tids: errors.append(f"Unknown gate task {tid}")
    covered_gate_tasks = {tid for gate in gates for tid in gate["tasks"]}
    for missing in sorted(tids - covered_gate_tasks): errors.append(f"Task not in a gate: {missing}")
    full = fixtures["full_seeds"]
    if len(full) != len(set(full)): errors.append("Duplicate corpus seeds")
    if any(not isinstance(s, int) or not -(1 << 31) <= s < (1 << 31) for s in full): errors.append("Seed outside native int32 range")
    if set(fixtures["calibration_seeds"]) & set(fixtures["holdout_seeds"]): errors.append("Calibration and holdout overlap")
    if set(fixtures["calibration_seeds"]) | set(fixtures["holdout_seeds"]) != set(full): errors.append("Corpus partitions incomplete")
    markdown_count = 0
    ignored_documentation_parts = {".git", ".local", "artifacts", "bin", "obj", "__pycache__"}
    for doc in root.rglob("*.md"):
        if ignored_documentation_parts & set(doc.relative_to(root).parts): continue
        markdown_count += 1
        text = doc.read_text(encoding="utf-8")
        if "\x00" in text or "\ufffd" in text: errors.append(f"Invalid text in {doc.relative_to(root)}")
        # Relative links only. Source URLs are not fetched by this offline validator.
        for target in re.findall(r"\[[^\]]*\]\(([^)]+)\)", text):
            if re.match(r"^[a-z][a-z0-9+.-]*:", target, re.I) or target.startswith("#"): continue
            target = target.split("#", 1)[0]
            if target and not (doc.parent / target).resolve().exists(): errors.append(f"Broken link {doc.relative_to(root)} -> {target}")
    quality = load(root, "registry/quality-budgets.json")
    if quality["status"] != "FROZEN": warnings.append("Game/performance/rarity budgets are proposed; mod acceptance remains blocked until frozen and executed.")
    dist = load(root, "registry/distribution-budgets.json")
    if dist.get("status") != "FROZEN": warnings.append("Distribution-specific budgets not frozen; no final game qualification claimed.")
    warnings.append("PowerShell runtime, C# compilation, installed game and MCP were not exercised by this documentation validator.")
    ready = [] if state_is_template else sorted(t["id"] for t in tasks if states[t["id"]]["status"] in {"BACKLOG", "READY"} and all(states[d]["status"] == "DONE" for d in t["depends_on"] if d in states))
    return {"status": "PASS" if not errors else "FAIL", "scope": "DOCUMENTATION_ONLY", "generated_utc": datetime.now(timezone.utc).isoformat(), "counts": {"tasks": len(tasks), "requirements": len(requirements), "test_scenarios": len(tests), "gates": len(gates), "seeds": len(full), "markdown_documents": markdown_count}, "candidate_tasks_by_declared_status_only_not_requalification": ready, "active_state_available": not state_is_template, "context_bytes": context_sizes, "errors": errors, "warnings": warnings}

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--emit-context", metavar="TASK")
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    root = args.root.resolve()
    report = validate(root)
    out = args.report or root / "artifacts/spec-validation.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if args.emit_context:
        tasks = load(root, "registry/tasks.json")["tasks"]
        task = next((t for t in tasks if t["id"] == args.emit_context), None)
        if task is None:
            print(f"Unknown task: {args.emit_context}", file=sys.stderr); return 2
        text = capsule(root, task)
        if len(text.encode("utf-8")) > task["context_max_bytes"]:
            print("Context budget exceeded; no truncation performed", file=sys.stderr); return 2
        context_out = root / "artifacts/contexts" / (task["id"] + ".md")
        context_out.parent.mkdir(parents=True, exist_ok=True)
        context_out.write_text(text, encoding="utf-8")
        print(f"Context: {context_out}")
    print(f"Documentation: {report['status']}; report: {out}")
    for error in report["errors"]: print("ERROR:", error)
    return 0 if report["status"] == "PASS" else 1

if __name__ == "__main__":
    raise SystemExit(main())
