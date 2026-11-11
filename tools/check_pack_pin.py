#!/usr/bin/env python3
"""Fail if any jar the friends pack serves differs from meta/norcom-mod-pin.json.

Why: launcher PinGate blocks Play when a pack jar's sha256 differs from the
published pin, and its heal fetches by filename from main (mods/<name>) and the
packwiz download URL. If the pack serves a different build than the pin (and
the server), heal can never converge and every friend is blocked (Nov 10, 2026:
norcom-marble 47fe426d served vs f99e1504 pinned).

Checks, all by sha256:
  1. every mods/*.pw.toml download: verify its own declared hash, then compare
     its sha256 to the pin entry with the same filename;
  2. every committed mods/*.jar that is also in the pin (the heal source);
  3. meta/server-compat.json "both" map and its "pin" agree with the pin file;
  4. --server-mods DIR (owner PC only): every pin jar matches the live server.
Pin jars the pack does not serve are fine (server-only). Pack jars missing from
the pin are reported but only fail with --strict (client-only mods).
Usage: python3 tools/check_pack_pin.py [--offline] [--server-mods DIR] [--strict]
"""
import argparse, hashlib, json, os, re, sys, urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def toml_kv(path):
    out, section = {}, ""
    for line in open(path, encoding="utf-8"):
        line = line.strip()
        m = re.match(r"^\[([^\]]+)\]$", line)
        if m:
            section = m.group(1) + "."
            continue
        m = re.match(r'^([A-Za-z0-9_-]+)\s*=\s*"(.*)"$', line)
        if m:
            out[section + m.group(1)] = m.group(2)
    return out


def fetch(url):
    req = urllib.request.Request(url, headers={"User-Agent": "norcom-pack-pin-check"})
    with urllib.request.urlopen(req, timeout=120) as r:
        return r.read()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--offline", action="store_true", help="skip downloading pw.toml URLs")
    ap.add_argument("--server-mods", help="live server mods dir to compare with the pin")
    ap.add_argument("--strict", action="store_true", help="also fail on pack jars missing from the pin")
    a = ap.parse_args()

    pin = json.load(open(os.path.join(ROOT, "meta", "norcom-mod-pin.json"), encoding="utf-8"))
    want = {j["name"].lower(): j["sha256"].lower() for j in pin["jars"]}
    errors, notes, checked = [], [], 0

    if pin.get("jar_count") not in (None, len(pin["jars"])):
        errors.append(f"pin jar_count {pin.get('jar_count')} != {len(pin['jars'])} jars listed")

    mods = os.path.join(ROOT, "mods")
    for f in sorted(os.listdir(mods)):
        p = os.path.join(mods, f)
        if f.endswith(".pw.toml"):
            kv = toml_kv(p)
            name = kv.get("filename", "")
            side = kv.get("side", "both")
            url = kv.get("download.url", "")
            if name.lower() not in want:
                (errors if a.strict and side != "client" else notes).append(f"{name}: served ({side}) but not in pin")
                continue
            if a.offline or not url:
                continue
            body = fetch(url)
            fmt = kv.get("download.hash-format", "sha256")
            if hashlib.new(fmt, body).hexdigest() != kv.get("download.hash", "").lower():
                errors.append(f"{name}: download does not match its own {fmt} in {f}")
            got = hashlib.sha256(body).hexdigest()
            checked += 1
            if got != want[name.lower()]:
                errors.append(f"{name}: pack serves sha256 {got} but pin wants {want[name.lower()]} ({url})")
        elif f.endswith(".jar") and f.lower() in want:
            got = hashlib.sha256(open(p, "rb").read()).hexdigest()
            checked += 1
            if got != want[f.lower()]:
                errors.append(f"mods/{f} (heal source) sha256 {got} but pin wants {want[f.lower()]}")

    compat_path = os.path.join(ROOT, "meta", "server-compat.json")
    if os.path.exists(compat_path):
        compat = json.load(open(compat_path, encoding="utf-8"))
        if compat.get("pin") and compat["pin"].lower() != pin.get("pin_sha256", "").lower():
            errors.append(f"server-compat pin {compat['pin']} != pin_sha256 {pin.get('pin_sha256')}")
        for jar, h in (compat.get("both") or {}).items():
            n = os.path.basename(jar).lower()
            if isinstance(h, str) and n in want and h.lower() != want[n]:
                errors.append(f"server-compat both {n} {h} != pin {want[n]}")

    if a.server_mods:
        live = {f.lower(): os.path.join(a.server_mods, f) for f in os.listdir(a.server_mods) if f.lower().endswith(".jar")}
        for n, h in want.items():
            if n not in live:
                errors.append(f"pin jar {n} missing from server mods")
                continue
            got = hashlib.sha256(open(live[n], "rb").read()).hexdigest()
            if got != h:
                errors.append(f"server {n} sha256 {got} != pin {h}")
        for n in sorted(set(live) - set(want)):
            notes.append(f"server-only jar not in pin: {n}")

    for n in notes:
        print("note:", n)
    for e in errors:
        print("ERROR:", e)
    print(f"checked {checked} served jars against pin {pin.get('pin_sha256','')[:12]}; {len(errors)} error(s)")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
