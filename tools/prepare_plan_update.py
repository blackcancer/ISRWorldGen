#!/usr/bin/env python3
"""Stage a reviewed plan update OUTSIDE the active repository. Never apply it.

Python 3.10+, standard library only. This is a documentation tool, not mod code.
The active repository, execution state, sources and saves are read-only inputs.
"""
from __future__ import annotations
import argparse
import copy
import hashlib
import json
import sys
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parent.parent
COLLECTIONS = {
    'registry/tasks.json': 'tasks',
    'registry/requirements.json': 'requirements',
    'registry/tests.json': 'tests',
    'registry/gates.json': 'gates',
}
UNION_FIELDS = {'depends_on', 'required_read', 'requirements', 'tests', 'tasks'}
MISSING = object()

def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding='utf-8-sig'))

def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()

def checked(root: Path, relative: str) -> Path:
    p = (root / relative).resolve()
    if not p.is_relative_to(root.resolve()):
        raise ValueError(f'Path escapes input root: {relative}')
    return p

def indexed(items: Any) -> dict[str, dict[str, Any]]:
    if not isinstance(items, list):
        raise ValueError('Registry collection must be a list of records')
    out = {}
    for item in items:
        if not isinstance(item, dict) or not isinstance(item.get('id'), str):
            raise ValueError('Every registry record needs a string id')
        if item['id'] in out:
            raise ValueError(f"Duplicate registry id: {item['id']}")
        out[item['id']] = item
    return out

def merge_registry(current: dict, baseline: dict, desired: dict, collection: str) -> tuple[dict, list[str]]:
    """Three-way field merge, preserving custom local items and fields.

    Dependencies/required reads add constraints by union. Content or write-path
    conflicts remain local in the candidate and are reported for human review.
    No current fields (including any historical states) are deleted implicitly.
    """
    result = copy.deepcopy(current)
    cur = indexed(result.get(collection, []))
    old = indexed(baseline.get(collection, []))
    wanted = indexed(desired.get(collection, []))
    conflicts: list[str] = []
    for rid, new in wanted.items():
        if rid not in cur:
            if rid in old:
                # A locally removed historical record may have been intentional.
                conflicts.append(f'{rid}: historical record absent locally; proposed record restored for review')
            cur[rid] = copy.deepcopy(new)
            continue
        if rid not in old:
            if cur[rid] != new:
                conflicts.append(f'{rid}: new ID already exists locally with different content')
            continue
        local = cur[rid]
        before = old[rid]
        for key, value in new.items():
            old_value = before.get(key, MISSING)
            local_value = local.get(key, MISSING)
            if old_value == value:
                # No upstream change: preserve local changes, omissions and extensions.
                continue
            if local_value == value:
                continue
            if local_value is MISSING:
                if old_value is not MISSING:
                    conflicts.append(f'{rid}.{key}: locally removed field also changed upstream')
                local[key] = copy.deepcopy(value)
            elif key in UNION_FIELDS and isinstance(value, list) and isinstance(local_value, list):
                local[key] = list(dict.fromkeys(local_value + value))
            elif local_value == old_value:
                local[key] = copy.deepcopy(value)
            else:
                conflicts.append(f'{rid}.{key}: conflicting local/upstream change; local retained')
    # Preserve local ordering and unknown records; new records append deterministically.
    result[collection] = list(cur.values())
    for key, value in desired.items():
        if key == collection:
            continue
        old_value = baseline.get(key, MISSING)
        local_value = current.get(key, MISSING)
        if old_value == value:
            continue
        if local_value is MISSING or local_value == old_value or local_value == value:
            result[key] = copy.deepcopy(value)
        else:
            conflicts.append(f'{key}: conflicting registry metadata; local retained')
    return result, conflicts

def state_candidate(current: dict | None, desired_tasks: list[dict], new_ids: set[str]) -> tuple[dict, list[str]]:
    notes: list[str] = []
    if current is None:
        result = {'schema_version': 1, 'tasks': {}}
        notes.append('No active state found. Existing task states remain UNVERIFIED; reconstruct from evidence.')
    else:
        result = copy.deepcopy(current)
    if not isinstance(result.get('tasks'), dict):
        raise ValueError('Unsupported active state schema: tasks must be a mapping. No conversion performed.')
    for task in desired_tasks:
        tid = task['id']
        if tid not in result['tasks']:
            result['tasks'][tid] = {
                'status': 'BACKLOG' if tid in new_ids else 'UNVERIFIED',
                'owner': None, 'branch': None, 'base_commit': None,
                'evidence': [], 'blocker': None,
            }
            if tid not in new_ids:
                notes.append(f'{tid}: absent active status; UNVERIFIED, not DONE or a fabricated history.')
    # Do not alter updated_utc, integrated_commit, active owners or previous evidence.
    return result, notes

def prepare(existing: Path, output: Path, package: Path = ROOT) -> dict:
    existing = existing.resolve(strict=True)
    package = package.resolve(strict=True)
    output = output.resolve()
    if not existing.is_dir() or not package.is_dir():
        raise ValueError('Existing repository and package must be directories')
    if output.is_relative_to(existing) or output.is_relative_to(package):
        raise ValueError('Review output must be outside both the active repository and the package')
    if output.exists():
        raise FileExistsError('Review output already exists; use a new directory (no overwrite)')
    baseline = read_json(package / 'tools/update_baseline_r11.json')
    manifest = read_json(package / 'registry/update-files.json')
    update = read_json(package / 'registry/update-r12.json')
    planned: list[tuple[str, bytes]] = []
    rows = []
    conflicts = []
    for rel in manifest['files']:
        if rel == 'registry/state.json' or rel.startswith(('worklogs/', 'src/', 'testsrc/')):
            raise ValueError(f'Forbidden active data in documentation update manifest: {rel}')
        proposed_path = checked(package, rel)
        proposed = proposed_path.read_bytes()
        local_path = checked(existing, rel)
        local = local_path.read_bytes() if local_path.is_file() else None
        base_hash = baseline['sha256'].get(rel)
        row: dict[str, Any] = {'path': rel, 'proposed_sha256': sha(proposed)}
        if local is None:
            row['status'] = 'ADD_REVIEW'
            planned.append((rel, proposed))
        elif local == proposed:
            row['status'] = 'ALREADY_IDENTICAL'
        elif rel in COLLECTIONS:
            collection = COLLECTIONS[rel]
            current_data = json.loads(local.decode('utf-8-sig'))
            desired_data = json.loads(proposed.decode('utf-8-sig'))
            merged, record_conflicts = merge_registry(current_data, baseline['registries'][rel], desired_data, collection)
            row['status'] = 'MERGE_REVIEW' if not record_conflicts else 'CONFLICT'
            row['field_conflicts'] = record_conflicts
            conflicts.extend(f'{rel}: {c}' for c in record_conflicts)
            planned.append((rel, (json.dumps(merged, ensure_ascii=False, indent=2) + '\n').encode('utf-8')))
        elif sha(local) == base_hash:
            row['status'] = 'BASELINE_MATCH_UPDATE_REVIEW'
            planned.append((rel, proposed))
        else:
            row['status'] = 'CONFLICT'
            conflicts.append(f'{rel}: locally changed; proposed version is for manual merge only')
            planned.append((rel, proposed))
        if local is not None:
            row['local_sha256'] = sha(local)
        rows.append(row)
    # Preflight every read/schema check before creating the review folder.
    active_path = checked(existing, 'registry/state.json')
    active = read_json(active_path) if active_path.is_file() else None
    task_defs = read_json(package / 'registry/tasks.json')['tasks']
    candidate, notes = state_candidate(active, task_defs, set(update['new_tasks']))
    output.mkdir(parents=True, exist_ok=False)
    for rel, data in planned:
        target = checked(output / 'proposal', rel)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
    # State is intentionally not in proposal/: applying docs cannot overwrite it.
    write_json(output / 'state.candidate.json', candidate)
    report = {
        'scope': 'DOCUMENTATION_PREPARATION_ONLY',
        'active_repository_modified': False,
        'auto_apply_supported': False,
        'reported_checkpoint': 'L05-C (user report, not an accepted task state)',
        'status': 'REVIEW_WITH_CONFLICTS' if conflicts else 'REVIEW_REQUIRED',
        'existing_root': str(existing),
        'files': rows,
        'conflicts': conflicts,
        'state_notes': notes,
        'instructions': 'Review all diffs; manually merge conflicts. State candidate is separate and must be merged preserving history. This report is not a game validation.',
    }
    write_json(output / 'review-report.json', report)
    (output / 'README.txt').write_text(
        'REVIEW ONLY — no active repository files were changed.\n'
        'proposal/ contains new or changed proposed documentation, including manual conflicts.\n'
        'Do not bulk-copy proposal/ when review-report.json contains conflicts.\n'
        'state.candidate.json preserves existing entries and adds new IDs; review separately.\n'
        'No build, game, MCP, Git commit, push or migration of saves was performed.\n', encoding='utf-8')
    return report

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--existing', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    try:
        report = prepare(args.existing, args.output)
    except (OSError, ValueError, KeyError, TypeError) as exc:
        print(f'Preparation failed, no active write performed: {exc}', file=sys.stderr)
        return 1
    print(f"{report['status']}: {args.output / 'review-report.json'}")
    print('Active repository unchanged. Review and integration remain the orchestrator responsibility.')
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
