import json
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zipfile

import release


class VersionContracts(unittest.TestCase):
    def test_accepts_strict_semver_tags(self):
        for tag in ("v0.0.0", "v1.2.3", "v10.20.30-preview.1", "v1.2.3-0.A-b.123", "v1.2.3+build.01", "v1.2.3-rc.1+sha.abc"):
            with self.subTest(tag=tag):
                self.assertEqual(tag[1:], release.version_from_tag(tag))

    def test_rejects_malformed_or_ambiguous_versions(self):
        for tag in ("", "1.2.3", "V1.2.3", "v1", "v1.2", "v01.2.3", "v1.02.3", "v1.2.03",
                    "v1.2.3.4", "v1.2.3-", "v1.2.3+", "v1.2.3-alpha..1", "v1.2.3-01", "v1.2.3-alpha.01",
                    "v1.2.3+build..id", "v1.2.3-ß", "v1.2.3\n", " v1.2.3", "v1.2.3/evil", "v1.2.3;echo bad"):
            with self.subTest(tag=tag), self.assertRaises(release.Rejected):
                release.version_from_tag(tag)

    def test_nuget_identity_omits_build_metadata(self):
        self.assertEqual("1.2.3-rc.1", release.package_version("1.2.3-rc.1+build.12"))


class PackageContracts(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.version = "1.2.3-preview.1"
        self.commit = "a" * 40
        for name in ("README.md", "LICENSE", "THIRD-PARTY-NOTICES.md"):
            (self.root / name).write_text("checked " + name)

    def package(self, change=None):
        metadata = ET.Element("metadata")
        for name, value in {"id": release.PACKAGE, "version": self.version, "readme": "README.md"}.items():
            ET.SubElement(metadata, name).text = value
        ET.SubElement(metadata, "license", type="expression").text = "Apache-2.0"
        ET.SubElement(metadata, "repository", type="git", url=release.REPOSITORY, commit=self.commit)
        dependencies = ET.SubElement(metadata, "dependencies")
        for tfm in release.TFMS:
            group = ET.SubElement(dependencies, "group", targetFramework=tfm)
            for name, version in release.direct_dependencies(tfm).items():
                ET.SubElement(group, "dependency", id=name, version=version)
        package = ET.Element("package"); package.append(metadata)
        assets = {asset: b"synthetic contract-only binary" for asset in release.ASSETS}
        assets.update({name: (self.root / name).read_bytes() for name in ("README.md", "LICENSE", "THIRD-PARTY-NOTICES.md")})
        if change:
            change(metadata, assets)
        assets[release.PACKAGE + ".nuspec"] = ET.tostring(package)
        path = self.root / (release.PACKAGE + "." + self.version + ".nupkg")
        with zipfile.ZipFile(path, "w") as archive:
            for name, data in assets.items():
                archive.writestr(name, data)
        return path

    def check(self, change=None):
        return release.inspect_package(self.package(change), self.version, self.commit, self.root)

    def test_accepts_expected_metadata_and_documents_and_assets(self):
        result = self.check()
        self.assertEqual(release.ASSETS, result["assets"].keys())
        self.assertEqual(self.commit, result["repository_commit"])

    def test_rejects_wrong_package_id_or_version(self):
        for field in ("id", "version"):
            with self.subTest(field=field), self.assertRaises(release.Rejected):
                self.check(lambda metadata, _: setattr(metadata.find(field), "text", "wrong"))

    def test_rejects_license_or_readme_metadata_mismatch(self):
        for field in ("license", "readme"):
            with self.subTest(field=field), self.assertRaises(release.Rejected):
                self.check(lambda metadata, _: setattr(metadata.find(field), "text", "wrong"))

    def test_rejects_file_license_instead_of_expression(self):
        with self.assertRaises(release.Rejected):
            self.check(lambda metadata, _: metadata.find("license").set("type", "file"))

    def test_rejects_wrong_or_absent_repository_commit(self):
        for value in ("", "b" * 40):
            with self.subTest(value=value), self.assertRaises(release.Rejected):
                self.check(lambda metadata, _: metadata.find("repository").set("commit", value))

    def test_rejects_wrong_repository_url(self):
        with self.assertRaises(release.Rejected):
            self.check(lambda metadata, _: metadata.find("repository").set("url", "https://example.invalid"))

    def test_rejects_modified_embedded_documents(self):
        for name in ("README.md", "LICENSE", "THIRD-PARTY-NOTICES.md"):
            with self.subTest(name=name), self.assertRaises(release.Rejected):
                self.check(lambda _, assets: assets.update({name: b"stale"}))

    def test_rejects_missing_target(self):
        for tfm in release.TFMS:
            with self.subTest(tfm=tfm), self.assertRaises(release.Rejected):
                self.check(lambda _, assets: assets.pop(f"lib/{tfm}/W.DamengProvider.dll"))

    def test_rejects_missing_satellite(self):
        for tfm in release.TFMS:
            for culture in release.RESOURCE_CULTURES:
                with self.subTest(tfm=tfm, culture=culture), self.assertRaises(release.Rejected):
                    self.check(lambda _, assets: assets.pop(f"lib/{tfm}/{culture}/W.DamengProvider.resources.dll"))

    def test_rejects_wrong_dependency_group(self):
        for tfm in release.TFMS:
            with self.subTest(tfm=tfm), self.assertRaises(release.Rejected):
                self.check(lambda metadata, _: metadata.find(f"dependencies/group[@targetFramework='{tfm}']").set("targetFramework", "net7.0"))

    def test_rejects_wrong_compatibility_version_or_dependency_on_newer_target(self):
        for tfm in ("net6.0", "net8.0"):
            with self.subTest(tfm=tfm), self.assertRaises(release.Rejected):
                self.check(lambda metadata, _: metadata.find(f"dependencies/group[@targetFramework='{tfm}']/dependency")
                           .set("version", "10.0.11"))
        for tfm, name in (("net9.0", "Microsoft.Bcl.Cryptography"), ("net10.0", "Microsoft.Bcl.Cryptography"),
                          ("net8.0", "Microsoft.Bcl.TimeProvider"), ("net9.0", "Microsoft.Bcl.TimeProvider")):
            with self.subTest(tfm=tfm, name=name), self.assertRaises(release.Rejected):
                self.check(lambda metadata, _: ET.SubElement(metadata.find(f"dependencies/group[@targetFramework='{tfm}']"),
                           "dependency", id=name, version="10.0.12"))

    def test_rejects_extra_native_or_official_or_source_assets(self):
        for name in ("lib/net10.0/DM.DmProvider.dll", "runtimes/linux-x64/native/libdm.so", "src/secret.cs", "lib/net7.0/W.DamengProvider.dll"):
            with self.subTest(name=name), self.assertRaises(release.Rejected):
                self.check(lambda _, assets: assets.update({name: b"unexpected"}))

    def test_rejects_unexpected_dependency(self):
        with self.assertRaises(release.Rejected):
            self.check(lambda metadata, _: ET.SubElement(metadata.find("dependencies/group"), "dependency", id="DM.DmProvider", version="1.0.0"))

    def test_rejects_traversal(self):
        with self.assertRaises(release.Rejected):
            self.check(lambda _, assets: assets.update({"../outside": b"unsafe"}))


class ConsumerContracts(unittest.TestCase):
    def consumer(self, tfm):
        identity = "1.2.3"
        key = release.PACKAGE + "/" + identity
        asset = f"lib/{tfm}/{release.PACKAGE}.dll"
        target = {key: {"compile": {asset: {}}, "runtime": {asset: {}}}}
        if release.direct_dependencies(tfm):
            target[key]["dependencies"] = dict(release.direct_dependencies(tfm))
        for name, edges in release.dependency_closure(tfm).items():
            target[name] = {"dependencies": dict(edges)} if edges else {}
        libraries = {name: {"type": "package"} for name in target}
        assets = {"libraries": libraries, "targets": {tfm: target}, "project": {"frameworks": {tfm: {
            "dependencies": {release.PACKAGE: {"version": "[1.2.3]"}}}}}}
        deps = {"libraries": {**libraries, "Smoke/1.0.0": {"type": "project"}}, "targets": {tfm: {key: {"runtime": {asset: {}}}}}}
        return assets, deps, identity, key

    def test_accepts_approved_closure_for_each_target(self):
        for tfm in release.TFMS:
            assets, deps, identity, _ = self.consumer(tfm)
            self.assertEqual(tfm, release.inspect_consumer(assets, deps, tfm, identity)["tfm"])

    def test_rejects_wrong_compile_or_runtime_asset(self):
        for tfm in release.TFMS:
            for kind in ("compile", "runtime", "deps"):
                assets, deps, identity, key = self.consumer(tfm)
                if kind == "deps":
                    deps["targets"][tfm][key]["runtime"] = {"lib/net7.0/W.DamengProvider.dll": {}}
                else:
                    assets["targets"][tfm][key][kind] = {"lib/net7.0/W.DamengProvider.dll": {}}
                with self.subTest(tfm=tfm, kind=kind), self.assertRaises(release.Rejected):
                    release.inspect_consumer(assets, deps, tfm, identity)

    def test_rejects_unapproved_or_missing_dependency_and_direct_transitive_reference(self):
        for tfm, direct, transitive in (("net6.0", "Microsoft.Bcl.TimeProvider", "Microsoft.Bcl.AsyncInterfaces/8.0.0"),
                                        ("net8.0", "Microsoft.Bcl.Cryptography", "System.Formats.Asn1/10.0.12")):
            for mutation in ("extra", "missing", "direct", "edge", "transitive_edge", "runtime"):
                assets, deps, identity, key = self.consumer(tfm)
                if mutation == "extra": assets["libraries"]["DM.DmProvider/1.0.0"] = {"type": "package"}
                if mutation == "missing": assets["libraries"].pop(transitive)
                if mutation == "direct": assets["project"]["frameworks"][tfm]["dependencies"][direct] = {}
                if mutation == "edge": assets["targets"][tfm][key]["dependencies"] = {}
                if mutation == "transitive_edge": assets["targets"][tfm][transitive]["dependencies"] = {"DM.DmProvider": "1.0.0"}
                if mutation == "runtime": deps["libraries"].pop(transitive)
                with self.subTest(tfm=tfm, mutation=mutation), self.assertRaises(release.Rejected):
                    release.inspect_consumer(assets, deps, tfm, identity)

    def test_package_cache_path_normalizes_uppercase_prerelease(self):
        self.assertEqual(Path("packages/w.damengprovider/1.2.3-rc.1/w.damengprovider.1.2.3-rc.1.nupkg"),
                         release.cached_product_package(Path("packages"), "1.2.3-RC.1"))

    def test_product_source_mapping_has_only_exact_local_feed(self):
        with tempfile.TemporaryDirectory() as temp:
            destination = Path(temp) / "NuGet.Config"
            release.consumer_sources(Path(temp) / "feed", destination)
            root = ET.parse(destination).getroot()
            mapping = root.find("packageSourceMapping")
            self.assertEqual([release.PACKAGE], [e.get("pattern") for e in mapping.find("packageSource[@key='candidate']")])
            remote = {e.get("pattern") for e in mapping.find("packageSource[@key='approved']")}
            self.assertEqual(set(release.APPROVED_TRANSITIVE) | {"Microsoft.NETCore.App.Ref", "Microsoft.AspNetCore.App.Ref"}, remote)
            self.assertNotIn(release.PACKAGE, remote)


class TestResultContracts(unittest.TestCase):
    def test_no_skips_and_complete_counters_required(self):
        attributes = {name: "0" for name in ("failed", "error", "timeout", "aborted", "inconclusive", "passedButRunAborted",
                      "notRunnable", "notExecuted", "disconnected", "warning", "completed", "inProgress", "pending")}
        attributes.update(total="10", executed="10", passed="10")
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "test.trx"
            for mutation in (None, "notExecuted", "failed", "missing_counter", "no_tests"):
                changed = dict(attributes)
                if mutation in ("notExecuted", "failed"):
                    changed[mutation] = "1"; changed["passed"] = "9"
                if mutation == "missing_counter":
                    changed.pop("notExecuted")
                if mutation == "no_tests":
                    changed.update(total="0", executed="0", passed="0")
                root = ET.Element("TestRun", xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010")
                summary = ET.SubElement(root, "ResultSummary", outcome="Completed")
                ET.SubElement(summary, "Counters", **changed)
                ET.ElementTree(root).write(path)
                if mutation:
                    with self.subTest(mutation=mutation), self.assertRaises(release.Rejected):
                        release.inspect_trx(path)
                else:
                    self.assertEqual(10, release.inspect_trx(path)["passed"])


if __name__ == "__main__":
    unittest.main()
