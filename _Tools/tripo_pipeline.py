"""
Tripo image-to-3D orchestration. Standard library only.

Uploads the concept references, runs a multiview reconstruction, optionally auto-rigs
the result, and downloads the model next to the concept art.

  set TRIPO_API_KEY=tsk_...
  python _Tools/tripo_pipeline.py --views _Concept/Ash --out _Concept/Ash/generated

The key is read from the environment and never echoed or written to disk.
"""

import argparse
import json
import mimetypes
import os
import sys
import time
import urllib.error
import urllib.request
import uuid
from pathlib import Path

API = "https://api.tripo3d.ai/v2/openapi"
# Multiview slots are fixed in this order; an omitted slot is an empty object.
SLOTS = ("front", "left", "back", "right")


def _request(url, key, data=None, headers=None, method=None):
    hdrs = {"Authorization": f"Bearer {key}"}
    if headers:
        hdrs.update(headers)
    req = urllib.request.Request(url, data=data, headers=hdrs, method=method)
    try:
        with urllib.request.urlopen(req, timeout=180) as r:
            return json.loads(r.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", "replace")
        raise SystemExit(f"HTTP {e.code} from {url}\n{body}")


def _multipart(field_name, path):
    """Hand-rolled multipart body so we stay dependency free."""
    boundary = f"----tripo{uuid.uuid4().hex}"
    mime = mimetypes.guess_type(path.name)[0] or "application/octet-stream"
    pre = (
        f"--{boundary}\r\n"
        f'Content-Disposition: form-data; name="{field_name}"; filename="{path.name}"\r\n'
        f"Content-Type: {mime}\r\n\r\n"
    ).encode()
    post = f"\r\n--{boundary}--\r\n".encode()
    return pre + path.read_bytes() + post, f"multipart/form-data; boundary={boundary}"


def upload(key, path):
    body, content_type = _multipart("file", path)
    res = _request(f"{API}/upload/sts", key, data=body, headers={"Content-Type": content_type})
    token = (res.get("data") or {}).get("image_token")
    if not token:
        raise SystemExit(f"upload of {path.name} returned no image_token: {res}")
    print(f"  uploaded {path.name} -> token ...{token[-8:]}")
    return token


def submit(key, payload):
    res = _request(
        f"{API}/task", key,
        data=json.dumps(payload).encode(),
        headers={"Content-Type": "application/json"},
    )
    task_id = (res.get("data") or {}).get("task_id")
    if not task_id:
        raise SystemExit(f"task submit failed: {res}")
    return task_id


def wait(key, task_id, label):
    print(f"  {label}: task {task_id}")
    last = None
    while True:
        data = _request(f"{API}/task/{task_id}", key).get("data") or {}
        status = data.get("status")
        progress = data.get("progress")
        if (status, progress) != last:
            print(f"    {status} {progress if progress is not None else ''}".rstrip())
            last = (status, progress)
        if status == "success":
            return data.get("output") or {}
        if status in ("failed", "cancelled", "banned", "expired", "unknown"):
            raise SystemExit(f"{label} ended as '{status}': {json.dumps(data)[:600]}")
        time.sleep(3)


def download(url, dest):
    dest.parent.mkdir(parents=True, exist_ok=True)
    # Signed result URLs expire in about five minutes, so fetch immediately.
    with urllib.request.urlopen(url, timeout=300) as r, open(dest, "wb") as f:
        f.write(r.read())
    print(f"  saved {dest.name}  ({dest.stat().st_size / 1024:.0f} KB)")
    return dest


def check(key):
    """Validates the key and reports credits so nothing is spent by surprise."""
    res = _request(f"{API}/user/balance", key)
    data = res.get("data") or {}
    print("key accepted")
    for label, field in (("balance", "balance"), ("frozen", "frozen")):
        if field in data:
            print(f"  {label}: {data[field]}")
    if not data:
        print(f"  unexpected balance payload: {json.dumps(res)[:300]}")
    return data


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="validate the key and print credits, then exit")
    ap.add_argument("--views", help="folder holding *_front/_side|_right/_back images")
    ap.add_argument("--out")
    ap.add_argument("--face-limit", type=int, default=6000)
    ap.add_argument("--model-version", default="P1-20260311")
    ap.add_argument("--no-rig", action="store_true", help="skip the auto-rig step")
    args = ap.parse_args()

    key = os.environ.get("TRIPO_API_KEY", "").strip()
    if not key:
        raise SystemExit("TRIPO_API_KEY is not set in the environment.")

    if args.check:
        check(key)
        return
    if not args.views or not args.out:
        raise SystemExit("--views and --out are required unless --check is used")

    views = Path(args.views)
    out = Path(args.out)

    # Our side reference shows the character's right side, so it fills the 'right' slot.
    wanted = {"front": ("front",), "back": ("back", "rear"), "right": ("right", "side")}
    found = {}
    for slot, hints in wanted.items():
        for img in sorted(views.glob("*.png")) + sorted(views.glob("*.jpg")):
            stem = img.stem.lower()
            if any(h in stem for h in hints) and slot not in found:
                found[slot] = img
    if "front" not in found:
        raise SystemExit(f"no front reference found in {views}")
    print("references:", {k: v.name for k, v in found.items()})

    print("1/4 uploading")
    tokens = {slot: upload(key, path) for slot, path in found.items()}

    files = []
    for slot in SLOTS:
        if slot in tokens:
            ext = found[slot].suffix.lstrip(".").lower()
            files.append({"type": "jpeg" if ext in ("jpg", "jpeg") else ext,
                          "file_token": tokens[slot]})
        else:
            files.append({})

    print("2/4 reconstructing")
    payload = {
        "type": "multiview_to_model",
        "model_version": args.model_version,
        "files": files,
        "face_limit": args.face_limit,
        "texture": True,
        "pbr": True,
        "texture_quality": "detailed",
        "texture_alignment": "original_image",
        "orientation": "align_image",
        "auto_size": True,
    }
    model_task = submit(key, payload)
    output = wait(key, model_task, "reconstruct")

    out.mkdir(parents=True, exist_ok=True)
    raw = None
    for field, name in (("pbr_model", "ash_raw_pbr.glb"), ("model", "ash_raw.glb")):
        url = output.get(field)
        if isinstance(url, dict):
            url = url.get("url")
        if url:
            raw = download(url, out / name)
            break
    if raw is None:
        raise SystemExit(f"no model URL in output: {json.dumps(output)[:600]}")

    rigged = None
    if not args.no_rig:
        print("3/4 auto-rigging")
        try:
            rig_task = submit(key, {
                "type": "animate_rig",
                "original_model_task_id": model_task,
                "out_format": "glb",
            })
            rig_out = wait(key, rig_task, "rig")
            url = rig_out.get("model") or rig_out.get("pbr_model")
            if isinstance(url, dict):
                url = url.get("url")
            if url:
                rigged = download(url, out / "ash_rigged.glb")
        except SystemExit as e:
            # Rigging is a separate paid task type and may be unavailable on some plans.
            print(f"  rig step unavailable, continuing with the unrigged mesh.\n  {e}")

    print("4/4 done")
    summary = {
        "model_task_id": model_task,
        "raw_model": str(raw),
        "rigged_model": str(rigged) if rigged else None,
        "references": {k: str(v) for k, v in found.items()},
    }
    (out / "tripo_result.json").write_text(json.dumps(summary, indent=2))
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    sys.exit(main())
