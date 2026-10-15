#!/usr/bin/env python3
"""Build and inspect the current NuGet package; never reads a publishing secret or pushes."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]
PACKAGE = "W.DamengProvider"
REPOSITORY = "https://github.com/whynpc9/W.DamengProvider"
PROJECT = ROOT / "src/W.DamengProvider/W.DamengProvider.csproj"
FLAGS = ["-m:1", "/nodeReuse:false", "/p:UseSharedCompilation=false", "--disable-build-servers"]
SUITES = ("Configuration", "Session", "Transport", "Security", "Command", "Type",
          "Transaction", "Isolation", "Async", "Pool", "Lob", "Diagnostics", "Schema")
RESOURCE_CULTURES = ("en", "zh-CN", "zh-HK", "zh-TW")
ASSETS = {f"lib/net10.0/{PACKAGE}.dll"} | {
    f"lib/net10.0/{culture}/{PACKAGE}.resources.dll" for culture in RESOURCE_CULTURES}
# SemVer 2.0.0: numeric prerelease identifiers cannot have leading zeroes.
NUM = r"(?:0|[1-9][0-9]*)"
PRE = rf"(?:{NUM}|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)"
SEMVER = re.compile(rf"{NUM}\.{NUM}\.{NUM}(?:-{PRE}(?:\.{PRE})*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?", re.ASCII)


class Rejected(Exception):
    pass


def need(condition, reason):
    if not condition:
        raise Rejected(reason)


def version_from_tag(tag):
    need(tag.startswith("v") and SEMVER.fullmatch(tag[1:]) is not None, "tag_requires_v_and_strict_SemVer_2")
    return tag[1:]


def package_version(version):
    # NuGet identity and filenames omit SemVer build metadata. Assembly informational
    # version retains the complete tag version, which is separately checked at runtime.
    return version.split("+", 1)[0]


def sha(data):
    return hashlib.sha256(data).hexdigest()


def inspect_package(package, version, commit, root=ROOT):
    package = Path(package)
    identity = package_version(version)
    need(package.name == f"{PACKAGE}.{identity}.nupkg", "package_filename_mismatch")
    with zipfile.ZipFile(package) as archive:
        entries = archive.namelist()
        need(len(entries) == len(set(entries)), "duplicate_zip_entry")
        need(all(not n.startswith("/") and ".." not in Path(n).parts and "\\" not in n for n in entries), "unsafe_zip_entry")
        allowed = ASSETS | {"README.md", "LICENSE", "THIRD-PARTY-NOTICES.md", f"{PACKAGE}.nuspec",
                            "[Content_Types].xml", "_rels/.rels"}
        need(all(n in allowed or re.fullmatch(r"package/services/metadata/core-properties/[^/]+\.psmdcp", n)
                 for n in entries), "unexpected_package_asset")
        need(ASSETS <= set(entries), "managed_or_satellite_asset_missing")
        metadata = ET.fromstring(archive.read(f"{PACKAGE}.nuspec"))
        # NuGet uses different nuspec namespace revisions; compare local names.
        metadata = next((e for e in metadata if e.tag.rsplit("}", 1)[-1] == "metadata"), None)
        need(metadata is not None, "nuspec_metadata_missing")
        fields = {e.tag.rsplit("}", 1)[-1]: e for e in metadata}
        value = lambda name: fields[name].text if name in fields else None
        need(value("id") == PACKAGE and value("version") == identity, "nuspec_identity_mismatch")
        need(value("license") == "Apache-2.0" and fields["license"].get("type") == "expression", "license_mismatch")
        need(value("readme") == "README.md", "readme_metadata_mismatch")
        repository = fields.get("repository")
        need(repository is not None and repository.attrib == {"type": "git", "url": REPOSITORY, "commit": commit}, "repository_commit_mismatch")
        dependencies = fields.get("dependencies")
        need(dependencies is not None and len(dependencies) == 1 and
             dependencies[0].tag.rsplit("}", 1)[-1] == "group" and
             dependencies[0].get("targetFramework") == "net10.0" and not len(dependencies[0]), "unexpected_package_dependency")
        for name in ("README.md", "LICENSE", "THIRD-PARTY-NOTICES.md"):
            need(archive.read(name) == (root / name).read_bytes(), "embedded_document_mismatch_" + name)
        assets = {name: sha(archive.read(name)) for name in sorted(ASSETS)}
    return {"id": PACKAGE, "version": identity, "tag_version": version, "repository_commit": commit,
            "package_sha256": sha(package.read_bytes()), "assets": assets}


def inspect_trx(path):
    root = ET.parse(path).getroot()
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    summary = root.find("t:ResultSummary", ns)
    counters = root.find("t:ResultSummary/t:Counters", ns)
    need(summary is not None and summary.get("outcome") in ("Completed", "Passed") and counters is not None,
         "test_result_missing_or_failed")
    counts = {key: int(value) for key, value in counters.attrib.items()}
    required = {"total", "executed", "passed", "failed", "error", "timeout", "aborted", "inconclusive",
                "passedButRunAborted", "notRunnable", "notExecuted", "disconnected", "warning", "completed", "inProgress", "pending"}
    need(required <= counts.keys(), "test_counters_missing")
    need(counts.get("total", 0) > 0 and counts.get("executed") == counts["total"] and counts.get("passed") == counts["total"] and
         all(value == 0 for key, value in counts.items() if key not in ("total", "executed", "passed")), "tests_failed_skipped_or_incomplete")
    return counts


def run(argv, cwd, env, results, stage):
    log = results / (stage + ".log")
    print(stage, flush=True)
    with log.open("w") as stream:
        outcome = subprocess.run([str(v) for v in argv], cwd=cwd, env=env, stdout=stream, stderr=subprocess.STDOUT)
    with (results / "commands.jsonl").open("a") as stream:
        stream.write(json.dumps({"stage": stage, "argv": [str(v) for v in argv], "exit_code": outcome.returncode}) + "\n")
    if outcome.returncode:
        # Commands run without database credentials; keep compiler/test diagnostics visible.
        print(log.read_text(errors="replace")[-16000:], file=sys.stderr)
        raise Rejected(stage + "_failed")


def environment(results):
    env = {k: v for k, v in os.environ.items() if not k.upper().startswith(("DAMENG", "NUGET_API_KEY"))}
    cli_home = results / "cli-home"
    cli_home.mkdir()
    env.update(DOTNET_CLI_HOME=str(cli_home), DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1", DOTNET_CLI_TELEMETRY_OPTOUT="1")
    return env


def build_consumer(dotnet, source, name, identity, version, feed, results, env, include_fixture=False):
    consumer = results / name
    consumer.mkdir()
    for path in source:
        shutil.copyfile(path, consumer / path.name)
    if include_fixture:
        shutil.copyfile(ROOT / "tests/W.DmProvider.DiagnosticsTests/ScriptedTransactionFixture.cs", consumer / "ScriptedTransactionFixture.cs")
    # The copied consumer has no ProjectReference: it restores only the packed
    # NuGet asset from the local feed into its own new package cache.
    document = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    props = ET.SubElement(document, "PropertyGroup")
    for key, value in {"OutputType": "Exe", "TargetFramework": "net10.0", "AssemblyName": name,
                       "Nullable": "enable", "ImplicitUsings": "enable", "NuGetAudit": "false"}.items():
        ET.SubElement(props, key).text = value
    items = ET.SubElement(document, "ItemGroup")
    ET.SubElement(items, "PackageReference", Include=PACKAGE, Version=f"[{identity}]")
    ET.SubElement(items, "AssemblyMetadata", Include="R3PackageVersion", Value=identity)
    project = consumer / (name + ".csproj")
    ET.ElementTree(document).write(project, encoding="utf-8", xml_declaration=True)
    # A fresh package cache and a sole local feed prove this exact package is used.
    consumer_env = {**env, "NUGET_PACKAGES": str(consumer / "packages")}
    run([dotnet, "restore", project, "--source", feed, *FLAGS], consumer, consumer_env, results, name + "_restore")
    app = consumer / "app"
    run([dotnet, "build", project, "--no-restore", "-c", "Release", "--output", app, *FLAGS], consumer, consumer_env, results, name + "_build")
    assets = json.loads((consumer / "obj/project.assets.json").read_text())
    key = f"{PACKAGE}/{identity}"
    need(set(assets["libraries"]) == {key} and assets["libraries"][key]["type"] == "package", "consumer_requires_sole_PackageReference")
    deps = json.loads((app / (name + ".deps.json")).read_text())
    need(deps["libraries"].get(key, {}).get("type") == "package", "consumer_runtime_package_reference_missing")
    return app


def prepare(args):
    version = version_from_tag(args.tag)
    need(re.fullmatch(r"[0-9a-f]{40}|[0-9a-f]{64}", args.commit) is not None, "repository_commit_invalid")
    actual = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    need(actual == args.commit, "repository_commit_does_not_match_checkout")
    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    results = Path(tempfile.mkdtemp(prefix="nuget-", dir=output))
    env = environment(results)
    dotnet = shutil.which(os.environ.get("DOTNET_COMMAND", "dotnet"))
    need(dotnet is not None, "dotnet_not_found")
    sdk = json.loads((ROOT / "global.json").read_text())["sdk"]
    need(sdk.get("rollForward") == "disable", "exact_sdk_required")
    run([dotnet, "--version"], ROOT, env, results, "sdk")
    need((results / "sdk.log").read_text().strip() == sdk["version"], "sdk_version_mismatch")
    if args.command == "prepare":
        for name in SUITES:
            project = ROOT / f"tests/W.DmProvider.{name}Tests/W.DmProvider.{name}Tests.csproj"
            run([dotnet, "test", project, "-c", "Release", "--logger", "trx;LogFileName=" + name + ".trx",
                 "--results-directory", results, *FLAGS], ROOT, env, results, "test_" + name)
            inspect_trx(results / (name + ".trx"))
    feed = results / "feed"
    feed.mkdir()
    identity = package_version(version)
    run([dotnet, "pack", PROJECT, "-c", "Release", "--output", feed,
         "/p:Version=" + version, "/p:PackageVersion=" + identity,
         "/p:RepositoryCommit=" + args.commit, "/p:ContinuousIntegrationBuild=true", *FLAGS], ROOT, env, results, "pack")
    package = feed / f"{PACKAGE}.{identity}.nupkg"
    manifest = inspect_package(package, version, args.commit)
    manifest_path = results / "package-manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    smoke = build_consumer(dotnet, [ROOT / "tools/NuGetRelease/Smoke.cs"], "NuGetSmoke", identity, version, feed, results, env)
    run([dotnet, smoke / "NuGetSmoke.dll", "offline", manifest_path], ROOT, env, results, "package_smoke")
    resource_sources = list((ROOT / "tools/R3ResourceProbe").glob("*.cs"))
    resource = build_consumer(dotnet, resource_sources, "R3ResourceProbe", identity, version, feed, results, env, include_fixture=True)
    run([dotnet, resource / "R3ResourceProbe.dll", "offline", ROOT, manifest_path, results / "first-init.json"],
        ROOT, env, results, "first_init")
    first_init = json.loads((results / "first-init.json").read_text())
    need(first_init.get("accepted") is True and first_init.get("status") == "offline_verified" and
         {case.get("point") for case in first_init.get("first_init_cases", [])} ==
         {"normal", "should_listen", "instrument_published", "sample", "started", "stopped", "measurement"},
         "first_initialization_matrix_incomplete")
    need(inspect_package(package, version, args.commit) == manifest, "package_changed_during_verification")
    (output / "package-path.txt").write_text(str(package) + "\n")
    (output / "smoke-path.txt").write_text(str(smoke / "NuGetSmoke.dll") + "\n")
    (output / "manifest-path.txt").write_text(str(manifest_path) + "\n")
    print(json.dumps({"status": "prepared", "package": str(package), "results": str(results),
                      "offline_suites": len(SUITES) if args.command == "prepare" else 0,
                      "integration": "integration_pending"}))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    tag = commands.add_parser("version")
    tag.add_argument("tag")
    for command in ("prepare", "pack-smoke"):
        p = commands.add_parser(command)
        p.add_argument("--tag", required=True)
        p.add_argument("--commit", required=True)
        p.add_argument("--output", default=str(ROOT / ".local/nuget-release"))
    args = parser.parse_args()
    try:
        if args.command == "version":
            print(version_from_tag(args.tag))
        else:
            prepare(args)
    except (Rejected, OSError, ValueError, zipfile.BadZipFile, ET.ParseError, subprocess.CalledProcessError) as error:
        print("release preparation rejected: " + str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
