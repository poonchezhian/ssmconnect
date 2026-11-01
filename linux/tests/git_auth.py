"""Offline regression checks for wiki authentication; Git and credentials are synthetic."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
BASH = shutil.which("bash") or r"C:\Program Files\Git\bin\bash.exe"


def shell_path(path):
    text = str(path).replace("\\", "/")
    return "/" + text[0].lower() + text[2:] if len(text) > 1 and text[1] == ":" else text


FAKE = r'''
import json, os, pathlib, subprocess, sys
a = sys.argv[1:]
assert os.environ.get('GH_TOKEN', '') not in a if os.environ.get('GH_TOKEN') else True
assert not any('synthetic-secret' in x or 'x-access-token:' in x for x in a)
if 'rev-parse' in a:
    print(os.environ['TEST_ROOT']); sys.exit(0)
with open(os.environ['TEST_LOG'], 'a') as f: f.write(json.dumps(a) + '\n')
if 'clone' in a or 'push' in a:
    helper = os.environ.get('GIT_ASKPASS')
    if os.environ.get('GH_TOKEN'):
        assert helper and 'synthetic-secret' not in pathlib.Path(helper).read_text()
        assert subprocess.check_output([os.environ['TEST_BASH'], helper, 'Username for https://github.com'], text=True).strip() == 'x-access-token'
        assert subprocess.check_output([os.environ['TEST_BASH'], helper, 'Password for https://github.com'], text=True).strip() == os.environ['GH_TOKEN']
    else:
        assert not helper
    with open(os.environ['TEST_HELPER_LOG'], 'a') as f: f.write((helper or '') + '\n')
    if os.environ.get('TEST_FAIL') in a: sys.exit(7)
if 'clone' in a:
    pathlib.Path(a[-1]).mkdir()
    (pathlib.Path(a[-1]) / 'origin.txt').write_text(a[-2])
if 'diff' in a: sys.exit(1)
if 'config' in a: print('test-user')
'''


with tempfile.TemporaryDirectory(prefix="ssc-git-auth-") as directory:
    base = Path(directory)
    repo = base / "source"
    (repo / "wiki").mkdir(parents=True)
    (repo / "wiki" / "Home.md").write_text("Test wiki\n")
    tools = base / "bin"
    tools.mkdir()
    fake_git = tools / "git"
    fake_git.write_text("#!" + shell_path(sys.executable) + "\n" + FAKE, encoding="utf-8")
    fake_git.chmod(0o755)
    for token, failure in [("synthetic-secret", "none"), ("synthetic-secret", "clone"), ("synthetic-secret", "push"), ("", "none")]:
        log = base / "calls.jsonl"
        helper_log = base / "helpers.txt"
        log.write_text("")
        helper_log.write_text("")
        env = os.environ.copy()
        for key in list(env):
            if key.startswith("GIT_CONFIG_") or key in ("GIT_ASKPASS", "SSH_ASKPASS", "GH_TOKEN"):
                env.pop(key)
        env.update(PATH=str(tools) + os.pathsep + env["PATH"], TEST_ROOT=shell_path(repo), TEST_LOG=str(log),
                   TEST_HELPER_LOG=str(helper_log), TEST_BASH=BASH, TEST_FAIL=failure, GITHUB_REPOSITORY="test/project")
        if token:
            env["GH_TOKEN"] = token
        env.update(TEST_TOOLS=shell_path(tools), TEST_SCRIPT=shell_path(ROOT / "linux/scripts/sync-wiki.sh"))
        result = subprocess.run([BASH, "-c", 'export PATH="$TEST_TOOLS:$PATH"; exec bash "$TEST_SCRIPT"'], env=env, capture_output=True, text=True)
        assert (result.returncode == 0) == (failure == "none"), result.stderr
        calls = [json.loads(line) for line in log.read_text().splitlines()]
        clone = next(a for a in calls if "clone" in a)
        assert clone[-2] == "https://github.com/test/project.wiki.git"
        assert not Path(clone[-1]).exists(), "temporary checkout must be removed on every exit"
        for helper in helper_log.read_text().splitlines():
            if helper:
                assert not Path(helper).exists(), "askpass helper must be removed on every exit"
        assert "synthetic-secret" not in result.stdout + result.stderr + log.read_text()
    print("PASS: clean Git arguments, private askpass, normal credentials, clone/push failures and cleanup")
