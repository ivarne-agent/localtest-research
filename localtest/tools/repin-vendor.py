#!/usr/bin/env python3
"""Re-pin a vendored upstream copy to a newer commit, keeping local (LOCALTEST directive) changes.

Usage: repin-vendor.py <upstream-git-dir> <old-commit> <new-commit> <vendor-dir> <path> [<path> ...]

<path>s are upstream paths (directories or files) that were copied into <vendor-dir> with their relative layout.
For every file under them:
  - unchanged locally (vendor == old): take the new upstream version (or delete it if upstream deleted it);
  - changed locally and upstream unchanged: keep the local file;
  - changed on both sides: three-way merge with `git merge-file` (local, old, new); conflicts are left marked and
    reported, and the script exits 1;
  - new upstream: added.
bin/ and obj/ are ignored. Prints a summary. Nothing is committed.
"""
import os
import shutil
import subprocess
import sys
import tempfile


def archive(repo, commit, paths, dest):
    existing = [p for p in paths if subprocess.run(["git", "-C", repo, "cat-file", "-e", f"{commit}:{p}"], capture_output=True).returncode == 0]
    if existing:
        tar = subprocess.run(["git", "-C", repo, "archive", commit, "--", *existing], check=True, capture_output=True).stdout
        subprocess.run(["tar", "-x", "-C", dest], input=tar, check=True)
    return {
        os.path.relpath(os.path.join(root, f), dest)
        for root, _, files in os.walk(dest)
        for f in files
    }


def under(paths, rel):
    return any(rel == p.rstrip("/") or rel.startswith(p.rstrip("/") + "/") for p in paths)


def same(a, b):
    with open(a, "rb") as fa, open(b, "rb") as fb:
        return fa.read() == fb.read()


def main():
    repo, old, new, vendor, *paths = sys.argv[1:]
    with tempfile.TemporaryDirectory() as tmp:
        old_dir, new_dir = os.path.join(tmp, "old"), os.path.join(tmp, "new")
        os.makedirs(old_dir)
        os.makedirs(new_dir)
        old_files = archive(repo, old, paths, old_dir)
        new_files = archive(repo, new, paths, new_dir)
        local_files = {
            os.path.relpath(os.path.join(root, f), vendor)
            for root, dirs, files in os.walk(vendor)
            for f in files
            if not {"bin", "obj"} & set(os.path.relpath(root, vendor).split(os.sep))
        }
        local_files = {f for f in local_files if under(paths, f)}

        updated, added, deleted, merged, kept, conflicts, notes = [], [], [], [], [], [], []
        for rel in sorted(old_files | new_files | local_files):
            o, n, v = os.path.join(old_dir, rel), os.path.join(new_dir, rel), os.path.join(vendor, rel)
            in_old, in_new, in_local = rel in old_files, rel in new_files, rel in local_files
            if in_local and not in_old and not in_new:
                notes.append(f"local-only file kept: {rel}")
                continue
            if not in_local and in_old:
                notes.append(f"missing locally (was upstream at the old pin), left missing: {rel}")
                continue
            if not in_old and in_new:
                os.makedirs(os.path.dirname(v), exist_ok=True)
                shutil.copyfile(n, v)
                added.append(rel)
                continue
            locally_changed = not same(v, o)
            if in_old and not in_new:
                if locally_changed:
                    conflicts.append(f"deleted upstream but changed locally: {rel}")
                else:
                    os.remove(v)
                    deleted.append(rel)
                continue
            if same(o, n):
                if locally_changed:
                    kept.append(rel)
                continue
            if not locally_changed:
                shutil.copyfile(n, v)
                updated.append(rel)
                continue
            result = subprocess.run(["git", "merge-file", "-L", "localtest", "-L", "old", "-L", "new", v, o, n])
            (conflicts if result.returncode != 0 else merged).append(rel)

    for title, items in [("updated", updated), ("added", added), ("deleted", deleted),
                         ("merged (local changes kept)", merged), ("local changes, upstream unchanged", kept),
                         ("CONFLICTS", conflicts), ("notes", notes)]:
        print(f"{title}: {len(items)}")
        if title in ("merged (local changes kept)", "local changes, upstream unchanged", "CONFLICTS", "notes", "deleted", "added") and len(items) <= 40:
            for item in items:
                print(f"  {item}")
    sys.exit(1 if conflicts else 0)


if __name__ == "__main__":
    main()
