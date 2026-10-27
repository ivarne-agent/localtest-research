"""Unit tests for vendor-sync.py: python3 -m unittest discover -s localtest/tools (standard library only).

The parser and the metadata are tested on strings; check, render and sync on throwaway git repositories: an "upstream"
repository with two commits, a blobless clone of it, and an "ours" repository with a component vendoring it.
"""
import contextlib
import importlib.util
import io
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

_spec = importlib.util.spec_from_file_location("vendor_sync", Path(__file__).with_name("vendor-sync.py"))
vs = importlib.util.module_from_spec(_spec)
sys.modules["vendor_sync"] = vs
_spec.loader.exec_module(vs)


def setUpModule():
    os.environ.update(GIT_CONFIG_GLOBAL=os.devnull, GIT_CONFIG_NOSYSTEM="1", GIT_AUTHOR_NAME="t",
                      GIT_AUTHOR_EMAIL="t@example.com", GIT_COMMITTER_NAME="t", GIT_COMMITTER_EMAIL="t@example.com")


# ---------------------------------------------------------------------------------------------------------------------
# The parser

PROGRAM = b"""var builder = WebApplication.CreateBuilder(args);
#if LOCALTEST
Localtest.Add(builder);
#endif

#if !LOCALTEST
services.AddAzureMonitor();
#else
services.AddOtlp();
#endif
app.Run();
"""
UPSTREAM_PROGRAM = b"""var builder = WebApplication.CreateBuilder(args);

services.AddAzureMonitor();
app.Run();
"""


class ParserTests(unittest.TestCase):
    def test_addition_and_replacement(self):
        p = vs.parse(PROGRAM)
        self.assertEqual([(b.kind, b.start, b.end) for b in p.blocks], [("addition", 2, 4), ("replacement", 6, 10)])
        self.assertEqual(p.blocks[1].else_line, 8)
        self.assertEqual(p.production, UPSTREAM_PROGRAM)
        self.assertEqual(p.production_lines, [1, 5, 7, 11])
        self.assertIn(b"services.AddOtlp();", p.localtest)
        self.assertNotIn(b"AddAzureMonitor", p.localtest)
        self.assertEqual(p.blocks[0].first_local_line(), "Localtest.Add(builder);")

    def test_directive_spacing_and_trailing_comment(self):
        p = vs.parse(b"a\n  #  if LOCALTEST // localtest: why\nb\n  #endif // done\nc\n")
        self.assertEqual(p.production, b"a\nc\n")

    def test_nested_preprocessor_inside_branches(self):
        data = (b"a\n#if LOCALTEST\n#if DEBUG\nx\n#else\ny\n#endif\n#endif\n"
                b"#if !LOCALTEST\n#if NET8_0\np\n#elif NET9_0\nq\n#endif\n#else\nr\n#endif\nz\n")
        p = vs.parse(data)
        self.assertEqual(len(p.blocks), 2)
        self.assertEqual(p.production, b"a\n#if NET8_0\np\n#elif NET9_0\nq\n#endif\nz\n")

    def test_block_inside_upstream_conditional(self):
        p = vs.parse(b"#if DEBUG\na\n#if LOCALTEST\nb\n#endif\n#else\nc\n#endif\n")
        self.assertEqual(p.production, b"#if DEBUG\na\n#else\nc\n#endif\n")

    def test_refused_shapes(self):
        cases = {
            b"#if LOCALTEST\na\n#elif DEBUG\nb\n#endif\n": (3, "#elif"),
            b"a\n#if !LOCALTEST\nb\n#endif\n": (2, "without #else"),
            b"#if LOCALTEST\na\n#else\nb\n#endif\n": (3, "#else in the `#if LOCALTEST`"),
            b"#if !LOCALTEST\na\n#else\nb\n#else\nc\n#endif\n": (5, "second #else"),
            b"#if LOCALTEST\n#if !LOCALTEST\na\n#else\n#endif\n#endif\n": (2, "inside the block"),
            b"#if LOCALTEST || DEBUG\na\n#endif\n": (1, "unsupported"),
            b"x\n#if LOCALTEST\na\n": (2, "without #endif"),
        }
        for data, (line, text) in cases.items():
            with self.subTest(data=data):
                with self.assertRaises(vs.DirectiveError) as ctx:
                    vs.parse(data)
                self.assertEqual(ctx.exception.line, line)
                self.assertIn(text, ctx.exception.message)

    def test_crlf_lines_keep_their_endings(self):
        data = PROGRAM.replace(b"\n", b"\r\n")
        p = vs.parse(data)
        self.assertEqual(p.production, UPSTREAM_PROGRAM.replace(b"\n", b"\r\n"))
        self.assertEqual(vs.tolerated_difference(p.production, UPSTREAM_PROGRAM), "line endings")

    def test_last_line_without_newline(self):
        p = vs.parse(b"a\n#if LOCALTEST\nb\n#endif")
        self.assertEqual(p.production, b"a\n")

    def test_has_directives(self):
        self.assertTrue(vs.has_directives(PROGRAM))
        self.assertFalse(vs.has_directives(b"// LOCALTEST is mentioned here\n#if DEBUG\n#endif\n"))

    def test_removal_candidates(self):
        p = vs.parse(b"#if !LOCALTEST\n  a(1);\n#else\n// localtest: same\na(1);\n#endif\n"
                     b"#if LOCALTEST\n// only a comment\n#endif\n#if LOCALTEST\nb();\n#endif\n")
        self.assertEqual([b.removal_candidate() for b in p.blocks],
                         ["both branches hold the same code", "the addition holds no code", None])


class CompareTests(unittest.TestCase):
    def test_tolerated_differences(self):
        self.assertEqual(vs.tolerated_difference(b"a\n", b"a\n"), "")
        self.assertEqual(vs.tolerated_difference(b"a\r\nb\r\n", b"a\nb\n"), "line endings")
        self.assertEqual(vs.tolerated_difference(b"\xef\xbb\xbfa\n", b"a\n"), "BOM")
        self.assertIsNone(vs.tolerated_difference(b"a\n\n", b"a\n"))
        self.assertIsNone(vs.tolerated_difference(b"a \n", b"a\n"))

    def test_first_difference_maps_production_lines(self):
        p = vs.parse(PROGRAM.replace(b"app.Run();", b"app.RunAsync();"))
        ours, theirs, a, b = vs.first_difference(p.production, UPSTREAM_PROGRAM, p.production_lines)
        self.assertEqual((ours, theirs, a, b), (11, 4, "app.RunAsync();", "app.Run();"))

    def test_conflict_marker(self):
        self.assertEqual(vs.conflict_marker_line(b"a\n<<<<<<< localtest\nb\n=======\nc\n>>>>>>> new\n"), 2)
        self.assertEqual(vs.conflict_marker_line(b"a\n=== heading\n"), 0)


# ---------------------------------------------------------------------------------------------------------------------
# The metadata

DOC = """# Vendored copy of Test/up

```json vendor-sync
{"repository": "Test/up", "branch": "main", "commit": "%s", "date": "2026-01-01", "paths": ["src", "Dockerfile"]}
```

<!-- vendor-sync:begin pin -->
<!-- vendor-sync:end pin -->
- Copied paths: `src/`.

## Directive sites

<!-- vendor-sync:begin sites -->
<!-- vendor-sync:end sites -->

Why the sites exist.
"""


class MetadataTests(unittest.TestCase):
    def test_read_format_round_trip(self):
        meta = vs.read_meta(DOC % ("a" * 40))
        self.assertEqual(meta["paths"], ["src", "Dockerfile"])
        text = vs.write_meta(DOC % ("a" * 40), dict(meta, mirrors=[{"upstream": "Dockerfile", "ours": "Dockerfile"}]))
        again = vs.read_meta(text)
        self.assertEqual(again["mirrors"], [{"upstream": "Dockerfile", "ours": "Dockerfile"}])
        self.assertIn('"mirrors": [{ "upstream": "Dockerfile", "ours": "Dockerfile" }]\n}', text)
        # Laid out as Prettier does at print width 80: what does not fit goes one item per line.
        long = dict(meta, paths=["src/apps/Altinn.Something", "src/libs/Altinn.Other", "Directory.Build.props"],
                    follow=[{"package": "OpenTelemetry.Exporter.OpenTelemetryProtocol", "follows": "OpenTelemetry"}])
        self.assertIn('  "paths": [\n    "src/apps/Altinn.Something",\n    "src/libs/Altinn.Other",\n'
                      '    "Directory.Build.props"\n  ],\n', vs.format_meta(long))
        self.assertIn('  "follow": [\n    {\n      "package": "OpenTelemetry.Exporter.OpenTelemetryProtocol",\n'
                      '      "follows": "OpenTelemetry"\n    }\n  ]\n}', vs.format_meta(long))
        self.assertTrue(text.endswith("Why the sites exist.\n"))

    def test_validation(self):
        bad = {"repository": "no-slash", "branch": "main", "commit": "abc", "date": "x", "paths": [], "colour": 1,
               "local": "patches"}
        with self.assertRaises(vs.ToolError) as ctx:
            vs.validate_meta(bad, "X")
        for text in ("OWNER/NAME", "full commit id", "non-empty list", "unknown key `colour`", "needs `patches`"):
            self.assertIn(text, str(ctx.exception))
        with self.assertRaises(vs.ToolError):
            vs.read_meta("# no block\n")

    def test_generated_sections(self):
        text = vs.replace_generated(DOC % ("a" * 40), "pin", "- one\n", "X")
        self.assertIn("<!-- vendor-sync:begin pin -->\n\n- one\n\n<!-- vendor-sync:end pin -->", text)
        self.assertEqual(vs.replace_generated(text, "pin", "- one\n", "X"), text)
        with self.assertRaises(vs.ToolError):
            vs.replace_generated("no markers", "pin", "x", "X")

    def test_site_table(self):
        table = vs.render_sites({"src/Program.cs": vs.parse(PROGRAM), "a|b.cs": vs.parse(b"#if LOCALTEST\n#endif\n")})
        self.assertIn("| `src/Program.cs` | 6-10  | replacement | `services.AddOtlp();`                |", table)
        self.assertIn("| `a\\|b.cs`        | 1-2   | addition    | (empty)                              |", table)
        self.assertIn("| ---------------- | ----- | ----------- | ------------------------------------ |", table)
        self.assertEqual(vs.render_sites({}), "No directive sites.\n")


# ---------------------------------------------------------------------------------------------------------------------
# check, render and sync on throwaway repositories


def run(*args, cwd):
    return subprocess.run(["git", *args], cwd=cwd, check=True, capture_output=True).stdout.decode()


def commit_all(repo, message):
    run("add", "-A", ".", cwd=repo)
    run("commit", "-q", "-m", message, cwd=repo)
    return run("rev-parse", "HEAD", cwd=repo).strip()


def write(path: Path, data: bytes, mode=0o644):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    path.chmod(mode)


def export(repo, commit, paths, dest: Path):
    """Write the files under paths at commit into dest, blob for blob, with their modes and symlinks."""
    out = subprocess.run(["git", "ls-tree", "-r", "-z", commit, "--", *paths], cwd=repo, check=True,
                         capture_output=True).stdout
    for entry in filter(None, out.split(b"\0")):
        info, path = entry.split(b"\t", 1)
        mode, _, sha = info.decode().split()
        data = subprocess.run(["git", "cat-file", "blob", sha], cwd=repo, check=True, capture_output=True).stdout
        target = dest / path.decode()
        target.parent.mkdir(parents=True, exist_ok=True)
        if mode == "120000":
            os.symlink(data.decode(), target)
        else:
            write(target, data, 0o755 if mode == "100755" else 0o644)


class RepoTests(unittest.TestCase):
    """Upstream Test/up at commit A (the pin) and B; ours vendors src/ and Dockerfile of A as component `demo`."""

    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(subprocess.run, ["rm", "-rf", str(self.tmp)])
        self.up = self.tmp / "upstream"
        self.up.mkdir()
        run("init", "-q", "-b", "main", cwd=self.up)
        run("config", "uploadpack.allowFilter", "true", cwd=self.up)
        run("config", "uploadpack.allowAnySHA1InWant", "true", cwd=self.up)
        write(self.up / "src/Program.cs", UPSTREAM_PROGRAM)
        write(self.up / "src/With space .cs", b"class A {}\n")
        write(self.up / "src/run.sh", b"#!/bin/sh\necho hi\n", 0o755)
        write(self.up / "src/Old.cs", b"old\n")
        write(self.up / "src/App.sln", b"line1\nline2\n")
        os.symlink("Program.cs", self.up / "src/link.cs")
        write(self.up / "Dockerfile", b"FROM base:1\nRUN build\n")
        write(self.up / "README.md", b"not copied\n")
        self.a = commit_all(self.up, "A")
        write(self.up / "src/Program.cs", UPSTREAM_PROGRAM.replace(b"app.Run();", b"app.Map();\napp.Run();"))
        (self.up / "src/Old.cs").unlink()
        write(self.up / "src/New.cs", b"new\n")
        write(self.up / "Dockerfile", b"FROM base:2\nRUN build\n")
        self.b = commit_all(self.up, "B: map")

        self.clones = self.tmp / "clones"
        (self.clones / "Test").mkdir(parents=True)
        run("clone", "-q", "--filter=blob:none", "--no-checkout", f"file://{self.up}", str(self.clones / "Test/up"),
            cwd=self.tmp)

        self.ours = self.tmp / "ours"
        self.comp = self.ours / "localtest/components/demo"
        self.vendor = self.comp / "vendor"
        self.ours.mkdir()
        run("init", "-q", "-b", "main", cwd=self.ours)
        export(self.up, self.a, ["src", "Dockerfile"], self.vendor)
        write(self.vendor / "src/Program.cs", PROGRAM)
        write(self.vendor / "src/App.sln", b"line1\r\nline2\r\n")
        write(self.comp / "Dockerfile", b"FROM base:1\nRUN build\n# localtest: ours\n")
        write(self.vendor / "UPSTREAM.md", (DOC % self.a).replace(
            '"paths": ["src", "Dockerfile"]}',
            '"paths": ["src", "Dockerfile"], "mirrors": [{"upstream": "Dockerfile", "ours": "Dockerfile", '
            '"merge": true}], "reminders": ["check upstream PR 1"]}').encode())
        self.assertEqual(self.tool("render", "demo")[0], 0)
        commit_all(self.ours, "vendor demo")

    def tool(self, *args):
        out = io.StringIO()
        err = io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = vs.main(["--root", str(self.ours), "--clones", str(self.clones), *args])
        return code, out.getvalue() + err.getvalue()

    # check

    def test_check_passes_with_sites_symlink_space_and_crlf(self):
        code, out = self.tool("check", "demo")
        self.assertEqual(code, 0, out)
        self.assertIn("ok demo: Test/up@" + self.a[:7] + ", 1 site files, 2 blocks", out)
        self.assertIn("tolerated: src/App.sln (line endings)", out)
        code, out = self.tool("check", "demo", "--index")
        self.assertEqual(code, 0, out)

    def test_bom_is_tolerated(self):
        write(self.vendor / "src/Old.cs", b"\xef\xbb\xbfold\n")
        code, out = self.tool("check", "demo")
        self.assertEqual(code, 0, out)
        self.assertIn("src/Old.cs (BOM)", out)

    def test_one_line_edit_fails_with_file_and_line(self):
        write(self.vendor / "src/With space .cs", b"class B {}\n")
        code, out = self.tool("check", "demo")
        self.assertEqual(code, 1)
        self.assertIn("localtest/components/demo/vendor/src/With space .cs:1: differs from upstream", out)
        self.assertIn("ours:     class B {}", out)

    def test_edit_outside_a_block_in_a_site_file_fails(self):
        write(self.vendor / "src/Program.cs", PROGRAM.replace(b"app.Run();", b"app.Run(); // x"))
        code, out = self.tool("check", "demo")
        self.assertEqual(code, 1)
        self.assertIn("vendor/src/Program.cs:11: production view differs from upstream (upstream line 4)", out)

    def test_added_well_formed_block_passes(self):
        write(self.vendor / "src/Old.cs", b"#if LOCALTEST\nlocal();\n#endif\nold\n")
        code, out = self.tool("check", "demo")
        self.assertEqual(code, 0, out)  # the site table in UPSTREAM.md is now stale: a warning
        self.assertIn("warning: localtest/components/demo/vendor/UPSTREAM.md: the site table is stale", out)
        self.assertEqual(self.tool("render", "demo")[0], 0)
        code, out = self.tool("check", "demo")
        self.assertEqual(code, 0, out)
        self.assertIn("2 site files, 3 blocks", out)

    def test_malformed_block_fails_with_line(self):
        write(self.vendor / "src/Old.cs", b"old\n#if !LOCALTEST\nx\n#endif\n")
        code, out = self.tool("check", "demo")
        self.assertEqual(code, 1)
        self.assertIn("vendor/src/Old.cs:2: #if !LOCALTEST without #else", out)

    def test_mode_extra_and_missing_files(self):
        (self.vendor / "src/run.sh").chmod(0o644)
        (self.vendor / "src/Old.cs").unlink()
        write(self.vendor / "src/Mine.cs", b"mine\n")
        (self.vendor / "src/link.cs").unlink()
        write(self.vendor / "src/link.cs", b"Program.cs")
        code, out = self.tool("check", "demo")
        self.assertEqual(code, 1)
        self.assertIn("src/run.sh: mode 100644, upstream 100755", out)
        self.assertIn("src/Old.cs: missing", out)
        self.assertIn("src/Mine.cs: not upstream at the pin", out)
        self.assertIn("src/link.cs: mode 100644, upstream 120000", out)

    def test_conflict_markers_inside_a_block_fail(self):
        write(self.vendor / "src/Program.cs", PROGRAM.replace(b"Localtest.Add(builder);",
                                                              b"<<<<<<< localtest\nLocaltest.Add(builder);\n=======\n"
                                                              b">>>>>>> new"))
        code, out = self.tool("check", "demo")
        self.assertEqual(code, 1)
        self.assertIn("src/Program.cs:3: conflict marker", out)

    def test_stale_pin_bullets_fail(self):
        md = self.vendor / "UPSTREAM.md"
        md.write_text(md.read_text().replace("(main, ", "(trunk, "))
        code, out = self.tool("check", "demo")
        self.assertEqual(code, 0)
        self.assertIn("warning: localtest/components/demo/vendor/UPSTREAM.md: the pin bullets are stale", out)

    # status

    def test_status(self):
        code, out = self.tool("status", "--all", "--json")
        self.assertEqual(code, 0, out)
        s = json.loads(out)[0]
        self.assertEqual((s["behind"], s["behind_touching_paths"]), (1, 1))
        self.assertEqual(s["files_changed"], {"M": 2, "A": 1, "D": 1})
        self.assertEqual(s["blocks"], {"addition": 1, "replacement": 1})
        self.assertEqual(s["site_files_changed_upstream"], ["`src/Program.cs`: merges clean"])
        self.assertEqual(s["mirrors_changed_upstream"], ["`Dockerfile`: merges clean into ours"])
        code, out = self.tool("status", "demo", "--no-fetch")
        self.assertIn("| demo | Test/up |", out)
        self.assertIn("- demo: reminder: check upstream PR 1", out)

    # sync

    def staged(self, path):
        return run("ls-files", "-s", "--", f"localtest/components/demo/vendor/{path}", cwd=self.ours).split()

    def test_sync_stages_upstream_and_merges_sites(self):
        summary = self.tmp / "summary.txt"
        code, out = self.tool("sync", "demo", "--summary", str(summary))
        self.assertEqual(code, 0, out)
        self.assertIn(f"demo: sync vendor to {self.b[:7]}", out)
        self.assertIn("merged: src/Program.cs", out)
        self.assertIn("added: src/New.cs", out)
        self.assertIn("removed: src/Old.cs", out)
        self.assertIn("reminder: check upstream PR 1", out)
        self.assertIn("Next steps", out)
        self.assertTrue(summary.read_text().startswith(f"demo: sync vendor to {self.b[:7]}"))
        # Upstream's blobs and modes in the index, the merged site file in the working tree and the index.
        up_new = run("ls-tree", "-r", self.b, "--", "src/New.cs", cwd=self.up).split()
        self.assertEqual(self.staged("src/New.cs")[:2], up_new[:1] + up_new[2:3])
        self.assertEqual(self.staged("src/Old.cs"), [])
        self.assertFalse((self.vendor / "src/Old.cs").exists())
        merged = (self.vendor / "src/Program.cs").read_bytes()
        self.assertEqual(vs.parse(merged).production,
                         UPSTREAM_PROGRAM.replace(b"app.Run();", b"app.Map();\napp.Run();"))
        self.assertEqual(self.staged("src/Program.cs")[1], vs.blob_sha(merged))
        # Our CRLF file upstream did not touch stays; the mirror is merged but not staged.
        self.assertEqual((self.vendor / "src/App.sln").read_bytes(), b"line1\r\nline2\r\n")
        self.assertEqual((self.comp / "Dockerfile").read_bytes(), b"FROM base:2\nRUN build\n# localtest: ours\n")
        self.assertIn(" M localtest/components/demo/Dockerfile", run("status", "--porcelain", cwd=self.ours))
        # The metadata moved, and the result passes check (index and working tree).
        self.assertEqual(vs.read_meta((self.vendor / "UPSTREAM.md").read_text())["commit"], self.b)
        self.assertEqual(self.tool("check", "demo", "--index")[0], 0)
        self.assertEqual(self.tool("check", "demo")[0], 0)

    def test_sync_conflict_is_left_unmerged(self):
        write(self.vendor / "src/Program.cs", PROGRAM.replace(b"app.Run();", b"#if LOCALTEST\nmine();\n#endif\napp.Run();"))
        self.tool("render", "demo")
        commit_all(self.ours, "another block next to the change")
        code, out = self.tool("sync", "demo", "--to", self.b)
        self.assertEqual(code, 2, out)
        self.assertIn("CONFLICT: src/Program.cs", out)
        stages = run("ls-files", "-u", "--", "localtest/components/demo/vendor/src/Program.cs", cwd=self.ours)
        self.assertEqual([line.split()[2] for line in stages.splitlines()], ["1", "2", "3"])
        self.assertIn(b"<<<<<<< localtest", (self.vendor / "src/Program.cs").read_bytes())
        code, out = self.tool("check", "demo")
        self.assertEqual(code, 1)
        self.assertIn("unmerged", out)

    def test_sync_refuses_a_dirty_or_invalid_vendor(self):
        write(self.vendor / "src/Old.cs", b"edited\n")
        code, out = self.tool("sync", "demo")
        self.assertEqual(code, 3)
        self.assertIn("uncommitted changes", out)
        commit_all(self.ours, "edit")
        code, out = self.tool("sync", "demo")
        self.assertEqual(code, 3)
        self.assertIn("`check` fails before the sync", out)

    def test_sync_moves_to_another_repository(self):
        other = self.tmp / "other"
        run("clone", "-q", str(self.up), str(other), cwd=self.tmp)
        run("checkout", "-q", self.a, cwd=other)
        run("checkout", "-q", "-b", "main2", cwd=other)
        (other / "lib").mkdir()
        run("mv", "src", "lib/src", cwd=other)
        run("mv", "Dockerfile", "lib/Dockerfile", cwd=other)
        write(other / "lib/src/Program.cs", UPSTREAM_PROGRAM.replace(b"app.Run();", b"app.Moved();\napp.Run();"))
        moved = commit_all(other, "moved")
        run("config", "uploadpack.allowFilter", "true", cwd=other)
        run("clone", "-q", "--filter=blob:none", "--no-checkout", "-b", "main2", f"file://{other}",
            str(self.clones / "Test/other"), cwd=self.tmp)
        code, out = self.tool("sync", "demo", "--repository", "Test/other", "--branch", "main2", "--paths", "lib")
        # The layout changed (src/ -> lib/src/): the site file is gone from the new tree, so it is a conflict.
        self.assertEqual(code, 2, out)
        self.assertIn("Upstream: Test/other main2 (moved from Test/up)", out)
        self.assertIn("CONFLICT: src/Program.cs: deleted upstream", out)
        self.assertTrue((self.vendor / "lib/src/Program.cs").exists())
        meta = vs.read_meta((self.vendor / "UPSTREAM.md").read_text())
        self.assertEqual((meta["repository"], meta["commit"], meta["paths"]), ("Test/other", moved, ["lib"]))

    def test_sync_to_another_repository_with_the_same_layout_merges(self):
        other = self.tmp / "other"
        run("clone", "-q", str(self.up), str(other), cwd=self.tmp)
        write(other / "src/Program.cs", UPSTREAM_PROGRAM.replace(b"app.Run();", b"app.Other();\napp.Run();"))
        new = commit_all(other, "other")
        run("config", "uploadpack.allowFilter", "true", cwd=other)
        run("clone", "-q", "--filter=blob:none", "--no-checkout", f"file://{other}", str(self.clones / "Test/other"),
            cwd=self.tmp)
        code, out = self.tool("sync", "demo", "--repository", "Test/other", "--paths", "src", "Dockerfile")
        self.assertEqual(code, 0, out)
        self.assertIn("merged: src/Program.cs", out)
        self.assertEqual(vs.read_meta((self.vendor / "UPSTREAM.md").read_text())["commit"], new)
        self.assertEqual(self.tool("check", "demo", "--index")[0], 0)

    def test_sync_reports_a_site_upstream_made_obsolete(self):
        # Upstream adopts our replacement: the replaced line changes, so the sync stops there, and once the agent takes
        # upstream's line into the #if !LOCALTEST branch, check names the block as a candidate for removal.
        write(self.up / "src/Program.cs", UPSTREAM_PROGRAM.replace(b"services.AddAzureMonitor();",
                                                                   b"services.AddOtlp();"))
        commit_all(self.up, "C: upstream switches to OTLP")
        code, out = self.tool("sync", "demo")
        self.assertEqual(code, 2, out)
        self.assertIn("CONFLICT: src/Program.cs: textual conflict; replacement at 6-10: upstream changed the code it "
                      "replaces", out)
        write(self.vendor / "src/Program.cs", PROGRAM.replace(b"services.AddAzureMonitor();", b"services.AddOtlp();"))
        run("add", "-A", ".", cwd=self.ours)
        self.tool("render", "demo")
        code, out = self.tool("check", "demo")
        self.assertEqual(code, 0, out)
        self.assertIn("candidate for removal: src/Program.cs:6-10 (replacement): both branches hold the same code", out)

    def test_reanchor_places_blocks_next_to_upstream_insertions(self):
        before = vs.parse(PROGRAM)
        theirs = b"using X;\n" + UPSTREAM_PROGRAM.replace(b"app.Run();", b"app.Map();\napp.Run();")
        merged, problems = vs.reanchor(before, UPSTREAM_PROGRAM, theirs)
        self.assertEqual(problems, [])
        self.assertEqual(merged, b"using X;\n" + PROGRAM.replace(b"app.Run();", b"app.Map();\napp.Run();"))
        merged, problems = vs.reanchor(before, UPSTREAM_PROGRAM, UPSTREAM_PROGRAM.replace(b"\n\n", b"\nx();\n"))
        self.assertIsNone(merged)
        self.assertEqual(problems, ["addition at 2-4: upstream changed or inserted lines right next to it"])


class GroupAndPatchTests(unittest.TestCase):
    """A group of two components on one upstream, and a `patches` component."""

    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(subprocess.run, ["rm", "-rf", str(self.tmp)])
        self.up = self.tmp / "upstream"
        self.up.mkdir()
        run("init", "-q", "-b", "main", cwd=self.up)
        run("config", "uploadpack.allowFilter", "true", cwd=self.up)
        write(self.up / "a/x.ts", b"export const a = 1;\nexport const host = 'https://' + h;\nexport const c = 3;\n")
        write(self.up / "b/y.cs", b"y\n")
        self.a = commit_all(self.up, "A")
        write(self.up / "a/x.ts", b"// header\nexport const a = 1;\nexport const host = 'https://' + h;\n"
                                  b"export const c = 3;\n")
        write(self.up / "b/y.cs", b"y2\n")
        self.b = commit_all(self.up, "B")
        self.clones = self.tmp / "clones"
        run("clone", "-q", "--filter=blob:none", "--no-checkout", f"file://{self.up}", str(self.clones / "Test/up"),
            cwd=self.tmp)
        self.ours = self.tmp / "ours"
        self.ours.mkdir()
        run("init", "-q", "-b", "main", cwd=self.ours)
        base = self.ours / "localtest/components"
        for name, paths, extra in (("one", ["a"], '"group": "g"'), ("two", ["b"], '"group": "g"'),
                                   ("web", ["a"], '"local": "patches", "patches": "patches"')):
            vendor = base / name / "vendor"
            export(self.up, self.a, paths, vendor)
            (vendor / "UPSTREAM.md").write_text(
                f'# {name}\n\n```json vendor-sync\n{{"repository": "Test/up", "branch": "main", "commit": "{self.a}", '
                f'"date": "2026-01-01", "paths": {json.dumps(paths)}, {extra}}}\n```\n\n'
                "<!-- vendor-sync:begin pin -->\n<!-- vendor-sync:end pin -->\n\n"
                "<!-- vendor-sync:begin sites -->\n<!-- vendor-sync:end sites -->\n")
        write(base / "web/patches/0001-scheme.patch",
              f"localtest: keep the scheme.\nAgainst Test/up {self.a}.\n\n".encode() +
              b"--- a/a/x.ts\n+++ b/a/x.ts\n@@ -1,3 +1,3 @@\n export const a = 1;\n"
              b"-export const host = 'https://' + h;\n+export const host = h; // localtest: keep the scheme\n"
              b" export const c = 3;\n")
        self.patch = base / "web/patches/0001-scheme.patch"
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            self.assertEqual(vs.main(["--root", str(self.ours), "--clones", str(self.clones), "render", "--all"]), 0)
        commit_all(self.ours, "vendor")

    def tool(self, *args):
        out = io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(out):
            code = vs.main(["--root", str(self.ours), "--clones", str(self.clones), *args])
        return code, out.getvalue()

    def test_check_all_and_patches(self):
        code, out = self.tool("check", "--all")
        self.assertEqual(code, 0, out)
        self.assertIn("ok web: Test/up@" + self.a[:7] + ", patches (1 apply)", out)

    def test_directive_in_a_patches_component_fails(self):
        write(self.ours / "localtest/components/web/vendor/a/x.ts", b"#if LOCALTEST\n#endif\n" +
              (self.ours / "localtest/components/web/vendor/a/x.ts").read_bytes())
        code, out = self.tool("check", "web")
        self.assertEqual(code, 1)
        self.assertIn("LOCALTEST directives in a `patches` component", out)

    def test_group_moves_together(self):
        code, out = self.tool("sync", "one")
        self.assertEqual(code, 0, out)
        self.assertIn(f"g: sync vendor to {self.b[:7]}", out)
        self.assertIn("Components: one, two.", out)
        for name in ("one", "two"):
            md = self.ours / f"localtest/components/{name}/vendor/UPSTREAM.md"
            self.assertEqual(vs.read_meta(md.read_text())["commit"], self.b)
        self.assertEqual(self.tool("check", "g", "--index")[0], 0)

    def test_group_out_of_step_fails_check(self):
        md = self.ours / "localtest/components/two/vendor/UPSTREAM.md"
        md.write_text(md.read_text().replace(self.a, self.b))
        code, out = self.tool("check", "one")
        self.assertEqual(code, 1)
        self.assertIn("group g: its components are not at one repository and commit", out)

    def test_patches_are_refreshed(self):
        code, out = self.tool("sync", "web")
        self.assertEqual(code, 0, out)
        self.assertIn("patch 0001-scheme.patch: refreshed", out)
        text = self.patch.read_text()
        self.assertIn(f"Against Test/up {self.b}.", text)
        self.assertIn("@@ -1,4 +1,4 @@\n // header\n", text)
        self.assertNotIn("index ", text)
        self.assertEqual(self.tool("check", "web", "--index")[0], 0)

    def test_a_patch_that_conflicts_is_reported_and_kept(self):
        write(self.up / "a/x.ts", b"export const a = 1;\nexport const host = `https://${h}`;\nexport const c = 3;\n")
        commit_all(self.up, "C")
        before = self.patch.read_text()
        code, out = self.tool("sync", "web")
        self.assertIn("patch 0001-scheme.patch: conflicts in a/x.ts; the patch file is unchanged", out)
        self.assertEqual(self.patch.read_text(), before)
        self.assertEqual(code, 2)
        code, out = self.tool("check", "web", "--index")
        self.assertEqual(code, 1)
        self.assertIn("does not apply to vendor/", out)


if __name__ == "__main__":
    unittest.main()
