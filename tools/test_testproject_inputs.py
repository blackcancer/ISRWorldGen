"""Exercise the real test project's Compile glob without the game's assemblies.

Only isolated temporary fixtures are written. The compiler witness consumes the
exact Compile list returned by MSBuild, not a Python approximation of SDK globs.
This is not a full build of WorldGen.Tests or a native game qualification.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

BASELINE = "ebe48f27d34ef5a5ad897204168bb42bd822c1f6"
PROJECT = "testsrc/WorldGen.Tests/WorldGen.Tests.csproj"
INPUTS = (PROJECT, "Directory.Build.props", "global.json")
ATTRIBUTES = (
    "TargetFrameworkAttribute", "AssemblyCompanyAttribute",
    "AssemblyConfigurationAttribute", "AssemblyFileVersionAttribute",
    "AssemblyInformationalVersionAttribute", "AssemblyProductAttribute",
    "AssemblyTitleAttribute", "AssemblyVersionAttribute",
)
GOOD_SOURCES = (
    "InputControl.cs", "L00C/InputControl.cs", "binary/InputControl.cs",
    "objects/InputControl.cs", "Properties/AssemblyInfo.cs",
)


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def run(arguments: list[str], cwd: Path, logfile: Path | None = None) -> subprocess.CompletedProcess:
    result = subprocess.run(arguments, cwd=cwd, capture_output=True, text=True,
                            encoding="utf-8", errors="replace", timeout=180)
    if logfile is not None:
        logfile.write_text(result.stdout + result.stderr, encoding="utf-8")
    return result


def write(path: Path, contents: str | bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(contents if isinstance(contents, bytes) else contents.encode("utf-8"))


def exercise(snapshot: dict[str, bytes], root: Path, configuration: str,
             relocated: bool, fixed: bool, logs: Path) -> dict:
    for name, data in snapshot.items():
        write(root / name, data)
    project = root / PROJECT
    source = project.parent
    for index, name in enumerate(GOOD_SOURCES[:-1]):
        write(source / name, f"internal class InputControl{index} {{ }}\n")
    # A legitimate hand-written attribute file must NOT be excluded by its name.
    write(source / GOOD_SOURCES[-1],
          '[assembly: System.Reflection.AssemblyMetadata("InputControl", "preserve")]\n')
    # An existing explicit exclusion must also remain effective.
    write(source / "L00C/L00CMenuActionLabModSystem.cs", "#error existing exclusion was lost\n")
    stale = "\n".join([
        '[assembly: System.Runtime.Versioning.TargetFramework(".NETCoreApp,Version=v10.0")]',
        '[assembly: System.Reflection.AssemblyCompany("Stale")]',
        '[assembly: System.Reflection.AssemblyConfiguration("Stale")]',
        '[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]',
        '[assembly: System.Reflection.AssemblyInformationalVersion("Stale")]',
        '[assembly: System.Reflection.AssemblyProduct("Stale")]',
        '[assembly: System.Reflection.AssemblyTitle("Stale")]',
        '[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]',
    ])
    for folder in ("L00C/obj/Debug/net10.0", "L00C/bin/Release/net10.0",
                   "L00C/deeper/obj/Release/net10.0", "obj/Debug/net10.0", "bin/Release/net10.0"):
        write(source / folder / "Stale.AssemblyInfo.cs", stale)
    common = [f"-p:Configuration={configuration}"]
    if relocated:
        common += [f"-p:BaseIntermediateOutputPath={root / 'external-obj'}/",
                   f"-p:BaseOutputPath={root / 'external-bin'}/"]

    def evaluate(label: str, design_time: bool = False) -> dict:
        command = ["dotnet", "msbuild", str(project), "-nologo", "-getItem:Compile",
                   "-getProperty:TargetFramework,GenerateAssemblyInfo,GenerateTargetFrameworkAttribute,EnableDefaultCompileItems"] + common
        if design_time:
            command += ["-p:DesignTimeBuild=true", "-p:BuildProjectReferences=false"]
        result = run(command, root, logs / f"{label}.log")
        require(result.returncode == 0, f"MSBuild evaluation failed; see {label}.log")
        return json.loads(result.stdout.lstrip("\ufeff"))

    evaluation = evaluate("evaluation")
    properties = evaluation["Properties"]
    require(properties["GenerateAssemblyInfo"] == "true", "AssemblyInfo generation was disabled")
    require(properties["GenerateTargetFrameworkAttribute"] != "false", "TargetFramework generation was disabled")
    require(properties["EnableDefaultCompileItems"] == "true", "Default Compile inclusion was disabled")
    items = evaluation["Items"]["Compile"]
    names = sorted(item["Identity"].replace("\\", "/") for item in items)
    require(set(GOOD_SOURCES).issubset(names), "Legitimate input sources disappeared")
    require("L00C/L00CMenuActionLabModSystem.cs" not in names, "Existing exclusion was lost")
    leaked = [name for name in names if any(part in ("bin", "obj") for part in name.split("/")[:-1])]
    require(not leaked if fixed else bool(leaked), "Unexpected generated-source exclusion result")
    design = evaluate("design-time", True)
    require(names == sorted(item["Identity"].replace("\\", "/") for item in design["Items"]["Compile"]),
            "Design-time and build evaluations disagree")

    # Compile the evaluated list without game/package dependencies. SDK metadata
    # stays enabled, so stale attribute sources must reproduce the real CS0579.
    witness = root / "witness/CompilerWitness.csproj"
    xml = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    group = ET.SubElement(xml, "PropertyGroup")
    for key, value in {
        "TargetFramework": properties["TargetFramework"], "EnableDefaultCompileItems": "false",
        "GenerateAssemblyInfo": "true", "GenerateTargetFrameworkAttribute": "true",
        "Company": "CompilerWitness", "Product": "CompilerWitness", "AssemblyTitle": "CompilerWitness",
        "AssemblyVersion": "1.0.0.0", "FileVersion": "1.0.0.0", "InformationalVersion": "1.0.0",
    }.items():
        ET.SubElement(group, key).text = value
    group = ET.SubElement(xml, "ItemGroup")
    for item in items:
        ET.SubElement(group, "Compile", Include=item["FullPath"])
    write(witness, ET.tostring(xml, encoding="utf-8"))
    build = run(["dotnet", "build", str(witness), "--nologo", "-c", configuration,
                 "-p:UseSharedCompilation=false"], root, logs / "compiler.log")
    diagnostics = build.stdout + build.stderr
    if fixed:
        require(build.returncode == 0, "Fixed compiler witness failed; see compiler.log")
        again = run(["dotnet", "build", str(witness), "--no-restore", "--nologo", "-c", configuration,
                     "-p:UseSharedCompilation=false"], root, logs / "compiler-warm.log")
        require(again.returncode == 0, "Repeated build failed")
        warm = evaluate("evaluation-warm")
        require(names == sorted(item["Identity"].replace("\\", "/") for item in warm["Items"]["Compile"]),
                "Repeated evaluation changed the input set")
    else:
        require(build.returncode != 0, "Baseline unexpectedly compiled")
        for attribute in ATTRIBUTES:
            require(any("error CS0579:" in line and attribute in line for line in diagnostics.splitlines()),
                    f"Baseline did not reproduce CS0579 for {attribute}")
    return {"configuration": configuration, "relocated_outputs": relocated, "fixed": fixed,
            "evaluated_sources": names, "leaked_sources": leaked, "design_time_checked": True,
            "compiler_result": "PASS" if fixed else "EXPECTED_CS0579_ALL_EIGHT",
            "repeated_build": "PASS" if fixed else "NOT_APPLICABLE"}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository-root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--configuration", choices=("Debug", "Release"), required=True)
    parser.add_argument("--report-dir", type=Path, required=True)
    args = parser.parse_args()
    repo = args.repository_root.resolve()
    logs = args.report_dir.resolve()
    # Reports are never overwritten; all build output lives in a fresh temp root.
    logs.mkdir(parents=True, exist_ok=False)
    current = {name: (repo / name).read_bytes() for name in INPUTS}
    old = {}
    for name in INPUTS:
        process = subprocess.run(["git", "show", f"{BASELINE}:{name}"], cwd=repo,
                                 capture_output=True, check=True, timeout=30)
        old[name] = process.stdout
    report = {"scope": "REAL_PROJECT_ITEM_EVALUATION_AND_COMPILER_WITNESS",
              "baseline": BASELINE, "native_game": "NOT_RUN", "full_test_project_build": "NOT_RUN",
              "input_sha256": {name: hashlib.sha256(data).hexdigest() for name, data in current.items()},
              "cases": [], "status": "FAIL"}
    try:
        with tempfile.TemporaryDirectory(prefix="isr-cs0579-") as directory:
            temporary = Path(directory)
            for relocated in (False, True):
                for fixed, snapshot in ((False, old), (True, current)):
                    label = f"{'fixed' if fixed else 'baseline'}-{'relocated' if relocated else 'default'}"
                    case_logs = logs / label
                    case_logs.mkdir()
                    report["cases"].append(exercise(snapshot, temporary / label, args.configuration,
                                                     relocated, fixed, case_logs))
        require(all((repo / name).read_bytes() == data for name, data in current.items()),
                "A repository input changed during the test")
        report["status"] = "PASS"
    finally:
        (logs / "result.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
