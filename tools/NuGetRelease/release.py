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
          "Transaction", "Isolation", "Async", "Pool", "Lob", "Diagnostics")
TFMS = ("net8.0", "net9.0", "net10.0")
COMPATIBILITY_VERSION = "10.0.12"
APPROVED_TRANSITIVE = ("Microsoft.Bcl.Cryptography", "System.Formats.Asn1")
RESOURCE_CULTURES = ("en", "zh-CN", "zh-HK", "zh-TW")
ASSETS = {f"lib/{tfm}/{PACKAGE}.dll" for tfm in TFMS} | {
    f"lib/{tfm}/{culture}/{PACKAGE}.resources.dll" for tfm in TFMS for culture in RESOURCE_CULTURES}
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
        need(dependencies is not None and len(dependencies) == len(TFMS), "unexpected_package_dependency")
        groups = {group.get("targetFramework"): group for group in dependencies}
        need(set(groups) == set(TFMS) and all(g.tag.rsplit("}", 1)[-1] == "group" for g in dependencies),
             "unexpected_package_dependency_group")
        for tfm, group in groups.items():
            expected = {"Microsoft.Bcl.Cryptography": COMPATIBILITY_VERSION} if tfm == "net8.0" else {}
            actual = {d.get("id"): d.get("version") for d in group}
            need(len(group) == len(expected) and actual == expected and
                 all(d.tag.rsplit("}", 1)[-1] == "dependency" for d in group), "unexpected_package_dependency")
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
    env.update(DOTNET_ROLL_FORWARD="Minor", DOTNET_CLI_HOME=str(cli_home), DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1", DOTNET_CLI_TELEMETRY_OPTOUT="1")
    return env


def inspect_consumer(assets, deps, tfm, identity):
    """Validate the resolved graph, direct reference and compile/runtime product asset."""
    key = f"{PACKAGE}/{identity}"
    expected = {key} | ({f"{name}/{COMPATIBILITY_VERSION}" for name in APPROVED_TRANSITIVE} if tfm == "net8.0" else set())
    need(set(assets["libraries"]) == expected and all(v["type"] == "package" for v in assets["libraries"].values()),
         "consumer_dependency_closure_mismatch")
    framework = assets["project"]["frameworks"][tfm]
    need(set(framework.get("dependencies", {})) == {PACKAGE}, "consumer_requires_sole_direct_PackageReference")
    need(set(assets["targets"]) == {tfm}, "consumer_target_mismatch")
    target = assets["targets"][tfm]
    need(set(target) == expected, "consumer_target_dependency_closure_mismatch")
    selected = f"lib/{tfm}/{PACKAGE}.dll"
    for kind in ("compile", "runtime"):
        need(set(target[key].get(kind, {})) == {selected}, "consumer_product_asset_mismatch")
    expected_edges = {"Microsoft.Bcl.Cryptography": COMPATIBILITY_VERSION} if tfm == "net8.0" else {}
    need(target[key].get("dependencies", {}) == expected_edges, "consumer_product_dependency_mismatch")
    if tfm == "net8.0":
        need(target[f"Microsoft.Bcl.Cryptography/{COMPATIBILITY_VERSION}"].get("dependencies", {}) ==
             {"System.Formats.Asn1": COMPATIBILITY_VERSION} and
             not target[f"System.Formats.Asn1/{COMPATIBILITY_VERSION}"].get("dependencies"),
             "consumer_transitive_dependency_mismatch")
    runtime_packages = {k for k, v in deps["libraries"].items() if v.get("type") == "package"}
    need(runtime_packages == expected, "consumer_runtime_dependency_closure_mismatch")
    runtime_targets = list(deps["targets"].values())
    need(len(runtime_targets) == 1 and set(runtime_targets[0].get(key, {}).get("runtime", {})) == {selected},
         "consumer_runtime_asset_mismatch")
    return {"tfm": tfm, "product_asset": selected, "dependencies": sorted(expected)}


def cached_product_package(package_root, identity):
    # NuGet's global-package paths use normalized lowercase IDs and versions.
    normalized = identity.lower()
    return package_root / PACKAGE.lower() / normalized / f"{PACKAGE.lower()}.{normalized}.nupkg"


def consumer_sources(feed, destination):
    config = ET.Element("configuration")
    sources = ET.SubElement(config, "packageSources")
    ET.SubElement(sources, "clear")
    ET.SubElement(sources, "add", key="candidate", value=str(feed))
    ET.SubElement(sources, "add", key="approved", value="https://api.nuget.org/v3/index.json")
    mapping = ET.SubElement(config, "packageSourceMapping")
    ET.SubElement(mapping, "clear")
    local = ET.SubElement(mapping, "packageSource", key="candidate")
    ET.SubElement(local, "package", pattern=PACKAGE)
    approved = ET.SubElement(mapping, "packageSource", key="approved")
    for name in (*APPROVED_TRANSITIVE, "Microsoft.NETCore.App.Ref", "Microsoft.AspNetCore.App.Ref"):
        ET.SubElement(approved, "package", pattern=name)
    ET.ElementTree(config).write(destination, encoding="utf-8", xml_declaration=True)


def build_consumer(dotnet, source, name, identity, version, tfm, feed, results, env, include_fixture=False):
    consumer = results / tfm / name
    consumer.mkdir(parents=True)
    for path in source:
        shutil.copyfile(path, consumer / path.name)
    if include_fixture:
        shutil.copyfile(ROOT / "tests/W.DmProvider.DiagnosticsTests/ScriptedTransactionFixture.cs", consumer / "ScriptedTransactionFixture.cs")
    document = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    props = ET.SubElement(document, "PropertyGroup")
    for key, value in {"OutputType": "Exe", "TargetFramework": tfm, "AssemblyName": name,
                       "LangVersion": "12.0", "RollForward": "Minor", "UseAppHost": "false", "CheckEolTargetFramework": "false",
                       "Nullable": "enable", "ImplicitUsings": "enable", "NuGetAudit": "false"}.items():
        ET.SubElement(props, key).text = value
    items = ET.SubElement(document, "ItemGroup")
    ET.SubElement(items, "PackageReference", Include=PACKAGE, Version=f"[{identity}]")
    ET.SubElement(items, "AssemblyMetadata", Include="R3PackageVersion", Value=identity)
    project = consumer / (name + ".csproj")
    ET.ElementTree(document).write(project, encoding="utf-8", xml_declaration=True)
    config = consumer / "NuGet.Config"
    consumer_sources(feed, config)
    # No inherited cache or alternate product source may satisfy this reference.
    consumer_env = {**env, "NUGET_PACKAGES": str(consumer / "packages")}
    stage = tfm + "_" + name
    run([dotnet, "restore", project, "--configfile", config, "--no-cache", *FLAGS], consumer, consumer_env, results, stage + "_restore")
    app = consumer / "app"
    run([dotnet, "build", project, "--no-restore", "-c", "Release", "--output", app, *FLAGS], consumer, consumer_env, results, stage + "_build")
    assets = json.loads((consumer / "obj/project.assets.json").read_text())
    deps = json.loads((app / (name + ".deps.json")).read_text())
    evidence = inspect_consumer(assets, deps, tfm, identity)
    cached = cached_product_package(consumer / "packages", identity)
    need(sha(cached.read_bytes()) == sha((feed / f"{PACKAGE}.{identity}.nupkg").read_bytes()), "consumer_product_source_mismatch")
    (results / (stage + "_assets.json")).write_text(json.dumps(evidence, indent=2) + "\n")
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
        for tfm in TFMS:
            for name in SUITES:
                project = ROOT / f"tests/W.DmProvider.{name}Tests/W.DmProvider.{name}Tests.csproj"
                result = f"{tfm}_{name}"
                run([dotnet, "test", project, "-f", tfm, "-c", "Release", "--logger", "trx;LogFileName=" + result + ".trx",
                     "--results-directory", results, *FLAGS], ROOT, env, results, "test_" + result)
                inspect_trx(results / (result + ".trx"))
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
    consumers = []
    for tfm in TFMS:
        smoke = build_consumer(dotnet, [ROOT / "tools/NuGetRelease/Smoke.cs"], "NuGetSmoke", identity, version, tfm, feed, results, env)
        run([dotnet, smoke / "NuGetSmoke.dll", "offline", manifest_path], ROOT, env, results, tfm + "_package_smoke")
        resource_sources = list((ROOT / "tools/R3ResourceProbe").glob("*.cs"))
        resource = build_consumer(dotnet, resource_sources, "R3ResourceProbe", identity, version, tfm, feed, results, env, include_fixture=True)
        init_path = results / ("first-init-" + tfm + ".json")
        run([dotnet, resource / "R3ResourceProbe.dll", "offline", ROOT, manifest_path, init_path],
            ROOT, env, results, tfm + "_first_init")
        first_init = json.loads(init_path.read_text())
        need(first_init.get("accepted") is True and first_init.get("status") == "offline_verified" and
             first_init.get("target_framework") == tfm and first_init.get("runtime_major") == int(tfm[3:].split(".")[0]) and
             {case.get("point") for case in first_init.get("first_init_cases", [])} ==
             {"normal", "should_listen", "instrument_published", "sample", "started", "stopped", "measurement"},
             "first_initialization_matrix_incomplete")
        consumers.append({"tfm": tfm, "smoke": str(smoke / "NuGetSmoke.dll"), "resource_probe": str(resource / "R3ResourceProbe.dll"),
                          "runtime_major": first_init["runtime_major"]})
    need(inspect_package(package, version, args.commit) == manifest, "package_changed_during_verification")
    (output / "package-path.txt").write_text(str(package) + "\n")
    (output / "smoke-path.txt").write_text(consumers[-1]["smoke"] + "\n")
    for consumer in consumers:
        (output / ("smoke-path-" + consumer["tfm"] + ".txt")).write_text(consumer["smoke"] + "\n")
    (output / "consumers.json").write_text(json.dumps(consumers, indent=2) + "\n")
    (output / "manifest-path.txt").write_text(str(manifest_path) + "\n")
    print(json.dumps({"status": "prepared", "package": str(package), "results": str(results),
                      "target_frameworks": list(TFMS), "offline_suites": len(SUITES) * len(TFMS) if args.command == "prepare" else 0,
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
