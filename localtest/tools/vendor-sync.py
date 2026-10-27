#!/usr/bin/env python3
"""Sync and check the vendored upstream copies under localtest/components/<name>/vendor/.

Every component's vendor/UPSTREAM.md holds a fenced JSON block with the info string `json vendor-sync` (the upstream
repository, the pinned commit, the copied paths and the rest, see localtest/docs/vendor-sync.md). The invariant this
tool proves: vendor/ is upstream at the pinned commit, file for file and mode for mode, except files with LOCALTEST
directive blocks, whose production view (the file without its `#if LOCALTEST` additions and with only the first branch
of each `#if !LOCALTEST ... #else ... #endif`) equals upstream's file. A difference of line endings (CRLF against LF)
or of a leading UTF-8 byte order mark is tolerated and reported.

Commands:
  status [NAME...|--all] [--json] [--no-fetch]   how far each pin is behind upstream; read only
  check  [NAME...|--all] [--index]                the invariant; exit 1 with file and line on a violation
  render [NAME...|--all]                          regenerate the pin bullets and site table in UPSTREAM.md
  sync   NAME [--to COMMIT] [--repository OWNER/REPO --paths P...] [--branch B] [--group G] [--summary FILE]
         moves the pin: writes upstream's new files into the working tree and the index, merges the site files,
         leaves conflicts unmerged in the index, prints the summary and the next steps. Never commits.

NAME is a component (the folder name) or a group (`altinn-auth`). Upstream repositories are read from blobless clones
in --clones DIR (or $VENDOR_SYNC_CLONES, default $XDG_CACHE_HOME/localtest-vendor), created when missing.
Standard library only.
"""
from __future__ import annotations

import argparse
import datetime as dt
import difflib
import fnmatch
import hashlib
import json
import os
import re
import shutil
import stat
import subprocess
import sys
import tempfile
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
COMPONENTS_REL = "localtest/components"
ZERO = "0" * 40
META_KEYS = ("repository", "branch", "commit", "date", "paths", "group", "local", "patches", "mirrors", "watch",
             "projects", "follow", "reminders")
GITHUB = "https://github.com/"


class ToolError(Exception):
    pass


def git(*args, cwd=None, input=None, check=True, env=None) -> bytes:
    p = subprocess.run(["git", *args], cwd=cwd, input=input, capture_output=True, env=env)
    if check and p.returncode != 0:
        raise ToolError(f"git {' '.join(map(str, args))} failed in {cwd}: {p.stderr.decode(errors='replace').strip()}")
    return p.stdout


def blob_sha(data: bytes) -> str:
    return hashlib.sha1(b"blob %d\0" % len(data) + data).hexdigest()


# --------------------------------------------------------------------------------------------------------------------
# Directive blocks


class DirectiveError(Exception):
    def __init__(self, line: int, message: str):
        super().__init__(f"line {line}: {message}")
        self.line = line
        self.message = message


@dataclass
class Block:
    kind: str  # "addition" (#if LOCALTEST) or "replacement" (#if !LOCALTEST ... #else)
    start: int  # line of the #if, 1-based
    end: int = 0  # line of the #endif
    else_line: int = 0
    production: list = field(default_factory=list)  # lines of the !LOCALTEST branch (replacement only)
    localtest: list = field(default_factory=list)  # lines of the LOCALTEST branch (addition body or #else branch)
    at: int = 0  # index in the production view of the first line the block replaces, or the line it precedes
    raw: list = field(default_factory=list)  # every line from the #if to the #endif

    def local_text(self) -> bytes:
        return b"".join(self.localtest)

    def production_text(self) -> bytes:
        return b"".join(self.production)

    def first_local_line(self) -> str:
        """The first line of code of the LOCALTEST branch (a comment when it holds nothing else)."""
        lines = [line.decode("utf-8", "replace").strip() for line in self.localtest]
        lines = [s for s in lines if s]
        return next((s for s in lines if not s.startswith("//")), lines[0] if lines else "")

    def removal_candidate(self) -> str | None:
        """Why this block may be unnecessary, when that is visible in the file alone."""
        local = code_lines(self.localtest)
        if self.kind == "addition" and not local:
            return "the addition holds no code"
        if self.kind == "replacement" and local == code_lines(self.production):
            return "both branches hold the same code"
        return None


def code_lines(lines) -> list:
    out = []
    for line in lines:
        s = line.decode("utf-8", "replace").strip()
        if s and not s.startswith("//"):
            out.append(" ".join(s.split()))
    return out


@dataclass
class Parsed:
    lines: list
    blocks: list
    production: bytes
    production_lines: list  # original 1-based line number of each production line
    localtest: bytes


_DIRECTIVE = re.compile(rb"^[ \t]*#[ \t]*(if|elif|else|endif)\b([^\r\n]*)")
_CONFLICT = re.compile(rb"^(<<<<<<<|>>>>>>>|\|\|\|\|\|\|\|)( |\r?\n|$)|^=======\r?\n?$")


def split_lines(data: bytes) -> list:
    return re.findall(rb"[^\n]*\n|[^\n]+$", data)


def _condition(rest: bytes) -> str:
    text = rest.decode("utf-8", "replace")
    text = text.split("//", 1)[0]
    return "".join(text.split())


def has_directives(data: bytes) -> bool:
    return b"LOCALTEST" in data and any(
        (m := _DIRECTIVE.match(line)) and "LOCALTEST" in _condition(m.group(2)) for line in split_lines(data))


def parse(data: bytes) -> Parsed:
    """Split a file into its LOCALTEST blocks, its production view and its localtest view.

    Accepted: `#if LOCALTEST ... #endif` (an addition) and `#if !LOCALTEST ... #else ... #endif` (a replacement),
    with any other preprocessor nesting inside a branch. Refused: #elif in a block, #else in an addition, a second
    #else, #if !LOCALTEST without #else, a LOCALTEST block inside another, any other condition naming LOCALTEST, an
    unterminated block.
    """
    lines = split_lines(data)
    blocks, production, production_lines, localtest = [], [], [], []
    cur: Block | None = None
    depth = 0
    for number, line in enumerate(lines, 1):
        m = _DIRECTIVE.match(line)
        keyword = m.group(1).decode() if m else None
        cond = _condition(m.group(2)) if m else ""
        names_localtest = "LOCALTEST" in cond
        if cur is None:
            if keyword == "if" and cond in ("LOCALTEST", "!LOCALTEST"):
                cur = Block("addition" if cond == "LOCALTEST" else "replacement", number, at=len(production),
                            raw=[line])
                depth = 0
                continue
            if names_localtest:
                raise DirectiveError(number, f"unsupported LOCALTEST directive `{line.decode(errors='replace').strip()}`"
                                     " (only `#if LOCALTEST` and `#if !LOCALTEST` ... `#else` are allowed)")
            production.append(line)
            production_lines.append(number)
            localtest.append(line)
            continue
        if keyword is not None and names_localtest and not (keyword == "endif" and depth == 0):
            raise DirectiveError(number, f"LOCALTEST directive inside the block opened at line {cur.start}")
        in_production = cur.kind == "replacement" and not cur.else_line
        cur.raw.append(line)
        if keyword == "if":
            depth += 1
        elif keyword in ("elif", "else") and depth == 0:
            if keyword == "elif":
                raise DirectiveError(number, f"#elif in the LOCALTEST block opened at line {cur.start}")
            if cur.kind == "addition":
                raise DirectiveError(number, f"#else in the `#if LOCALTEST` block opened at line {cur.start}"
                                     " (a replacement starts with `#if !LOCALTEST`)")
            if cur.else_line:
                raise DirectiveError(number, f"second #else in the block opened at line {cur.start}")
            cur.else_line = number
            continue
        elif keyword == "endif":
            if depth == 0:
                if cur.kind == "replacement" and not cur.else_line:
                    raise DirectiveError(cur.start, "#if !LOCALTEST without #else")
                cur.end = number
                blocks.append(cur)
                cur = None
                continue
            depth -= 1
        if in_production:
            cur.production.append(line)
            production.append(line)
            production_lines.append(number)
        else:
            cur.localtest.append(line)
            localtest.append(line)
    if cur is not None:
        raise DirectiveError(cur.start, "LOCALTEST block without #endif")
    return Parsed(lines, blocks, b"".join(production), production_lines, b"".join(localtest))


def normalise(data: bytes) -> bytes:
    if data.startswith(b"\xef\xbb\xbf"):
        data = data[3:]
    return data.replace(b"\r\n", b"\n")


def tolerated_difference(a: bytes, b: bytes) -> str | None:
    """None when a and b differ beyond line endings and a leading BOM, else what differs ('' when identical)."""
    if a == b:
        return ""
    if normalise(a) != normalise(b):
        return None
    what = []
    if a.startswith(b"\xef\xbb\xbf") != b.startswith(b"\xef\xbb\xbf"):
        what.append("BOM")
    if a.replace(b"\r\n", b"\n") != a or b.replace(b"\r\n", b"\n") != b:
        what.append("line endings")
    return " and ".join(what) or "line endings"


def first_difference(ours: bytes, theirs: bytes, ours_lines=None):
    """(our line, their line, our text, their text) of the first differing line, compared without CR and BOM."""
    a = normalise(ours).split(b"\n")
    b = normalise(theirs).split(b"\n")
    i = 0
    while i < len(a) and i < len(b) and a[i] == b[i]:
        i += 1
    if ours_lines is not None:
        our_line = ours_lines[i] if i < len(ours_lines) else (ours_lines[-1] + 1 if ours_lines else 1)
    else:
        our_line = i + 1
    text = lambda lines: lines[i].decode("utf-8", "replace").strip() if i < len(lines) else "<end of file>"
    return our_line, i + 1, text(a), text(b)


def conflict_marker_line(data: bytes) -> int:
    for number, line in enumerate(split_lines(data), 1):
        if _CONFLICT.match(line):
            return number
    return 0


# --------------------------------------------------------------------------------------------------------------------
# Metadata in UPSTREAM.md

_META_RE = re.compile(r"^```json vendor-sync\n(.*?)^```[ \t]*$", re.S | re.M)


def read_meta(text: str, where: str = "UPSTREAM.md") -> dict:
    m = _META_RE.search(text)
    if not m:
        raise ToolError(f"{where}: no ```json vendor-sync block")
    try:
        meta = json.loads(m.group(1))
    except json.JSONDecodeError as e:
        raise ToolError(f"{where}: the vendor-sync block is not JSON: {e}") from e
    validate_meta(meta, where)
    return meta


def validate_meta(meta: dict, where: str):
    problems = []
    for key in ("repository", "branch", "commit", "date", "paths"):
        if key not in meta:
            problems.append(f"missing `{key}`")
    for key in meta:
        if key not in META_KEYS:
            problems.append(f"unknown key `{key}`")
    if "repository" in meta and not re.fullmatch(r"[\w.-]+/[\w.-]+", str(meta["repository"])):
        problems.append("`repository` is not OWNER/NAME")
    if "commit" in meta and not re.fullmatch(r"[0-9a-f]{40}", str(meta["commit"])):
        problems.append("`commit` is not a full commit id")
    if not isinstance(meta.get("paths", []), list) or not meta.get("paths"):
        problems.append("`paths` must be a non-empty list")
    if meta.get("local", "directives") not in ("directives", "patches"):
        problems.append("`local` must be `directives` or `patches`")
    if meta.get("local") == "patches" and not meta.get("patches"):
        problems.append("`local: patches` needs `patches`, the folder of the patch files")
    for mirror in meta.get("mirrors", []):
        if not isinstance(mirror, dict) or not {"upstream", "ours"} <= set(mirror):
            problems.append("each mirror needs `upstream` and `ours`")
    for follow in meta.get("follow", []):
        if not isinstance(follow, dict) or "package" not in follow:
            problems.append("each `follow` entry needs `package`")
    for project in meta.get("projects", []):
        if not isinstance(project, (str, dict)) or (isinstance(project, dict) and "path" not in project):
            problems.append("each project is a path or {path, configuration}")
    if problems:
        raise ToolError(f"{where}: " + "; ".join(problems))


def format_meta(meta: dict) -> str:
    ordered = {k: meta[k] for k in META_KEYS if k in meta}
    lines = []
    for i, (key, value) in enumerate(ordered.items()):
        comma = "," if i < len(ordered) - 1 else ""
        compact = json.dumps(value, ensure_ascii=False)
        if isinstance(value, list) and value and (len(compact) > 100 or any(isinstance(v, dict) for v in value)):
            items = [json.dumps(v, ensure_ascii=False, separators=(", ", ": ")) for v in value]
            body = ",\n".join(f"    {item}" for item in items)
            lines.append(f'  "{key}": [\n{body}\n  ]{comma}')
        else:
            lines.append(f'  "{key}": {compact}{comma}')
    return "{\n" + "\n".join(lines) + "\n}\n"


def write_meta(text: str, meta: dict) -> str:
    return _META_RE.sub(lambda m: "```json vendor-sync\n" + format_meta(meta) + "```", text, count=1)


def replace_generated(text: str, name: str, content: str, where: str) -> str:
    begin, end = f"<!-- vendor-sync:begin {name} -->", f"<!-- vendor-sync:end {name} -->"
    pattern = re.compile(re.escape(begin) + r"\n.*?" + re.escape(end), re.S)
    if not pattern.search(text):
        raise ToolError(f"{where}: no `{begin}` ... `{end}` markers")
    return pattern.sub(lambda m: f"{begin}\n{content}{end}", text, count=1)


def has_generated(text: str, name: str) -> bool:
    return f"<!-- vendor-sync:begin {name} -->" in text


def render_pin(meta: dict) -> str:
    url = GITHUB + meta["repository"]
    return (f"- Repository: {url}\n"
            f"- Commit: [`{meta['commit']}`]({url}/tree/{meta['commit']}) ({meta['branch']}, {meta['date']})\n")


def md_cell(text: str, limit: int = 0) -> str:
    text = text.replace("|", "\\|")
    if limit and len(text) > limit:
        text = text[:limit - 3] + "..."
    return text


def render_sites(sites: dict) -> str:
    """sites: vendor-relative path -> Parsed (files with blocks)."""
    if not sites:
        return "No directive sites.\n"
    rows = ["| File | Lines | Kind | First line of the `LOCALTEST` branch |", "| --- | --- | --- | --- |"]
    for path in sorted(sites):
        for b in sites[path].blocks:
            first = b.first_local_line()
            rows.append(f"| `{md_cell(path)}` | {b.start}-{b.end} | {b.kind} | "
                        f"{('`' + md_cell(first, 90) + '`') if first else '(empty)'} |")
    return "\n".join(rows) + "\n"


# --------------------------------------------------------------------------------------------------------------------
# Components


@dataclass
class Component:
    name: str
    root: Path
    meta: dict
    text: str

    @property
    def dir_rel(self) -> str:
        return f"{COMPONENTS_REL}/{self.name}"

    @property
    def dir(self) -> Path:
        return self.root / self.dir_rel

    @property
    def vendor_rel(self) -> str:
        return f"{self.dir_rel}/vendor"

    @property
    def vendor(self) -> Path:
        return self.root / self.vendor_rel

    @property
    def upstream_md(self) -> Path:
        return self.vendor / "UPSTREAM.md"

    @property
    def group(self) -> str | None:
        return self.meta.get("group")

    @property
    def patches_mode(self) -> bool:
        return self.meta.get("local", "directives") == "patches"

    def projects(self) -> list:
        return [p if isinstance(p, dict) else {"path": p} for p in self.meta.get("projects", [])]

    def save(self, meta: dict | None = None, sites: dict | None = None):
        text = self.text
        if meta is not None:
            self.meta = meta
            text = write_meta(text, meta)
        if has_generated(text, "pin"):
            text = replace_generated(text, "pin", render_pin(self.meta), str(self.upstream_md))
        if sites is not None and has_generated(text, "sites"):
            text = replace_generated(text, "sites", render_sites(sites), str(self.upstream_md))
        self.text = text
        self.upstream_md.write_text(text)


def load_components(root: Path = ROOT) -> list:
    out = []
    base = root / COMPONENTS_REL
    for md in sorted(base.glob("*/vendor/UPSTREAM.md")):
        text = md.read_text()
        name = md.parent.parent.name
        out.append(Component(name, root, read_meta(text, str(md.relative_to(root))), text))
    return out


def select(components: list, names: list, all_: bool) -> list:
    if all_ or not names:
        if names:
            raise ToolError("give names or --all, not both")
        if not all_:
            raise ToolError("name a component or a group, or pass --all")
        return components
    chosen = []
    for name in names:
        hit = [c for c in components if c.name == name or c.group == name]
        if not hit:
            known = sorted({c.name for c in components} | {c.group for c in components if c.group})
            raise ToolError(f"unknown component or group `{name}` (known: {', '.join(known)})")
        chosen += [c for c in hit if c not in chosen]
    return chosen


def under(path: str, paths: list) -> bool:
    return any(p in (".", "") or path == p.rstrip("/") or path.startswith(p.rstrip("/") + "/") for p in paths)


# --------------------------------------------------------------------------------------------------------------------
# Upstream clones


def default_clones() -> Path:
    if os.environ.get("VENDOR_SYNC_CLONES"):
        return Path(os.environ["VENDOR_SYNC_CLONES"])
    cache = os.environ.get("XDG_CACHE_HOME") or os.path.expanduser("~/.cache")
    return Path(cache) / "localtest-vendor"


class Upstream:
    """A blobless clone of one upstream repository."""

    def __init__(self, clones: Path, repository: str, url: str | None = None, quiet=False):
        owner, name = repository.split("/")
        self.repository = repository
        nested, flat = clones / owner / name, clones / name
        self.path = nested if (nested / ".git").exists() or nested.joinpath("HEAD").exists() else (
            flat if (flat / ".git").exists() else nested)
        self.url = url or f"{GITHUB}{repository}.git"
        self.quiet = quiet
        self._trees = {}

    def ensure(self):
        if not self.path.exists():
            self.path.parent.mkdir(parents=True, exist_ok=True)
            if not self.quiet:
                print(f"cloning {self.repository} (blobless) into {self.path}", file=sys.stderr)
            git("clone", "-q", "--filter=blob:none", "--no-checkout", self.url, str(self.path))
        return self

    def git(self, *args, **kw) -> bytes:
        return git(*args, cwd=self.path, **kw)

    def fetch(self, branch: str):
        self.git("fetch", "-q", "--no-tags", "origin", f"+refs/heads/{branch}:refs/remotes/origin/{branch}")

    def has_commit(self, commit: str) -> bool:
        return subprocess.run(["git", "cat-file", "-e", f"{commit}^{{commit}}"], cwd=self.path,
                              capture_output=True).returncode == 0

    def ensure_commit(self, commit: str):
        if not self.has_commit(commit):
            self.git("fetch", "-q", "--no-tags", "origin", commit)

    def resolve(self, ref: str) -> str:
        return self.git("rev-parse", "--verify", f"{ref}^{{commit}}").decode().strip()

    def commit_date(self, commit: str) -> str:
        return self.git("show", "-s", "--format=%cs", commit).decode().strip()

    def tree(self, commit: str, paths: list) -> dict:
        """path -> (mode, sha) of every blob and symlink under paths at commit (submodules left out)."""
        key = (commit, tuple(paths))
        if key not in self._trees:
            spec = [] if paths == ["."] else ["--", *paths]
            out = self.git("ls-tree", "-r", "-z", "--full-tree", commit, *spec)
            files = {}
            for entry in out.split(b"\0"):
                if not entry:
                    continue
                info, path = entry.split(b"\t", 1)
                mode, kind, sha = info.decode().split()
                path = path.decode("utf-8", "surrogateescape")
                if kind == "blob" and under(path, paths):
                    files[path] = (mode, sha)
            self._trees[key] = files
        return self._trees[key]

    def blobs(self, shas) -> dict:
        shas = sorted(set(shas))
        if not shas:
            return {}
        env = dict(os.environ, GIT_NO_LAZY_FETCH="1")
        check = self.git("cat-file", "--batch-check=%(objectname) %(objecttype)", input="\n".join(shas).encode() + b"\n",
                         env=env, check=False).decode().splitlines()
        missing = [line.split()[0] for line in check if line.endswith(" missing")]
        if missing:
            self.git("-c", "fetch.negotiationAlgorithm=noop", "fetch", "-q", "--no-tags", "--no-write-fetch-head",
                     "--recurse-submodules=no", "--filter=blob:none", "--stdin", "origin",
                     input="\n".join(missing).encode() + b"\n")
        out = self.git("cat-file", "--batch", input="\n".join(shas).encode() + b"\n")
        result, pos = {}, 0
        while pos < len(out):
            nl = out.index(b"\n", pos)
            header = out[pos:nl].decode().split()
            if header[1] == "missing":
                raise ToolError(f"{self.repository}: blob {header[0]} missing")
            size = int(header[2])
            result[header[0]] = out[nl + 1:nl + 1 + size]
            pos = nl + 1 + size + 1
        return result

    def changed(self, old: str, new: str, paths: list) -> list:
        """(status, path, new path) of the files under paths that differ between old and new, renames detected."""
        spec = [] if paths == ["."] else ["--", *paths]
        out = self.git("diff", "-z", "--name-status", "-M", old, new, *spec).split(b"\0")
        result, i = [], 0
        while i < len(out) and out[i]:
            status = out[i].decode()
            if status[0] in "RC":
                result.append((status[0], out[i + 1].decode(), out[i + 2].decode()))
                i += 3
            else:
                result.append((status[0], out[i + 1].decode(), out[i + 1].decode()))
                i += 2
        return result

    def log(self, old: str | None, new: str, paths: list | None = None) -> list:
        """Commits after old up to new touching paths; with old None, the last 20 up to new."""
        spec = [] if not paths or paths == ["."] else ["--", *paths]
        revs = [f"{old}..{new}"] if old else ["-n", "20", new]
        out = self.git("log", "--format=%h %cs %s", *revs, *spec).decode()
        return [line for line in out.splitlines() if line]

    def is_ancestor(self, old: str, new: str) -> bool:
        return subprocess.run(["git", "merge-base", "--is-ancestor", old, new], cwd=self.path,
                              capture_output=True).returncode == 0


class Clones:
    def __init__(self, path: Path, quiet=False):
        self.path = path
        self.quiet = quiet
        self._repos = {}

    def get(self, repository: str) -> Upstream:
        if repository not in self._repos:
            self._repos[repository] = Upstream(self.path, repository, quiet=self.quiet).ensure()
        return self._repos[repository]


# --------------------------------------------------------------------------------------------------------------------
# Our side


def mode_of(path: Path) -> str:
    st = path.lstat()
    if stat.S_ISLNK(st.st_mode):
        return "120000"
    return "100755" if st.st_mode & stat.S_IXUSR else "100644"


def read_entry(path: Path) -> bytes:
    if path.is_symlink():
        return os.fsencode(os.readlink(path))
    return path.read_bytes()


class Vendor:
    """The files of one component's vendor/ as committed (index) or as in the working tree, UPSTREAM.md left out."""

    def __init__(self, comp: Component, index: bool = False):
        self.comp = comp
        self.index = index
        self.files = {}  # vendor-relative path -> (mode, sha)
        self.unmerged = []
        self._content = {}
        prefix = comp.vendor_rel + "/"
        if index:
            out = git("ls-files", "-s", "-z", "--", comp.vendor_rel, cwd=comp.root)
            for entry in out.split(b"\0"):
                if not entry:
                    continue
                info, path = entry.split(b"\t", 1)
                mode, sha, stage = info.decode().split()
                rel = path.decode("utf-8", "surrogateescape")[len(prefix):]
                if stage != "0":
                    if rel not in self.unmerged:
                        self.unmerged.append(rel)
                    continue
                self.files[rel] = (mode, sha)
        else:
            out = git("ls-files", "-z", "--cached", "--others", "--exclude-standard", "--", comp.vendor_rel,
                      cwd=comp.root)
            for path in sorted(set(out.split(b"\0")) - {b""}):
                rel = path.decode("utf-8", "surrogateescape")[len(prefix):]
                full = comp.vendor / rel
                if not full.is_symlink() and not full.exists():
                    continue
                data = read_entry(full)
                self._content[rel] = data
                self.files[rel] = (mode_of(full), blob_sha(data))
            out = git("ls-files", "-z", "--unmerged", "--", comp.vendor_rel, cwd=comp.root)
            for entry in out.split(b"\0"):
                if entry:
                    rel = entry.split(b"\t", 1)[1].decode("utf-8", "surrogateescape")[len(prefix):]
                    if rel not in self.unmerged:
                        self.unmerged.append(rel)
        self.files.pop("UPSTREAM.md", None)

    def content(self, rel: str) -> bytes:
        if rel not in self._content:
            if self.index:
                self._content[rel] = git("cat-file", "blob", self.files[rel][1], cwd=self.comp.root)
            else:
                self._content[rel] = read_entry(self.comp.vendor / rel)
        return self._content[rel]


# --------------------------------------------------------------------------------------------------------------------
# check


@dataclass
class CheckResult:
    errors: list = field(default_factory=list)
    notes: list = field(default_factory=list)
    warnings: list = field(default_factory=list)
    sites: dict = field(default_factory=dict)  # path -> Parsed
    candidates: list = field(default_factory=list)  # (path, Block, reason)


def compare(comp: Component, vendor: Vendor, upstream_files: dict, upstream_blobs: dict) -> CheckResult:
    """The invariant for one component, given upstream's files at the pin and their contents for the files that
    differ. Errors name the file relative to the repository root, with the line where there is one."""
    r = CheckResult()
    v = comp.vendor_rel
    for rel in vendor.unmerged:
        r.errors.append(f"{v}/{rel}: unmerged (resolve the conflict, then `git add` it)")
    tolerated = []
    for rel in sorted(set(upstream_files) | set(vendor.files)):
        if rel in vendor.unmerged:
            continue
        if rel not in vendor.files:
            r.errors.append(f"{v}/{rel}: missing (upstream has it at the pin)")
            continue
        if rel not in upstream_files:
            r.errors.append(f"{v}/{rel}: not upstream at the pin (an added file belongs outside vendor/)")
            continue
        (umode, usha), (vmode, vsha) = upstream_files[rel], vendor.files[rel]
        if vmode != umode:
            r.errors.append(f"{v}/{rel}: mode {vmode}, upstream {umode}")
        if vsha == usha:
            continue
        ours, theirs = vendor.content(rel), upstream_blobs[usha]
        marker = conflict_marker_line(ours)
        if marker:
            r.errors.append(f"{v}/{rel}:{marker}: conflict marker")
            continue
        if has_directives(ours):
            if comp.patches_mode:
                r.errors.append(f"{v}/{rel}: LOCALTEST directives in a `patches` component (put the change in a patch)")
                continue
            try:
                parsed = parse(ours)
            except DirectiveError as e:
                r.errors.append(f"{v}/{rel}:{e.line}: {e.message}")
                continue
            how = tolerated_difference(parsed.production, theirs)
            if how is None:
                ol, ul, ot, ut = first_difference(parsed.production, theirs, parsed.production_lines)
                r.errors.append(f"{v}/{rel}:{ol}: production view differs from upstream (upstream line {ul})\n"
                                f"    ours:     {ot}\n    upstream: {ut}")
                continue
            if how:
                tolerated.append(f"{rel} ({how})")
            r.sites[rel] = parsed
            for b in parsed.blocks:
                reason = b.removal_candidate()
                if reason:
                    r.candidates.append((rel, b, reason))
            continue
        how = tolerated_difference(ours, theirs)
        if how is None:
            ol, ul, ot, ut = first_difference(ours, theirs)
            r.errors.append(f"{v}/{rel}:{ol}: differs from upstream (upstream line {ul}) and has no LOCALTEST block\n"
                            f"    ours:     {ot}\n    upstream: {ut}")
        else:
            tolerated.append(f"{rel} ({how})")
    if tolerated:
        r.notes.append("tolerated: " + ", ".join(tolerated))
    return r


def site_blobs_needed(vendor: Vendor, upstream_files: dict) -> list:
    return [upstream_files[rel][1] for rel, (_, sha) in vendor.files.items()
            if rel in upstream_files and upstream_files[rel][1] != sha]


def check_patches(comp: Component, index: bool) -> list:
    errors = []
    folder = comp.dir / comp.meta["patches"]
    patches = sorted(folder.glob("*.patch"))
    if not patches:
        errors.append(f"{comp.dir_rel}/{comp.meta['patches']}: no patches")
    for patch in patches:
        args = ["apply", "--check", f"--directory={comp.vendor_rel}", "-p1"] + (["--cached"] if index else [])
        p = subprocess.run(["git", *args, str(patch)], cwd=comp.root, capture_output=True)
        if p.returncode != 0:
            errors.append(f"{patch.relative_to(comp.root)}: does not apply to vendor/: "
                          f"{p.stderr.decode(errors='replace').strip()}")
    return errors


def follow_warnings(comp: Component) -> tuple:
    """Versions our build files pin 'at the host's version', against what the host resolved in its last build."""
    warnings, notes = [], []
    if not comp.meta.get("follow"):
        return warnings, notes
    resolved, built = {}, []
    for project in comp.projects():
        assets = (comp.vendor / project["path"]).parent / "obj" / "project.assets.json"
        if (comp.vendor / project["path"]).is_dir():
            assets = comp.vendor / project["path"] / "obj" / "project.assets.json"
        if assets.exists():
            built.append(project["path"])
            for key in json.loads(assets.read_text()).get("libraries", {}):
                name, _, version = key.partition("/")
                resolved.setdefault(name.lower(), set()).add(version)
    if not built:
        notes.append("versions that follow the host: not compared (no obj/project.assets.json; build the host first)")
        return warnings, notes
    for f in comp.meta["follow"]:
        file = comp.dir / f.get("file", "localtest.build.targets")
        m = re.search(r'<PackageReference\s+Include="' + re.escape(f["package"]) +
                      r'"\s+(?:Version|VersionOverride)="([^"]+)"', file.read_text() if file.exists() else "")
        if not m:
            warnings.append(f"follow: {f['package']} not found in {file.relative_to(comp.dir)}")
            continue
        ours = m.group(1)
        target = f.get("follows", f["package"])
        versions = resolved.get(target.lower())
        if not versions:
            warnings.append(f"follow: the host does not resolve {target}")
        elif versions != {ours}:
            warnings.append(f"follow: {file.relative_to(comp.dir)} pins {f['package']} {ours}, the host resolves "
                            f"{target} {', '.join(sorted(versions))}: move it to the host's version")
    return warnings, notes


def shared_fakes_errors(root: Path) -> list:
    base = root / COMPONENTS_REL
    shared = base / "shared" / "fakes"
    errors = []
    if not shared.exists():
        return errors
    for src in sorted(p for p in shared.rglob("*") if p.is_file()):
        rel = src.relative_to(shared)
        for comp_fakes in sorted(base.glob("*/fakes")):
            if comp_fakes == shared:
                continue
            for copy in comp_fakes.rglob(rel.name):
                if copy.is_file() and copy.relative_to(comp_fakes).as_posix().endswith(rel.as_posix()):
                    if copy.read_bytes() != src.read_bytes():
                        errors.append(f"{copy.relative_to(base)}: differs from shared/fakes/{rel.as_posix()}")
    return errors


def generated_errors(comp: Component, sites: dict) -> tuple:
    """(errors, warnings): missing markers are errors, stale generated text is a warning (`render` fixes it)."""
    errors, warnings = [], []
    if has_generated(comp.text, "pin"):
        if replace_generated(comp.text, "pin", render_pin(comp.meta), "") != comp.text:
            warnings.append(f"{comp.vendor_rel}/UPSTREAM.md: the pin bullets are stale (run `vendor-sync.py render {comp.name}`)")
    else:
        errors.append(f"{comp.vendor_rel}/UPSTREAM.md: no `vendor-sync:begin pin` markers")
    if not comp.patches_mode:
        if has_generated(comp.text, "sites"):
            if replace_generated(comp.text, "sites", render_sites(sites), "") != comp.text:
                warnings.append(f"{comp.vendor_rel}/UPSTREAM.md: the site table is stale (run `vendor-sync.py render {comp.name}`)")
        else:
            errors.append(f"{comp.vendor_rel}/UPSTREAM.md: no `vendor-sync:begin sites` markers")
    return errors, warnings


def run_check(comp: Component, clones: Clones, index=False) -> CheckResult:
    up = clones.get(comp.meta["repository"])
    up.ensure_commit(comp.meta["commit"])
    upstream_files = up.tree(comp.meta["commit"], comp.meta["paths"])
    vendor = Vendor(comp, index=index)
    blobs = up.blobs(site_blobs_needed(vendor, upstream_files))
    r = compare(comp, vendor, upstream_files, blobs)
    if comp.patches_mode:
        r.errors += check_patches(comp, index)
    errors, warnings = generated_errors(comp, r.sites)
    r.errors += errors
    if not r.errors:  # with a broken site file the table cannot be compared
        r.warnings += warnings
    w, n = follow_warnings(comp)
    r.warnings += w
    r.notes += n
    return r


def cmd_check(args) -> int:
    components = load_components(args.root)
    chosen = select(components, args.names, args.all)
    clones = Clones(args.clones)
    failed = 0
    groups = {}
    for c in components:
        if c.group:
            groups.setdefault(c.group, set()).add((c.meta["repository"], c.meta["commit"]))
    for comp in chosen:
        r = run_check(comp, clones, index=args.index)
        if comp.group and len(groups[comp.group]) > 1:
            r.errors.append(f"group {comp.group}: its components are not at one repository and commit")
        status = "FAIL" if r.errors else "ok"
        blocks = sum(len(p.blocks) for p in r.sites.values())
        detail = (f"patches ({len(list((comp.dir / comp.meta['patches']).glob('*.patch')))} apply)"
                  if comp.patches_mode else f"{len(r.sites)} site files, {blocks} blocks")
        print(f"{status} {comp.name}: {comp.meta['repository']}@{comp.meta['commit'][:7]}, {detail}")
        for e in r.errors:
            print(f"  error: {e}")
        for w in r.warnings:
            print(f"  warning: {w}")
        for path, b, reason in r.candidates:
            print(f"  candidate for removal: {path}:{b.start}-{b.end} ({b.kind}): {reason}")
        for n in r.notes:
            print(f"  note: {n}")
        failed += bool(r.errors)
    if args.all:
        errs = shared_fakes_errors(args.root)
        for e in errs:
            print(f"FAIL shared fakes: {e}")
        if not errs:
            print("ok shared fakes: every copy of components/shared/fakes/ is identical")
        failed += bool(errs)
    return 1 if failed else 0


# --------------------------------------------------------------------------------------------------------------------
# render


def cmd_render(args) -> int:
    components = load_components(args.root)
    clones = Clones(args.clones)
    for comp in select(components, args.names, args.all):
        vendor = Vendor(comp)
        sites = {}
        for rel in vendor.files:
            data = vendor.content(rel)
            if has_directives(data):
                try:
                    sites[rel] = parse(data)
                except DirectiveError as e:
                    raise ToolError(f"{comp.vendor_rel}/{rel}:{e.line}: {e.message}") from e
        before = comp.text
        comp.save(sites=sites)
        print(f"{comp.name}: {'updated' if comp.text != before else 'unchanged'}")
    del clones
    return 0


# --------------------------------------------------------------------------------------------------------------------
# status


def merge_text(ours: bytes, base: bytes, theirs: bytes, labels=("localtest", "old", "new")) -> tuple:
    """git merge-file --diff3; (merged bytes, number of conflicts; -1 on a binary or failed merge)."""
    with tempfile.TemporaryDirectory() as tmp:
        files = []
        for name, data in (("ours", ours), ("base", base), ("theirs", theirs)):
            p = Path(tmp) / name
            p.write_bytes(data)
            files.append(str(p))
        p = subprocess.run(["git", "merge-file", "-p", "--diff3", "-L", labels[0], "-L", labels[1], "-L", labels[2],
                            *files], capture_output=True)
        if p.returncode < 0 or p.returncode > 127:
            return ours, -1
        return p.stdout, p.returncode


@dataclass
class SiteMerge:
    path: str
    ok: bool
    merged: bytes
    reason: str = ""
    moved: bool = False
    upstream_changed_replaced: list = field(default_factory=list)  # block line ranges whose production branch moved


def reanchor(before: Parsed, base: bytes, theirs: bytes) -> tuple:
    """Put the blocks of a file whose production view is base into theirs, where upstream left the lines they stand
    on alone: an addition between two lines upstream kept next to each other, a replacement over lines upstream kept
    unchanged (an upstream insertion at its edge goes outside it). (merged bytes or None, [why not, per block])."""
    b, t = split_lines(base), split_lines(theirs)
    if b"".join(b) != before.production:
        return None, ["the production view is not upstream's old file byte for byte"]
    ops = [op for op in difflib.SequenceMatcher(None, b, t, autojunk=False).get_opcodes() if op[0] == "equal"]
    placed, problems = [], []
    for block in before.blocks:
        k, n = block.at, len(block.production)
        j = None
        for _, i1, i2, j1, j2 in ops:
            if n and i1 <= k and k + n <= i2:
                j = j1 + k - i1
            elif not n and (i1 < k < i2 or (k == i1 == 0 and j1 == 0) or (k == i2 == len(b) and j2 == len(t))):
                j = j1 + k - i1
        if j is None:
            what = ("upstream changed the code it replaces" if n else
                    "upstream changed or inserted lines right next to it")
            problems.append(f"{block.kind} at {block.start}-{block.end}: {what}")
        placed.append((j, n, block))
    if problems:
        return None, problems
    out, pos = [], 0
    for j, n, block in sorted(placed, key=lambda p: p[0]):
        out += t[pos:j]
        out += block.raw
        pos = j + n
    out += t[pos:]
    return b"".join(out), []


def merge_site(path: str, ours: bytes, base: bytes, theirs: bytes, labels) -> SiteMerge:
    before = parse(ours)
    merged, conflicts = merge_text(ours, base, theirs, labels)
    if conflicts != 0:
        anchored, problems = reanchor(before, base, theirs)
        if anchored is None:
            return SiteMerge(path, False, merged, ("textual conflict" if conflicts > 0 else "binary or failed merge")
                             + "; " + "; ".join(problems))
        merged = anchored
    try:
        after = parse(merged)
    except DirectiveError as e:
        return SiteMerge(path, False, merged, f"the merged file does not parse: {e}")
    if tolerated_difference(after.production, theirs) is None:
        ol, ul, _, _ = first_difference(after.production, theirs, after.production_lines)
        return SiteMerge(path, False, merged, f"merged production view differs from upstream at line {ol}")
    if [(b.kind, b.local_text()) for b in before.blocks] != [(b.kind, b.local_text()) for b in after.blocks]:
        return SiteMerge(path, False, merged, "a LOCALTEST branch changed in the merge (a hunk landed in a block)")
    moved = [(b.start, b.end) for b in before.blocks] != [(b.start, b.end) for b in after.blocks]
    changed = [f"{a.start}-{a.end}" for a, b in zip(before.blocks, after.blocks)
               if a.kind == "replacement" and a.production_text() != b.production_text()]
    return SiteMerge(path, True, merged, moved=moved, upstream_changed_replaced=changed)


def component_status(comp: Component, clones: Clones, fetch: bool, today: dt.date) -> dict:
    meta = comp.meta
    up = clones.get(meta["repository"])
    if fetch:
        up.fetch(meta["branch"])
    up.ensure_commit(meta["commit"])
    head = up.resolve(f"refs/remotes/origin/{meta['branch']}")
    pin, paths = meta["commit"], meta["paths"]
    on_branch = up.is_ancestor(pin, head)
    behind_all = len(up.log(pin, head)) if on_branch else None
    touching = up.log(pin, head, paths) if on_branch else []
    changed = up.changed(pin, head, paths)
    counts = {"M": 0, "A": 0, "D": 0}
    for status_, old, new in changed:
        if status_ == "R":
            counts["D"] += 1
            counts["A"] += 1
        else:
            counts[status_ if status_ in counts else "M"] += 1
    vendor = Vendor(comp, index=True)
    upstream_files = up.tree(pin, paths)
    blobs = up.blobs(site_blobs_needed(vendor, upstream_files))
    r = compare(comp, vendor, upstream_files, blobs)
    adds = sum(b.kind == "addition" for p in r.sites.values() for b in p.blocks)
    reps = sum(b.kind == "replacement" for p in r.sites.values() for b in p.blocks)
    new_files = up.tree(head, paths)
    site_changes = []
    changed_sites = [rel for rel in r.sites if new_files.get(rel, (None, None))[1] != upstream_files[rel][1]]
    if changed_sites:
        need = [upstream_files[rel][1] for rel in changed_sites] + [new_files[rel][1] for rel in changed_sites
                                                                    if rel in new_files]
        more = up.blobs(need)
        for rel in changed_sites:
            if rel not in new_files:
                site_changes.append(f"`{rel}`: deleted or moved upstream")
                continue
            m = merge_site(rel, vendor.content(rel), more[upstream_files[rel][1]], more[new_files[rel][1]],
                           ("localtest", pin[:7], head[:7]))
            site_changes.append(f"`{rel}`: " + ("merges clean" if m.ok else f"conflicts ({m.reason})"))
    mirrors = []
    for mirror in meta.get("mirrors", []):
        old_tree, new_tree = up.tree(pin, [mirror["upstream"]]), up.tree(head, [mirror["upstream"]])
        if old_tree.get(mirror["upstream"]) == new_tree.get(mirror["upstream"]):
            continue
        what = f"`{mirror['upstream']}`"
        if mirror.get("merge") and mirror["upstream"] in old_tree and mirror["upstream"] in new_tree:
            ours_path = comp.dir / mirror["ours"]
            b = up.blobs([old_tree[mirror["upstream"]][1], new_tree[mirror["upstream"]][1]])
            _, n = merge_text(ours_path.read_bytes(), b[old_tree[mirror["upstream"]][1]],
                              b[new_tree[mirror["upstream"]][1]])
            what += ": merges clean into ours" if n == 0 else ": the merge into ours conflicts"
        else:
            what += f" changed (mirrored by `{mirror['ours']}`)"
        mirrors.append(what)
    watched = [f"`{p}`" for _, p, _ in up.changed(pin, head, meta.get("watch", []))] if meta.get("watch") else []
    pin_date = dt.date.fromisoformat(up.commit_date(pin))
    return {
        "component": comp.name,
        "repository": meta["repository"],
        "branch": meta["branch"],
        "group": comp.group,
        "pin": pin,
        "pin_date": pin_date.isoformat(),
        "age_days": (today - pin_date).days,
        "head": head,
        "on_branch": on_branch,
        "behind": behind_all,
        "behind_touching_paths": len(touching),
        "commits_touching_paths": touching,
        "files_changed": counts,
        "site_files": len(r.sites),
        "blocks": {"addition": adds, "replacement": reps},
        "site_files_changed_upstream": site_changes,
        "mirrors_changed_upstream": mirrors,
        "watched_changed_upstream": watched,
        "check_errors": r.errors,
        "candidates": [f"{p}:{b.start}-{b.end} ({reason})" for p, b, reason in r.candidates],
        "reminders": meta.get("reminders", []),
        "local": meta.get("local", "directives"),
        "patches": len(list((comp.dir / meta["patches"]).glob("*.patch"))) if comp.patches_mode else 0,
    }


def status_table(rows: list) -> str:
    out = ["| Component | Upstream | Pin (commit date, age) | Behind the default branch: commits / touching the paths | "
           "Files changed in the paths (M/A/D) | Sites: files, blocks (addition/replacement) | "
           "Site files changed upstream | Mirrored files changed upstream | Watched paths changed |",
           "| --- | --- | --- | --- | --- | --- | --- | --- | --- |"]
    for s in rows:
        c = s["files_changed"]
        total = sum(c.values())
        behind = (f"{s['behind']} / {s['behind_touching_paths']}" if s["on_branch"]
                  else f"pin not on `{s['branch']}`")
        sites = (f"{s['patches']} patches" if s["local"] == "patches" else
                 f"{s['site_files']}, {s['blocks']['addition'] + s['blocks']['replacement']} "
                 f"({s['blocks']['addition']}/{s['blocks']['replacement']})")
        out.append(" | ".join([
            f"| {s['component']}", s["repository"], f"`{s['pin'][:7]}` ({s['pin_date'][5:]}, {s['age_days']} d)",
            behind, f"{total} ({c['M']}/{c['A']}/{c['D']})" if total else "0", sites,
            "; ".join(s["site_files_changed_upstream"]) or "none",
            "; ".join(s["mirrors_changed_upstream"]) or "none",
            "; ".join(s["watched_changed_upstream"]) or "none"]) + " |")
    return "\n".join(out)


def cmd_status(args) -> int:
    components = load_components(args.root)
    chosen = select(components, args.names, args.all)
    clones = Clones(args.clones, quiet=args.json)
    today = dt.date.today()
    fetched = set()
    rows = []
    for comp in chosen:
        key = (comp.meta["repository"], comp.meta["branch"])
        rows.append(component_status(comp, clones, args.fetch and key not in fetched, today))
        fetched.add(key)
    if args.json:
        print(json.dumps(rows, indent=2))
        return 0
    print(f"Upstream status on {today.isoformat()} (clones in {args.clones})\n")
    print(status_table(rows))
    extra = []
    for s in rows:
        for e in s["check_errors"]:
            extra.append(f"- {s['component']}: check fails: {e.splitlines()[0]}")
        for cand in s["candidates"]:
            extra.append(f"- {s['component']}: candidate for removal: {cand}")
        for rem in s["reminders"]:
            extra.append(f"- {s['component']}: reminder: {rem}")
    if extra:
        print()
        print("\n".join(extra))
    return 0


# --------------------------------------------------------------------------------------------------------------------
# sync


def image_targets(root: Path, comp: Component) -> list:
    """(image name, Dockerfile) pairs the images workflow builds from the component folder."""
    wf = root / ".github" / "workflows" / "images.yml"
    if not wf.exists():
        return []
    out = []
    for m in re.finditer(r"\{\s*name:\s*([\w-]+),\s*context:\s*([^,]+),\s*file:\s*([^ }]+)\s*\}", wf.read_text()):
        if m.group(2).strip() == comp.dir_rel:
            out.append((m.group(1), m.group(3).strip()[len(comp.dir_rel) + 1:]))
    return out


def manifest_groups(root: Path) -> dict:
    """container name -> group names (from studioctl's manifest)."""
    path = root / "localtest/studioctl/src/cli/internal/localtestv2/manifest/manifest.json"
    if not path.exists():
        return {}
    groups = {}
    for g in json.loads(path.read_text()).get("groups", []):
        for c in g.get("containers", []):
            groups.setdefault(c, []).append(g["name"])
    return groups


class Stager:
    """Writes files into the working tree and the index of our repository, from blob contents."""

    def __init__(self, root: Path):
        self.root = root
        self.index_lines = []
        self._objects = {}

    def _store(self, data: bytes) -> str:
        sha = blob_sha(data)
        if sha not in self._objects:
            self._objects[sha] = data
        return sha

    def write(self, rel_to_root: str, mode: str, data: bytes):
        path = self.root / rel_to_root
        path.parent.mkdir(parents=True, exist_ok=True)
        if path.is_symlink() or path.exists():
            path.unlink()
        if mode == "120000":
            os.symlink(os.fsdecode(data), path)
        else:
            path.write_bytes(data)
            path.chmod(0o755 if mode == "100755" else 0o644)
        sha = self._store(data)
        self.index_lines.append(f"0 {ZERO}\t{rel_to_root}")
        self.index_lines.append(f"{mode} {sha} 0\t{rel_to_root}")

    def remove(self, rel_to_root: str):
        path = self.root / rel_to_root
        if path.is_symlink() or path.exists():
            path.unlink()
        parent = path.parent
        while parent != self.root and parent.exists() and not any(parent.iterdir()):
            parent.rmdir()
            parent = parent.parent
        self.index_lines.append(f"0 {ZERO}\t{rel_to_root}")

    def conflict(self, rel_to_root: str, worktree: bytes, mode: str, base, ours, theirs):
        """Leave a path unmerged: the working tree gets `worktree`, the index stages 1-3 (None for absent)."""
        path = self.root / rel_to_root
        path.write_bytes(worktree)
        self.index_lines.append(f"0 {ZERO}\t{rel_to_root}")
        for stage, data in ((1, base), (2, ours), (3, theirs)):
            if data is not None:
                self.index_lines.append(f"{mode} {self._store(data)} {stage}\t{rel_to_root}")

    def flush(self):
        if self._objects:
            with tempfile.TemporaryDirectory() as tmp:
                names = []
                for sha, data in self._objects.items():
                    p = Path(tmp) / sha
                    p.write_bytes(data)
                    names.append(str(p))
                out = git("hash-object", "-w", "--no-filters", "--stdin-paths", cwd=self.root,
                          input="\n".join(names).encode() + b"\n").decode().split()
                if out != list(self._objects):
                    raise ToolError("hash-object wrote different blob ids than expected")
        if self.index_lines:
            git("update-index", "-z", "--index-info", cwd=self.root,
                input=b"".join(line.encode("utf-8", "surrogateescape") + b"\0" for line in self.index_lines))
        self.index_lines, self._objects = [], {}


@dataclass
class SyncReport:
    comp: Component
    old: str
    new: str
    old_repository: str
    updated: list = field(default_factory=list)
    added: list = field(default_factory=list)
    removed: list = field(default_factory=list)
    merged: list = field(default_factory=list)  # SiteMerge ok
    conflicts: list = field(default_factory=list)  # (path, reason)
    sites_unchanged: int = 0
    patches: list = field(default_factory=list)  # (patch, result)
    mirrors: list = field(default_factory=list)
    watched: list = field(default_factory=list)
    candidates: list = field(default_factory=list)
    commits: list = field(default_factory=list)
    date: str = ""


def refresh_patches(comp: Component, old_files: dict, new_files: dict, old_blobs, new_blobs, new: str) -> list:
    """Carry each patch from the old tree to the new one by cherry-picking it in a scratch repository."""
    folder = comp.dir / comp.meta["patches"]
    results = []
    with tempfile.TemporaryDirectory() as tmp:
        repo = Path(tmp)
        git("init", "-q", cwd=repo)
        git("config", "user.email", "vendor-sync@localhost", cwd=repo)
        git("config", "user.name", "vendor-sync", cwd=repo)
        git("config", "core.autocrlf", "false", cwd=repo)
        patches = sorted(folder.glob("*.patch"))
        touched = set()
        for patch in patches:
            for m in re.finditer(r"^(?:---|\+\+\+) [ab]/(\S+)", patch.read_text(), re.M):
                touched.add(m.group(1))

        def fill(files, blobs):
            for rel in touched:
                p = repo / rel
                if p.exists():
                    p.unlink()
                if rel in files:
                    p.parent.mkdir(parents=True, exist_ok=True)
                    p.write_bytes(blobs(files[rel][1]))
            git("add", "-A", ".", cwd=repo)
            git("commit", "-q", "--allow-empty", "-m", "tree", cwd=repo)
            return git("rev-parse", "HEAD", cwd=repo).decode().strip()

        old_base = fill(old_files, old_blobs)
        picks = []
        for patch in patches:
            p = subprocess.run(["git", "apply", "-p1", str(patch)], cwd=repo, capture_output=True)
            if p.returncode != 0:
                results.append((patch.name, "does not apply to the old pin either: " + p.stderr.decode().strip()))
                picks.append(None)
                continue
            git("add", "-A", ".", cwd=repo)
            git("commit", "-q", "-m", patch.name, cwd=repo)
            picks.append(git("rev-parse", "HEAD", cwd=repo).decode().strip())
        git("checkout", "-q", "--detach", old_base, cwd=repo)
        fill(new_files, new_blobs)
        for patch, pick in zip(patches, picks):
            if pick is None:
                continue
            before = git("rev-parse", "HEAD", cwd=repo).decode().strip()
            p = subprocess.run(["git", "cherry-pick", pick], cwd=repo, capture_output=True)
            if p.returncode != 0:
                files = git("diff", "--name-only", "--diff-filter=U", cwd=repo).decode().split()
                subprocess.run(["git", "cherry-pick", "--abort"], cwd=repo, capture_output=True)
                results.append((patch.name, "conflicts in " + ", ".join(files) + "; the patch file is unchanged"))
                continue
            diff = git("diff", "--no-color", "--no-renames", before, "HEAD", cwd=repo).decode()
            body = "".join(line for line in diff.splitlines(keepends=True)
                           if not line.startswith(("diff --git ", "index ", "new file mode", "deleted file mode")))
            text = patch.read_text()
            header = text[:text.index("\n--- a/") + 1] if "\n--- a/" in text else ""
            header = re.sub(r"(Against \S+ )[0-9a-f]{7,40}", lambda m: m.group(1) + new, header)
            new_text = header + body
            if new_text != text:
                patch.write_text(new_text)
                results.append((patch.name, "refreshed"))
            else:
                results.append((patch.name, "unchanged"))
    return results


def sync_component(comp: Component, clones: Clones, new_ref: str | None, repository: str | None, paths: list | None,
                   branch: str | None, group: str | None, stager: Stager) -> SyncReport:
    meta = comp.meta
    old_up = clones.get(meta["repository"])
    old_up.ensure_commit(meta["commit"])
    new_repo = repository or meta["repository"]
    new_branch = branch or meta["branch"]
    new_paths = paths or meta["paths"]
    new_up = clones.get(new_repo)
    new_up.fetch(new_branch)
    if new_ref:
        if re.fullmatch(r"[0-9a-f]{40}", new_ref):
            new_up.ensure_commit(new_ref)
        new = new_up.resolve(new_ref)
    else:
        new = new_up.resolve(f"refs/remotes/origin/{new_branch}")
    old = meta["commit"]
    rep = SyncReport(comp, old, new, meta["repository"])
    old_files = old_up.tree(old, meta["paths"])
    new_files = new_up.tree(new, new_paths)
    vendor = Vendor(comp, index=True)
    # The tool starts from a clean, valid vendor/.
    dirty = git("status", "--porcelain", "-z", "--", comp.vendor_rel, cwd=comp.root)
    if dirty:
        raise ToolError(f"{comp.name}: uncommitted changes under {comp.vendor_rel}; commit or stash them first")
    pre = compare(comp, vendor, old_files, old_up.blobs(site_blobs_needed(vendor, old_files)))
    if pre.errors:
        raise ToolError(f"{comp.name}: `check` fails before the sync; fix that first:\n  " +
                        "\n  ".join(e.splitlines()[0] for e in pre.errors))
    sites = set(pre.sites)
    renames = {}
    if new_repo == meta["repository"]:
        renames = {o: n for s, o, n in old_up.changed(old, new, meta["paths"]) if s == "R"}
    old_need = [old_files[rel][1] for rel in sites]
    new_need = [new_files[rel][1] for rel in sites if rel in new_files]
    new_need += [sha for rel, (mode, sha) in new_files.items() if old_files.get(rel) != (mode, sha)]
    old_blobs = old_up.blobs(old_need)
    new_blobs = new_up.blobs(new_need)
    vr = comp.vendor_rel
    for rel in sorted(set(old_files) | set(new_files)):
        o, n = old_files.get(rel), new_files.get(rel)
        if rel in sites:
            ours = vendor.content(rel)
            mode = vendor.files[rel][0]
            if n is None:
                where = f" (renamed to `{renames[rel]}`)" if rel in renames else ""
                stager.conflict(f"{vr}/{rel}", ours, mode, old_blobs[o[1]], ours, None)
                rep.conflicts.append((rel, f"deleted upstream{where}; move the blocks by hand or drop them"))
                continue
            if o[1] == n[1]:
                rep.sites_unchanged += 1
                if o[0] != n[0]:
                    stager.write(f"{vr}/{rel}", n[0], ours)
                continue
            m = merge_site(rel, ours, old_blobs[o[1]], new_blobs[n[1]], ("localtest", old[:7], new[:7]))
            if m.ok:
                stager.write(f"{vr}/{rel}", n[0], m.merged)
                rep.merged.append(m)
            else:
                stager.conflict(f"{vr}/{rel}", m.merged, n[0], old_blobs[o[1]], ours, new_blobs[n[1]])
                rep.conflicts.append((rel, m.reason))
            continue
        if n is None:
            if rel in vendor.files:
                stager.remove(f"{vr}/{rel}")
                rep.removed.append(rel)
            continue
        if o == n and rel in vendor.files:
            continue  # unchanged upstream (a tolerated line-ending difference of ours stays as it is)
        stager.write(f"{vr}/{rel}", n[0], new_blobs[n[1]])
        (rep.updated if rel in vendor.files else rep.added).append(rel)
    # Files we have that are in neither tree cannot exist after the pre-check, but a path list change can drop some.
    for rel in sorted(set(vendor.files) - set(old_files) - set(new_files)):
        stager.remove(f"{vr}/{rel}")
        rep.removed.append(rel)
    if comp.patches_mode:
        rep.patches = refresh_patches(comp, old_files, new_files, lambda s: old_blobs.get(s) or old_up.blobs([s])[s],
                                      lambda s: new_blobs.get(s) or new_up.blobs([s])[s], new)
    # Mirrors: carried over when the merge is clean, otherwise reported; never staged.
    for mirror in meta.get("mirrors", []):
        u = mirror["upstream"]
        ot, nt = old_up.tree(old, [u]).get(u), new_up.tree(new, [u]).get(u)
        if ot == nt:
            continue
        if nt is None:
            rep.mirrors.append(f"`{u}` is gone upstream: decide what `{mirror['ours']}` mirrors now")
            continue
        if ot is None:
            rep.mirrors.append(f"`{u}` is new upstream: compare it with `{mirror['ours']}`")
            continue
        ob, nb = old_up.blobs([ot[1]])[ot[1]], new_up.blobs([nt[1]])[nt[1]]
        diff = "".join(difflib.unified_diff(ob.decode(errors="replace").splitlines(True),
                                            nb.decode(errors="replace").splitlines(True), f"a/{u}", f"b/{u}", n=1))
        if mirror.get("merge"):
            ours_path = comp.dir / mirror["ours"]
            merged, nconf = merge_text(ours_path.read_bytes(), ob, nb, ("localtest", old[:7], new[:7]))
            if nconf == 0:
                ours_path.write_bytes(merged)
                rep.mirrors.append(f"`{u}` changed; merged into `{mirror['ours']}` (unstaged, review it):\n{diff}")
                continue
            rep.mirrors.append(f"`{u}` changed; the merge into `{mirror['ours']}` conflicts, carry it by hand:\n{diff}")
        else:
            rep.mirrors.append(f"`{u}` changed; carry it into `{mirror['ours']}` by hand:\n{diff}")
    if meta.get("watch"):
        rep.watched = [f"{s} {p}" for s, p, _ in new_up.changed(old, new, meta["watch"])] \
            if new_repo == meta["repository"] else [f"(repository moved) {w}" for w in meta["watch"]]
    rep.commits = new_up.log(old if new_repo == meta["repository"] else None, new, new_paths)
    rep.date = new_up.commit_date(new)
    new_meta = dict(meta, repository=new_repo, branch=new_branch, commit=new, date=rep.date, paths=new_paths)
    if group is not None:
        new_meta["group"] = group or None
    comp.meta = new_meta
    return rep


def summary_text(reports: list, root: Path) -> str:
    first = reports[0]
    names = ", ".join(r.comp.name for r in reports)
    group = first.comp.group
    subject = f"{group if len(reports) > 1 else first.comp.name}: sync vendor to {first.new[:7]} ({first.date})"
    lines = [subject, ""]
    if len(reports) > 1:
        lines.append(f"Components: {names}.")
    moved = first.old_repository != first.comp.meta["repository"]
    lines.append(f"Upstream: {first.comp.meta['repository']} {first.comp.meta['branch']}"
                 + (f" (moved from {first.old_repository})" if moved else ""))
    lines.append(f"Pin: {first.old[:10]} -> {first.new[:10]} ({first.date}); "
                 f"{len(first.commits)} commit(s) touch the copied paths" + (" (the last 20 shown; the base is another"
                                                                          " repository)" if moved else ""))
    for c in first.commits[:20]:
        lines.append(f"  {c}")
    if len(first.commits) > 20:
        lines.append(f"  and {len(first.commits) - 20} more")
    todo_parts = []
    for r in reports:
        lines.append("")
        head = f"{r.comp.name}:" if len(reports) > 1 else "Files:"
        lines.append(f"{head} {len(r.updated)} updated, {len(r.added)} added, {len(r.removed)} removed; "
                     f"site files: {len(r.merged)} merged clean, {len(r.conflicts)} conflicts, "
                     f"{r.sites_unchanged} unchanged upstream")
        for kind, items in (("updated", r.updated), ("added", r.added), ("removed", r.removed)):
            if items and len(items) <= 15:
                lines.extend(f"  {kind}: {p}" for p in items)
        for m in r.merged:
            notes = []
            if m.moved:
                notes.append("blocks moved")
            if m.upstream_changed_replaced:
                notes.append("upstream changed the code replaced at " + ", ".join(m.upstream_changed_replaced) +
                             ": is the site still needed, and is its #else still right?")
            lines.append(f"  merged: {m.path}" + (f" ({'; '.join(notes)})" if notes else ""))
        for path, reason in r.conflicts:
            lines.append(f"  CONFLICT: {path}: {reason}")
        for path, b, reason in r.candidates:
            lines.append(f"  candidate for removal: {path}:{b.start}-{b.end} ({b.kind}): {reason}")
        for name, result in r.patches:
            lines.append(f"  patch {name}: {result}")
        for m in r.mirrors:
            lines.append("  mirror: " + m.rstrip().replace("\n", "\n    "))
        for w in r.watched:
            lines.append(f"  watched path changed (needs a decision): {w}")
        for rem in r.comp.meta.get("reminders", []):
            lines.append(f"  reminder: {rem}")
        todo_parts.append(r.comp.name)
    sites_merged = sum(len(r.merged) for r in reports)
    files = sum(len(r.updated) + len(r.added) + len(r.removed) for r in reports)
    lines += ["", f"TODO note: {', '.join(todo_parts)} synced {first.old[:7]}..{first.new[:7]} ({first.date}, "
                  f"{len(first.commits)} commit(s), {files} files, {sites_merged} sites merged clean, "
                  f"verify-after-sync pending)"]
    return "\n".join(lines) + "\n"


def next_steps(reports: list, root: Path, summary_file: Path) -> str:
    tool = "python3 localtest/tools/vendor-sync.py"
    names = " ".join(r.comp.name for r in reports)
    conflicts = sum(len(r.conflicts) for r in reports)
    groups = manifest_groups(root)
    out = ["Next steps (nothing is committed; the agent finishes the sync, see localtest/docs/vendor-sync.md):"]
    n = 1
    triage = []
    if conflicts:
        triage.append(f"resolve the {conflicts} unmerged site file(s) (`git status`): keep every LOCALTEST block, take "
                      "upstream's new text outside them and in the `#if !LOCALTEST` branches, then `git add` the file")
    if any(result not in ("refreshed", "unchanged") for r in reports for _, result in r.patches):
        triage.append("regenerate the patches that no longer apply (by hand, as small as they are now)")
    if any(r.mirrors for r in reports):
        triage.append("review the mirrors above (merged ones are in the working tree, unstaged)")
    if any(r.watched for r in reports):
        triage.append("decide on the watched paths that changed")
    if any(m.upstream_changed_replaced for r in reports for m in r.merged) or any(r.candidates for r in reports):
        triage.append("re-check the sites upstream touched and the removal candidates")
    if any(r.comp.meta.get("reminders") for r in reports):
        triage.append("work through the reminders")
    triage.append("read the commits above for new settings, renamed types or new platform calls the fakes must follow")
    out.append(f"{n}. Triage: " + "; ".join(triage) + ".")
    n += 1
    out.append(f"{n}. `{tool} render {names}` (the site table, after resolving), then `{tool} check {names}`.")
    n += 1
    builds = []
    for r in reports:
        for p in r.comp.projects():
            conf = p.get("configuration", "Release")
            builds.append(f"   (cd {r.comp.dir_rel} && DirectoryBuildPropsPath=$PWD/localtest.build.props "
                          f"DirectoryBuildTargetsPath=$PWD/localtest.build.targets dotnet build -c {conf} "
                          f"vendor/{p['path']})")
    if builds:
        out.append(f"{n}. Build each host with the overrides (one at a time), fix the fakes and overrides for what "
                   f"broke, then `{tool} check {names}` again: it compares the versions that follow the host:")
        out += builds
        n += 1
    out.append(f"{n}. Commit in two commits: first the staged vendored part alone, `git commit -F {summary_file}` "
               "(vendor/ and UPSTREAM.md; nothing else staged), then our fixes (fakes, overrides, mirrors, "
               "smoke tests, READMEs).")
    n += 1
    flags, levels, smoke, opt_groups = [], ["2.1", "2.5"], [], set()
    for r in reports:
        for image, dockerfile in image_targets(root, r.comp):
            flags.append(f"--dev-{image}" + ("" if dockerfile == "Dockerfile" else f"={dockerfile}"))
            opt_groups.update(g for g in groups.get(image, []) if g not in ("default", "minimal"))
        if (r.comp.dir / "smoke-test.sh").exists():
            smoke.append(f"{r.comp.dir_rel}/smoke-test.sh")
    out.append(f"{n}. Verify (localtest/docs/verify-after-sync.md, under the lock): levels {', '.join(levels)}; 5.1 "
               f"with {' '.join(flags) or 'the component images'} (from the component folder); 4.2 and "
               f"{', '.join(smoke) or 'the root smoke test'}" +
               (f"; 4.3 with --with {' --with '.join(sorted(opt_groups))}" if opt_groups else "") + ".")
    n += 1
    out.append(f"{n}. After the merge: the images workflow builds the new images; then 5.2, the pinned check, "
               "before `bump-tag.sh` moves the pins.")
    return "\n".join(out) + "\n"


def cmd_sync(args) -> int:
    components = load_components(args.root)
    chosen = select(components, args.names, False)
    groups = {c.group for c in chosen if c.group}
    for g in groups:
        chosen += [c for c in components if c.group == g and c not in chosen]
    if (args.repository or args.paths) and len(chosen) != 1:
        raise ToolError("--repository and --paths move one component")
    if args.paths and not args.repository:
        raise ToolError("--paths goes with --repository (to change the path list alone, edit the metadata)")
    to = args.to
    if args.group and not to:
        members = [c for c in components if c.group == args.group and c not in chosen]
        if members:
            to = members[0].meta["commit"]
    clones = Clones(args.clones)
    stager = Stager(args.root)
    reports = []
    for comp in chosen:
        reports.append(sync_component(comp, clones, to, args.repository, args.paths, args.branch, args.group,
                                      stager))
        to = reports[-1].new  # a group moves to one commit
    stager.flush()
    for r in reports:
        vendor = Vendor(r.comp)
        sites = {}
        for rel in vendor.files:
            data = vendor.content(rel)
            if has_directives(data) and not conflict_marker_line(data):
                try:
                    sites[rel] = parse(data)
                    r.candidates += [(rel, b, b.removal_candidate()) for b in sites[rel].blocks
                                     if b.removal_candidate()]
                except DirectiveError:
                    pass
        r.comp.save(meta=r.comp.meta, sites=sites)
        git("add", "--", f"{r.comp.vendor_rel}/UPSTREAM.md", cwd=args.root)
    summary = summary_text(reports, args.root)
    summary_file = args.summary or Path(git("rev-parse", "--git-path", "vendor-sync-summary.txt",
                                            cwd=args.root).decode().strip())
    if not summary_file.is_absolute():
        summary_file = args.root / summary_file
    summary_file.write_text(summary)
    print(summary)
    print(f"(summary written to {summary_file})\n")
    print(next_steps(reports, args.root, summary_file))
    failed_patches = any(result not in ("refreshed", "unchanged") for r in reports for _, result in r.patches)
    return 2 if any(r.conflicts for r in reports) or failed_patches else 0


# --------------------------------------------------------------------------------------------------------------------


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0],
                                     formatter_class=argparse.RawDescriptionHelpFormatter, epilog=__doc__)
    parser.add_argument("--clones", type=Path, default=default_clones(), help="folder of the blobless upstream clones")
    parser.add_argument("--root", type=Path, default=ROOT, help=argparse.SUPPRESS)
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("status", "check", "render"):
        p = sub.add_parser(name)
        p.add_argument("names", nargs="*")
        p.add_argument("--all", action="store_true")
        if name == "status":
            p.add_argument("--json", action="store_true")
            p.add_argument("--no-fetch", dest="fetch", action="store_false")
        if name == "check":
            p.add_argument("--index", action="store_true", help="check the index instead of the working tree")
    p = sub.add_parser("sync")
    p.add_argument("names", nargs=1)
    p.add_argument("--to", help="upstream commit or ref (default: the head of the branch)")
    p.add_argument("--repository", help="move to another upstream repository (OWNER/NAME)")
    p.add_argument("--paths", nargs="+", help="the copied paths in the new repository (with --repository)")
    p.add_argument("--branch", help="follow another branch")
    p.add_argument("--group", help="join a group (empty string: leave it); --to defaults to the group's commit")
    p.add_argument("--summary", type=Path, help="write the summary here (default: .git/vendor-sync-summary.txt)")
    args = parser.parse_args(argv)
    args.root = args.root.resolve()
    try:
        return {"status": cmd_status, "check": cmd_check, "render": cmd_render, "sync": cmd_sync}[args.command](args)
    except ToolError as e:
        print(f"vendor-sync: {e}", file=sys.stderr)
        return 3


if __name__ == "__main__":
    sys.exit(main())
