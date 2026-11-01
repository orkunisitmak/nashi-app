#!/usr/bin/env python3
"""Real X11 XDND smoke: serve a disposable PDF and drop on the native wheel's JPG outer petal.

Run after building linux/Nashi.Linux.Checks in Release; use xvfb-run in CI.
No global pointer/keyboard input is synthesized. An existing user drag aborts the test.
"""

import ctypes as c
import hashlib
import math
import os
import selectors
import shutil
import subprocess
import sys
import tempfile
import threading
import time
from pathlib import Path

x = c.CDLL("libX11.so.6")
x.XInitThreads()


def api(name, result, args):
    f = getattr(x, name)
    f.restype = result
    f.argtypes = args
    return f


P = c.c_void_p
U = c.c_ulong
I = c.c_int
L = c.c_long
open_d = api("XOpenDisplay", P, [P])
close = api("XCloseDisplay", I, [P])
root = api("XDefaultRootWindow", U, [P])
atom = api("XInternAtom", U, [P, c.c_char_p, I])
create = api("XCreateSimpleWindow", U, [P, U, I, I, c.c_uint, c.c_uint, c.c_uint, U, U])
destroy = api("XDestroyWindow", I, [P, U])
owner = api("XGetSelectionOwner", U, [P, U])
setowner = api("XSetSelectionOwner", I, [P, U, U, U])
flush = api("XFlush", I, [P])
pending = api("XPending", I, [P])
next_event = api("XNextEvent", I, [P, P])
send = api("XSendEvent", I, [P, U, I, L, P])
prop = api("XChangeProperty", I, [P, U, U, U, I, I, P, I])
getprop = api("XGetWindowProperty", I, [P, U, U, L, L, I, U, P, P, P, P, P])
free = api("XFree", I, [P])
d = open_d(None)
assert d
rt = root(d)
A = lambda s: atom(d, s.encode(), 0)
selection = A("XdndSelection")
assert owner(d, selection) == 0, "A user drag is active; aborting without changing it"
work = Path(tempfile.mkdtemp(prefix="nashi-native-drop-"))
# A one-page vector PDF, built with the standard library so the native test needs no Python packages.
content = b"1 0.7 0 rg 0 0 100 70 re f\n"
objects = [
    b"<< /Type /Catalog /Pages 2 0 R >>",
    b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
    b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 70] /Resources << >> /Contents 4 0 R >>",
    b"<< /Length "
    + str(len(content)).encode()
    + b" >>\nstream\n"
    + content
    + b"endstream",
]
pdf = bytearray(b"%PDF-1.4\n")
offsets = [0]
for i, obj in enumerate(objects, 1):
    offsets.append(len(pdf))
    pdf.extend(f"{i} 0 obj\n".encode() + obj + b"\nendobj\n")
xref = len(pdf)
pdf.extend(b"xref\n0 5\n0000000000 65535 f \n")
for offset in offsets[1:]:
    pdf.extend(f"{offset:010d} 00000 n \n".encode())
pdf.extend(f"trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n".encode())
(work / "source.pdf").write_bytes(pdf)
original_hash = hashlib.sha256(pdf).digest()
source = create(d, rt, -100, -100, 1, 1, 0, 0, 0)
setowner(d, selection, source, 0)
flush(d)
uri = (work / "source.pdf").as_uri().encode() + b"\r\n"
stop = threading.Event()
counts = {}


def event(typ, window, kind, values):
    e = (L * 24)()
    e[0] = typ
    e[2] = 1
    e[3] = d
    e[4] = window
    e[5] = kind
    e[6] = 32
    for k, v in enumerate(values):
        e[7 + k] = v
    return e


def serve():
    while not stop.is_set():
        if pending(d):
            e = (L * 24)()
            next_event(d, e)
            if (e[0] & 0xFFFFFFFF) == 30:
                requestor, sel, target, property_, stamp = e[5], e[6], e[7], e[8], e[9]
                ok = target in (A("text/uri-list"), A("text/plain"))
                if ok:
                    payload = c.create_string_buffer(uri)
                    prop(d, requestor, property_, target, 8, 0, payload, len(uri))
                reply = (L * 24)()
                reply[0] = 31
                reply[2] = 1
                reply[3] = d
                reply[4] = requestor
                reply[5] = sel
                reply[6] = target
                reply[7] = property_ if ok else 0
                reply[8] = stamp
                send(d, requestor, 0, 0, reply)
                flush(d)
                counts["selection_replies"] = counts.get("selection_replies", 0) + 1
            elif (e[0] & 0xFFFFFFFF) == 33:
                counts["client_messages"] = counts.get("client_messages", 0) + 1
                counts.setdefault("status_flags", []).append((e[5], e[8]))
        else:
            time.sleep(0.002)


thread = threading.Thread(target=serve)
thread.start()
env = os.environ.copy()
env["XDG_DATA_HOME"] = str(work / "data")
env["NASHI_LANGUAGE"] = "en"
proc = None
try:
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    project = Path(__file__).resolve().parent.parent / "linux/Nashi.Linux.Checks"
    target = project / "bin/Release/net10.0/linux-x64/Nashi.Linux.Checks.dll"
    assert target.is_file(), "Build linux/Nashi.Linux.Checks -c Release first"
    proc = subprocess.Popen(
        [dotnet, str(target), "--native-drop-target", str(work)],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        env=env,
    )
    while True:
        line = proc.stdout.readline().strip()
        if not line:
            raise RuntimeError(proc.stderr.read())
        if line.startswith("WHEEL|"):
            break
    vals = line.split("|")
    win, ox, oy, width, height = map(int, vals[1:6])
    scale = float(vals[6])
    print("Native X11 wheel ready", flush=True)
    actual = U()
    fmt = I()
    n = U()
    after = U()
    data = P()
    getprop(
        d,
        win,
        A("XdndProxy"),
        0,
        1,
        0,
        0,
        c.byref(actual),
        c.byref(fmt),
        c.byref(n),
        c.byref(after),
        c.byref(data),
    )
    proxy = c.cast(data, c.POINTER(U))[0] if data and n.value else 0
    if data:
        free(data)
    dest = proxy
    assert dest, "No XDND proxy was installed"
    send(
        d,
        dest,
        0,
        0,
        event(33, win, A("XdndEnter"), [source, 5 << 24, A("text/uri-list"), 0, 0]),
    )
    flush(d)

    def position(local_x, local_y):
        packed = ((ox + round(local_x * scale)) << 16) | (oy + round(local_y * scale))
        send(
            d,
            dest,
            0,
            0,
            event(
                33, win, A("XdndPosition"), [source, 0, packed, 0, A("XdndActionCopy")]
            ),
        )
        flush(d)

    count, images, jpg, child_count = map(int, vals[7:11])
    parent_mid = (180 if count == 3 else 90) - images * 360 / count
    parent_angle = math.radians(parent_mid)
    position(
        width / scale / 2 + 100 * math.cos(parent_angle),
        height / scale / 2 - 100 * math.sin(parent_angle),
    )
    # Wait for the real UI to open Images, rather than racing first-render/JIT delays with a fixed sleep.
    selector = selectors.DefaultSelector()
    selector.register(proc.stdout, selectors.EVENT_READ)
    deadline = time.monotonic() + 5
    while time.monotonic() < deadline:
        if selector.select(0.1):
            line = proc.stdout.readline().strip()
            if line == f"GROUP|{images}":
                break
    else:
        raise AssertionError("The real X11 hover never opened the Images outer ring")
    selector.close()
    inner, outer, span = map(float, vals[11:14])
    r = (inner + outer) / 2
    angle = math.radians(parent_mid - (jpg - (child_count - 1) / 2) * span)
    position(
        width / scale / 2 + r * math.cos(angle),
        height / scale / 2 - r * math.sin(angle),
    )
    time.sleep(0.15)
    send(d, dest, 0, 0, event(33, win, A("XdndDrop"), [source, 0, 0, 0, 0]))
    flush(d)
    stdout, stderr = proc.communicate(timeout=12)
    print(stdout.strip())
    print("Outputs:", [p.name for p in work.glob("*.jpg")])
    print("X11 events:", counts)
    if stderr.strip():
        print("App errors:", stderr[-1600:])
    output = work / "source Page 1.jpg"
    assert proc.returncode == 0 and output.is_file(), (
        "Native drop did not create JPG beside the source PDF"
    )
    assert output.read_bytes().startswith(b"\xff\xd8\xff"), "Output is not a JPEG"
    assert (
        hashlib.sha256((work / "source.pdf").read_bytes()).digest() == original_hash
    ), "Source PDF changed"
    assert any(flags == 1 for _, flags in counts.get("status_flags", [])), (
        "The source never received a successful XdndFinished"
    )
    print("PASS: native hover, JPG drop, source-adjacent export, original preserved")
    print("Fixture directory:", work)
    sys.exit(proc.returncode)
finally:
    if proc and proc.poll() is None:
        proc.terminate()
        proc.wait(timeout=5)
    stop.set()
    thread.join()
    destroy(d, source)
    close(d)
