"""
Free image-to-3D via HuggingFace Spaces. No paid API needed.

  python _Tools/hf_image_to_3d.py --views _Concept/Ash --out _Concept/Ash/generated
  python _Tools/hf_image_to_3d.py --views _Concept/Ash --out ... --backend trellis

Backends
  hunyuan  tencent/Hunyuan3D-2.1  - native front/back/left/right input, PBR textures.
                                    Tencent Community License: excludes EU/UK/South
                                    Korea, and >1M MAU needs Tencent approval.
  trellis  trellis-community/TRELLIS - MIT licensed, no commercial strings attached.

A free HuggingFace token raises the ZeroGPU quota considerably; set HF_TOKEN if the
anonymous attempt is rejected.
"""

import argparse
import os
import shutil
import sys
from pathlib import Path

try:
    from gradio_client import Client, handle_file
except ImportError:
    raise SystemExit("pip install gradio_client")

SPACES = {"hunyuan": "tencent/Hunyuan3D-2.1", "trellis": "trellis-community/TRELLIS"}


def find_views(folder: Path):
    """Maps reference images onto view slots by filename hint."""
    hints = {"front": ("front",), "back": ("back", "rear"), "right": ("right", "side"), "left": ("left",)}
    imgs = sorted(folder.glob("*.png")) + sorted(folder.glob("*.jpg"))
    found = {}
    for slot, keys in hints.items():
        for img in imgs:
            if slot not in found and any(k in img.stem.lower() for k in keys):
                found[slot] = img
    if "front" not in found:
        raise SystemExit(f"no front reference in {folder} (need a file with 'front' in the name)")
    return found


def collect(result, out: Path, stem: str):
    """Pulls every mesh file out of a Gradio result tuple into the output folder."""
    saved = []
    items = result if isinstance(result, (list, tuple)) else [result]
    for item in items:
        path = None
        if isinstance(item, str):
            path = item
        elif isinstance(item, dict):
            path = item.get("path") or item.get("value") or item.get("name")
        if not path or not isinstance(path, str):
            continue
        src = Path(path)
        if not src.exists() or src.suffix.lower() not in (".glb", ".gltf", ".obj", ".ply", ".fbx", ".zip"):
            continue
        dest = out / f"{stem}_{len(saved)}{src.suffix.lower()}" if saved else out / f"{stem}{src.suffix.lower()}"
        shutil.copy2(src, dest)
        print(f"  saved {dest.name}  ({dest.stat().st_size / 1024:.0f} KB)")
        saved.append(dest)
    return saved


def run_hunyuan(client, views, args):
    def f(slot):
        return handle_file(str(views[slot])) if slot in views else None

    print("  submitting multiview shape + texture generation")
    return client.predict(
        image=None,
        mv_image_front=f("front"),
        mv_image_back=f("back"),
        mv_image_left=f("left"),
        mv_image_right=f("right"),
        steps=args.steps,
        guidance_scale=args.guidance,
        seed=args.seed,
        octree_resolution=args.octree,
        check_box_rembg=True,
        num_chunks=8000,
        randomize_seed=False,
        api_name="/generation_all",
    )


def run_trellis(client, views, args):
    # The Space allocates a per-session temp dir here. Without it the generate call
    # fails with FileNotFoundError when it tries to write the GLB.
    try:
        client.predict(api_name="/start_session")
        print("  session started")
    except Exception as e:
        print(f"  start_session failed ({type(e).__name__}), continuing anyway")

    # TRELLIS takes extra views through a gallery rather than named slots.
    extra = [{"image": handle_file(str(p))} for slot, p in views.items() if slot != "front"]
    print(f"  submitting with {len(extra) + 1} views (multidiffusion)")
    return client.predict(
        image=handle_file(str(views["front"])),
        multiimages=extra,
        seed=args.seed,
        ss_guidance_strength=7.5,
        ss_sampling_steps=12,
        slat_guidance_strength=3.0,
        slat_sampling_steps=12,
        multiimage_algo="multidiffusion",
        mesh_simplify=0.95,
        texture_size=1024,
        api_name="/generate_and_extract_glb",
    )


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--views", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--backend", choices=list(SPACES), default="hunyuan")
    ap.add_argument("--stem", default="ash_raw")
    ap.add_argument("--seed", type=int, default=1234)
    ap.add_argument("--steps", type=int, default=30)
    ap.add_argument("--guidance", type=float, default=5.0)
    ap.add_argument("--octree", type=int, default=256)
    args = ap.parse_args()

    views = find_views(Path(args.views))
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    print("references:", {k: v.name for k, v in views.items()})

    space = SPACES[args.backend]
    token = os.environ.get("HF_TOKEN") or None
    print(f"connecting to {space}" + ("  (with HF token)" if token else "  (anonymous)"))

    try:
        client = Client(space, token=token, verbose=False)
    except Exception as e:
        raise SystemExit(f"could not reach {space}: {type(e).__name__}: {e}")

    try:
        result = (run_hunyuan if args.backend == "hunyuan" else run_trellis)(client, views, args)
    except Exception as e:
        msg = str(e)
        hint = ""
        if "quota" in msg.lower() or "gpu" in msg.lower():
            hint = ("\nThis Space runs on ZeroGPU, which limits anonymous use. Create a free "
                    "HuggingFace token at https://huggingface.co/settings/tokens and set HF_TOKEN.")
        raise SystemExit(f"generation failed: {type(e).__name__}: {msg[:800]}{hint}")

    saved = collect(result, out, args.stem)
    if not saved:
        raise SystemExit(f"no mesh file came back. Raw result was:\n{result}")
    print(f"done: {len(saved)} file(s) in {out}")


if __name__ == "__main__":
    sys.exit(main())
