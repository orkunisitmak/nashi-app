#!/usr/bin/env python3
"""Validate app registration and real launcher argument boundaries in private fixtures."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time

root = Path(__file__).resolve().parents[1]
with tempfile.TemporaryDirectory(prefix="nashi-install-") as temporary:
    base = Path(temporary)
    for index, name in enumerate(["portable folder", 'space $ dollar ` tick % percent " quote \\ slash']):
        app = base / name
        app.mkdir()
        shutil.copy2(root / "linux/packaging/install.sh", app / "install.sh")
        (app / "nashi.png").write_bytes(b"fixture")
        (app / "Nashi").write_text(
            '#!/usr/bin/env python3\nimport json,os,sys\n'
            'with open(os.environ["NASHI_INSTALL_TEST_LOG"],"w") as f: json.dump(sys.argv,f)\n', encoding="utf-8")
        (app / "Nashi").chmod(0o755)
        log = base / f"arguments-{index}.json"
        env = dict(os.environ, XDG_DATA_HOME=str(base / "xdg"), NASHI_INSTALL_TEST_LOG=str(log))
        subprocess.run(["bash", str(app / "install.sh")], check=True, env=env, capture_output=True)
        desktop = base / "xdg/applications/io.github.orkunisitmak.Nashi.desktop"
        subprocess.run(["desktop-file-validate", str(desktop)], check=True)
        fixture = base / "file ; $(touch sentinel).png"
        fixture.write_bytes(b"fixture")
        launched = subprocess.run(["gio", "launch", str(desktop), str(fixture)], env=env, capture_output=True, text=True)
        if launched.returncode: raise RuntimeError(str(index) + " " + desktop.read_text() + launched.stderr)
        deadline = time.monotonic() + 5
        while not log.exists() and time.monotonic() < deadline:
            time.sleep(.05)
        assert json.loads(log.read_text()) == [str(app / "Nashi"), str(fixture)]
        assert not (base / "sentinel").exists()
        desktop.unlink()
    print("PASS desktop registration: spaces, reserved characters and literal file arguments")
