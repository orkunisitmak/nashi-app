import importlib.util
import os
import pathlib
import shutil
import subprocess
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("scan_secrets", pathlib.Path(__file__).with_name("scan-secrets.py"))
scan = importlib.util.module_from_spec(spec)
spec.loader.exec_module(scan)
# An obviously fake value that gitleaks' default generic-api-key rule matches. It is joined at
# run time so this file never holds the assignment itself and the repository's own scan stays clean.
FAKE = "".join(["q8Vz3", "Lm9Rt", "2Xw7K", "p4Nc6", "Bj1Hd", "5Fg0Y"])
LINE = "fake_api_token = " + repr(FAKE)
GIT = {**os.environ, "GIT_CONFIG_GLOBAL": os.devnull, "GIT_CONFIG_NOSYSTEM": "1"}


def gitleaks():
    """The pinned 8.30.1 binary: $GITLEAKS, the one scan-secrets.py downloaded, or one on PATH."""
    for found in [os.environ.get("GITLEAKS"), scan.root / "build/security/gitleaks", shutil.which("gitleaks")]:
        if found and pathlib.Path(found).is_file():
            out = subprocess.run([str(found), "version"], capture_output=True, text=True).stdout
            if "8.30.1" in out:
                return found


def git(repo, *args):
    return subprocess.run(["git", "-C", str(repo), "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid",
                           "-c", "commit.gpgsign=false", *args], check=True, capture_output=True, text=True, env=GIT).stdout


@unittest.skipUnless(gitleaks(), "gitleaks 8.30.1 not found; run scripts/scan-secrets.py once or set GITLEAKS")
class ScannedTreeCannotSilenceTheScan(unittest.TestCase):
    def fixture(self, files, ignore=False):
        repo = pathlib.Path(self.enterContext(tempfile.TemporaryDirectory()))
        git(repo, "init", "-q")
        for name, text in files.items():
            (repo / name).write_text(text)
        git(repo, "add", "-A")
        git(repo, "commit", "-qm", "fixture")
        if ignore:
            commit = git(repo, "rev-parse", "HEAD").strip()
            (repo / ".gitleaksignore").write_text(f"{commit}:secret.env:generic-api-key:1\n")
            git(repo, "add", "-A")
            git(repo, "commit", "-qm", "ignore")
        return repo

    def assertFound(self, repo):
        self.assertEqual(scan.scan(gitleaks(), repo), 1)

    def test_plain_secret_is_found(self):
        self.assertFound(self.fixture({"secret.env": LINE + "\n"}))

    def test_committed_config_allowlist_is_ignored(self):
        self.assertFound(self.fixture({"secret.env": LINE + "\n",
                                       ".gitleaks.toml": "[extend]\nuseDefault = true\n[allowlist]\npaths = ['''secret\\.env''']\n"}))

    def test_committed_gitleaksignore_refuses_the_scan(self):
        with self.assertRaisesRegex(SystemExit, "Refusing to scan"):
            scan.scan(gitleaks(), self.fixture({"secret.env": LINE + "\n"}, ignore=True))

    def test_inline_allow_comment_is_ignored(self):
        self.assertFound(self.fixture({"secret.env": LINE + "  # gitleaks:allow\n"}))

    def test_committed_gitattributes_cannot_hide_contents(self):
        for attribute in ["binary", "-diff"]:
            with self.subTest(attribute=attribute):
                self.assertFound(self.fixture({"secret.env": LINE + "\n", ".gitattributes": f"secret.env {attribute}\n"}))


if __name__ == "__main__":
    unittest.main()
