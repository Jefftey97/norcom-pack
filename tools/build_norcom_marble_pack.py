#!/usr/bin/env python3
"""Build the norcom_marble friends-pack resource pack.

Ten full families, labPBR normal/specular, Continuity method=ctm.
The black marble emits only on blue veins (specular alpha). Stone alpha stays 255,
which labPBR ignores, so the stone is not full-bright.

Does not touch the launcher feed.
"""

from __future__ import annotations

import hashlib
import io
import json
import zipfile
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
TEMPLATE_DIR = Path(__file__).resolve().parent / "marble_templates"
PACK = ROOT / "resourcepacks" / "_staging_norcom_marble_ctm"
ZIP_PATH = ROOT / "resourcepacks" / "norcom_marble.zip"
PREVIEW = Path("/opt/cursor/artifacts/norcom-marble-families.png")
NS = "norcom_marble"
SIZE = 128
SUFFIXES = (
    "_stairs",
    "_slab",
    "_wall",
    "_fence",
    "_fence_gate",
    "_button",
    "_pressure_plate",
)

# kind, palette, seed
FAMILIES = (
    ("veined_classic_white", "veined", "white", 1101),
    ("veined_norcom_blue", "veined", "blue", 2202),
    ("veined_neon_slate", "veined", "slate", 3303),
    ("solid_classic_white", "solid", "white", 4404),
    ("solid_norcom_blue", "solid", "blue", 5505),
    ("solid_neon_slate", "solid", "slate", 6606),
    ("soft_classic_white", "soft", "white", 7707),
    ("soft_norcom_blue", "soft", "blue", 8808),
    ("soft_neon_slate", "soft", "slate", 9909),
    ("veined_black_blue_glow", "glow", "black", 1010),
)


def smooth_noise(size: int, cells: int, rng: np.random.Generator) -> np.ndarray:
    grid = rng.random((cells, cells)).astype(np.float32)
    padded = np.empty((cells + 1, cells + 1), np.float32)
    padded[:-1, :-1] = grid
    padded[-1, :-1] = grid[0, :]
    padded[:-1, -1] = grid[:, 0]
    padded[-1, -1] = grid[0, 0]
    ys = np.linspace(0, cells, size, endpoint=False)
    xs = np.linspace(0, cells, size, endpoint=False)
    y0 = np.floor(ys).astype(np.int32)
    x0 = np.floor(xs).astype(np.int32)
    fy = (ys - y0).astype(np.float32)
    fx = (xs - x0).astype(np.float32)
    fy = fy * fy * (3.0 - 2.0 * fy)
    fx = fx * fx * (3.0 - 2.0 * fx)
    y1 = y0 + 1
    x1 = x0 + 1
    n00 = padded[y0[:, None], x0[None, :]]
    n10 = padded[y0[:, None], x1[None, :]]
    n01 = padded[y1[:, None], x0[None, :]]
    n11 = padded[y1[:, None], x1[None, :]]
    fx = fx[None, :]
    fy = fy[:, None]
    return (n00 * (1.0 - fx) + n10 * fx) * (1.0 - fy) + (n01 * (1.0 - fx) + n11 * fx) * fy


def fbm(size: int, seed: int, layers: tuple[tuple[int, float], ...]) -> np.ndarray:
    rng = np.random.default_rng(seed)
    acc = np.zeros((size, size), np.float32)
    weight = 0.0
    for cells, amp in layers:
        acc += amp * smooth_noise(size, cells, rng)
        weight += amp
    return acc / weight


def vein_mask(field: np.ndarray, coverage: float) -> np.ndarray:
    ridge = np.abs(np.sin(field * np.pi * 3.15)).astype(np.float32)
    # Pick a threshold so the smoothed mask covers about `coverage` of the face.
    lo = 0.45
    hi = 0.92
    for _ in range(18):
        mid = (lo + hi) * 0.5
        soft = np.clip((ridge - mid) / 0.16, 0.0, 1.0)
        soft = soft * soft * (3.0 - 2.0 * soft)
        if float(soft.mean()) > coverage:
            lo = mid
        else:
            hi = mid
    soft = np.clip((ridge - lo) / 0.16, 0.0, 1.0)
    return soft * soft * (3.0 - 2.0 * soft)


def palette(color: str) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    if color == "white":
        return (
            np.array([232, 228, 222], np.float32),
            np.array([250, 248, 244], np.float32),
            np.array([154, 162, 174], np.float32),
        )
    if color == "blue":
        # Royal blue. Blue channel leads so it cannot read as green.
        return (
            np.array([8, 28, 108], np.float32),
            np.array([28, 64, 176], np.float32),
            np.array([188, 206, 234], np.float32),
        )
    if color == "slate":
        return (
            np.array([20, 26, 40], np.float32),
            np.array([46, 54, 80], np.float32),
            np.array([88, 72, 242], np.float32),
        )
    return (
        np.array([7, 8, 11], np.float32),
        np.array([24, 26, 32], np.float32),
        np.array([36, 112, 255], np.float32),
    )


def build_field(kind: str, color: str, seed: int) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    if kind == "soft":
        grain = fbm(SIZE, seed, ((12, 0.45), (24, 0.35), (48, 0.20)))
        grain = 0.46 + (grain - 0.5) * 0.42
        vein = np.zeros((SIZE, SIZE), np.float32)
    elif kind == "solid":
        grain = fbm(SIZE, seed, ((4, 0.55), (8, 0.30), (16, 0.15)))
        grain = np.clip(0.08 + grain * 0.92, 0.0, 1.0)
        vein = np.zeros((SIZE, SIZE), np.float32)
    else:
        grain = fbm(SIZE, seed, ((4, 0.40), (8, 0.35), (18, 0.25)))
        grain = np.clip(0.22 + grain * 0.70, 0.0, 1.0)
        flow = fbm(SIZE, seed + 17, ((3, 0.65), (6, 0.35)))
        vein = vein_mask(flow, 0.11 if kind == "veined" else 0.09)
    base, high, vein_color = palette(color)
    if kind == "solid":
        # Wider albedo swing so grain still reads after the 16px mip.
        mid = (base + high) * 0.5
        base = np.clip(base - (mid - base) * 0.85, 0, 255)
        high = np.clip(high + (high - mid) * 0.65, 0, 255)
        if color == "white":
            base = np.array([186, 176, 164], np.float32)
            high = np.array([252, 250, 246], np.float32)
    elif kind == "soft" and color == "white":
        base = np.array([208, 200, 190], np.float32)
        high = np.array([250, 248, 244], np.float32)
    body = base + (high - base) * grain[..., None]
    if kind in ("veined", "glow"):
        rgb = body * (1.0 - vein[..., None]) + vein_color * vein[..., None]
    else:
        rgb = body
    return np.clip(rgb, 0, 255).astype(np.uint8), grain, vein


def normals_and_spec(
    kind: str, grain: np.ndarray, vein: np.ndarray, glow: bool
) -> tuple[np.ndarray, np.ndarray]:
    height = grain * (0.55 if kind == "solid" else 0.28 if kind == "soft" else 0.36)
    height = height - vein * 0.42
    strength = 3.2 if kind == "solid" else 1.7 if kind == "soft" else 2.3
    dx = np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)
    dy = np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)
    nx = -dx * strength
    ny = dy * strength  # labPBR DirectX: green points down the texture
    nz = np.ones_like(nx)
    length = np.sqrt(nx * nx + ny * ny + nz * nz)
    nx /= length
    ny /= length
    normal = np.zeros((SIZE, SIZE, 4), np.uint8)
    normal[..., 0] = np.clip(nx * 127.5 + 128.0, 0, 255).astype(np.uint8)
    normal[..., 1] = np.clip(ny * 127.5 + 128.0, 0, 255).astype(np.uint8)
    # Blue is ambient occlusion, not Z. Keep pits slightly darker, never crushed.
    ao = 255.0 - (1.0 - grain) * (18.0 if kind == "solid" else 8.0) - vein * 10.0
    normal[..., 2] = np.clip(ao, 210, 255).astype(np.uint8)
    # Alpha is height. 255 is flush. Stay well above 1 so POM cannot collapse the face.
    normal[..., 3] = np.clip(232.0 + grain * 20.0 - vein * 28.0, 188, 255).astype(np.uint8)

    if kind == "soft":
        smooth = 122.0 + (grain - 0.5) * 16.0
        porosity = 6.0
    elif kind == "solid":
        smooth = 74.0 + (grain - 0.5) * 26.0
        porosity = 12.0
    elif kind == "glow":
        smooth = 90.0 + (grain - 0.5) * 14.0
        smooth = smooth * (1.0 - vein) + 148.0 * vein
        porosity = 5.0
    else:
        smooth = 98.0 + (grain - 0.5) * 18.0
        porosity = 9.0
    spec = np.zeros((SIZE, SIZE, 4), np.uint8)
    spec[..., 0] = np.clip(smooth, 48, 158).astype(np.uint8)
    # Dielectric F0. Stay far below the metal range (230+).
    spec[..., 1] = np.clip(16.0 + grain * 8.0, 12, 36).astype(np.uint8)
    spec[..., 2] = np.clip(porosity + grain * 3.0, 0, 40).astype(np.uint8)
    spec[..., 3] = 255
    if glow:
        # 255 means "no emission" in labPBR. Veins use 1..250. Stone stays 255.
        emit = np.clip(vein * 235.0, 0, 248)
        alpha = np.where(vein > 0.04, np.clip(emit, 1, 248), 255).astype(np.uint8)
        spec[..., 3] = alpha
    return normal, spec


def save_png(path: Path, array: np.ndarray) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    mode = "RGBA" if array.shape[-1] == 4 else "RGB"
    Image.fromarray(array, mode).save(path, optimize=True)


def assert_wrap(name: str, rgb: np.ndarray) -> None:
    image = rgb.astype(np.int16)
    seam_x = np.abs(image[:, 0, :] - image[:, -1, :]).mean()
    inner_x = np.abs(image[:, 1:, :] - image[:, :-1, :]).mean()
    seam_y = np.abs(image[0, :, :] - image[-1, :, :]).mean()
    inner_y = np.abs(image[1:, :, :] - image[:-1, :, :]).mean()
    if seam_x > inner_x * 1.45 + 3 or seam_y > inner_y * 1.45 + 3:
        raise SystemExit(f"{name} has a seam (x {seam_x:.2f}/{inner_x:.2f}, y {seam_y:.2f}/{inner_y:.2f})")


def assert_family(name: str, kind: str, color: str, rgb: np.ndarray, spec: np.ndarray, vein: np.ndarray) -> None:
    mean = rgb.reshape(-1, 3).mean(axis=0)
    if color == "blue":
        if not (mean[2] > mean[1] + 25 and mean[2] > mean[0] + 25):
            raise SystemExit(f"{name} is not blue enough: {mean}")
    if color == "slate":
        if mean[1] >= mean[2]:
            raise SystemExit(f"{name} slate reads green: {mean}")
    if color == "white":
        if mean.min() < 170:
            raise SystemExit(f"{name} white is too dark: {mean}")
    if color == "black":
        if mean.mean() > 55:
            raise SystemExit(f"{name} stone is too bright: {mean}")
    if int(spec[..., 1].max()) >= 230:
        raise SystemExit(f"{name} specular F0 entered the metal range")
    if int(spec[..., 0].max()) > 170:
        raise SystemExit(f"{name} smoothness is mirror-smooth")
    if int(spec[..., 2].max()) > 64:
        raise SystemExit(f"{name} porosity spilled into subsurface")
    if kind == "glow":
        stone = vein <= 0.04
        vein_px = vein > 0.45
        if not np.all(spec[..., 3][stone] == 255):
            raise SystemExit(f"{name} stone emits; it must stay unlit")
        if int(vein_px.sum()) < 20 or int(spec[..., 3][vein_px].min()) < 1:
            raise SystemExit(f"{name} veins do not emit")
        if int(spec[..., 3][vein_px].max()) >= 255:
            raise SystemExit(f"{name} vein emission was stored as the ignored alpha")
        glow_frac = float(np.mean(spec[..., 3] != 255))
        if glow_frac > 0.28 or glow_frac < 0.04:
            raise SystemExit(f"{name} glow coverage {glow_frac:.3f} is not a vein")
        vein_mean = rgb[vein_px].mean(axis=0)
        if not (vein_mean[2] > vein_mean[1] + 40 and vein_mean[2] > vein_mean[0] + 40):
            raise SystemExit(f"{name} glowing veins are not blue: {vein_mean}")
    else:
        if not np.all(spec[..., 3] == 255):
            raise SystemExit(f"{name} should not emit")
    if kind in ("veined", "glow"):
        cover = float((vein > 0.35).mean())
        if cover < 0.03 or cover > 0.22:
            raise SystemExit(f"{name} vein coverage {cover:.3f} is out of range")
    else:
        if float(vein.max()) > 0:
            raise SystemExit(f"{name} solid has veins")
    small = Image.fromarray(rgb, "RGB").resize((16, 16), Image.Resampling.BOX)
    std = float(np.asarray(small).std())
    floor = 4.0 if kind == "soft" else 6.0
    if std < floor:
        raise SystemExit(f"{name} loses grain at the 16px mip (std {std:.2f})")
    assert_wrap(name, rgb)


def write_json(path: Path, payload: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")


def load_template(name: str) -> dict:
    return json.loads((TEMPLATE_DIR / name).read_text(encoding="utf-8"))


def swap_models(node: object, mapping: dict[str, str]) -> object:
    if isinstance(node, dict):
        out = {}
        for key, value in node.items():
            if key == "model" and isinstance(value, str):
                if value not in mapping:
                    raise SystemExit(f"unmapped model {value}")
                out[key] = mapping[value]
            else:
                out[key] = swap_models(value, mapping)
        return out
    if isinstance(node, list):
        return [swap_models(item, mapping) for item in node]
    return node


def ctm_properties(base: str) -> str:
    ids = [base] + [base + suffix for suffix in SUFFIXES]
    tiles = " ".join([f"{NS}:{base}"] * 47)
    blocks = " ".join(f"{NS}:{item}" for item in ids)
    return (
        "# Seamless tile on every CTM index. Veins and grain wrap, so neighbors join.\n"
        "# No random variants. innerSeams stays off.\n"
        "method=ctm\n"
        "innerSeams=false\n"
        "connect=tile\n"
        "faces=all\n"
        f"matchTiles={NS}:{base}\n"
        f"matchBlocks={blocks}\n"
        f"tiles={tiles}\n"
    )


def cube(tex: str) -> dict:
    return {"parent": "minecraft:block/cube_all", "textures": {"all": tex}}


def face_textures(parent: str, tex: str) -> dict:
    return {"parent": parent, "textures": {"bottom": tex, "top": tex, "side": tex}}


def wall_model(parent: str, tex: str) -> dict:
    return {"parent": parent, "textures": {"wall": tex}}


def single_texture(parent: str, tex: str) -> dict:
    return {"parent": parent, "textures": {"texture": tex}}


def write_models(base: str) -> None:
    tex = f"{NS}:block/{base}"
    block = PACK / "assets" / NS / "models" / "block"
    write_json(block / f"{base}.json", cube(tex))
    write_json(block / f"{base}_stairs.json", face_textures("minecraft:block/stairs", tex))
    write_json(block / f"{base}_stairs_inner.json", face_textures("minecraft:block/inner_stairs", tex))
    write_json(block / f"{base}_stairs_outer.json", face_textures("minecraft:block/outer_stairs", tex))
    write_json(block / f"{base}_slab.json", face_textures("minecraft:block/slab", tex))
    write_json(block / f"{base}_slab_top.json", face_textures("minecraft:block/slab_top", tex))
    write_json(block / f"{base}_wall_post.json", wall_model("minecraft:block/template_wall_post", tex))
    write_json(block / f"{base}_wall_side.json", wall_model("minecraft:block/template_wall_side", tex))
    write_json(block / f"{base}_wall_side_tall.json", wall_model("minecraft:block/template_wall_side_tall", tex))
    write_json(block / f"{base}_wall_inventory.json", wall_model("minecraft:block/wall_inventory", tex))
    write_json(block / f"{base}_fence_post.json", single_texture("minecraft:block/fence_post", tex))
    write_json(block / f"{base}_fence_side.json", single_texture("minecraft:block/fence_side", tex))
    write_json(block / f"{base}_fence_inventory.json", single_texture("minecraft:block/fence_inventory", tex))
    write_json(block / f"{base}_fence_gate.json", single_texture("minecraft:block/template_fence_gate", tex))
    write_json(block / f"{base}_fence_gate_open.json", single_texture("minecraft:block/template_fence_gate_open", tex))
    write_json(block / f"{base}_fence_gate_wall.json", single_texture("minecraft:block/template_fence_gate_wall", tex))
    write_json(block / f"{base}_fence_gate_wall_open.json", single_texture("minecraft:block/template_fence_gate_wall_open", tex))
    write_json(block / f"{base}_button.json", single_texture("minecraft:block/button", tex))
    write_json(block / f"{base}_button_pressed.json", single_texture("minecraft:block/button_pressed", tex))
    write_json(block / f"{base}_button_inventory.json", single_texture("minecraft:block/button_inventory", tex))
    write_json(block / f"{base}_pressure_plate.json", single_texture("minecraft:block/pressure_plate_up", tex))
    write_json(block / f"{base}_pressure_plate_down.json", single_texture("minecraft:block/pressure_plate_down", tex))


def write_blockstates(base: str) -> None:
    states = PACK / "assets" / NS / "blockstates"
    write_json(states / f"{base}.json", {"variants": {"": {"model": f"{NS}:block/{base}"}}})
    stair_map = {
        "minecraft:block/stone_stairs": f"{NS}:block/{base}_stairs",
        "minecraft:block/stone_stairs_inner": f"{NS}:block/{base}_stairs_inner",
        "minecraft:block/stone_stairs_outer": f"{NS}:block/{base}_stairs_outer",
    }
    write_json(states / f"{base}_stairs.json", swap_models(load_template("stone_stairs.json"), stair_map))
    write_json(
        states / f"{base}_slab.json",
        {
            "variants": {
                "type=bottom": {"model": f"{NS}:block/{base}_slab"},
                "type=double": {"model": f"{NS}:block/{base}"},
                "type=top": {"model": f"{NS}:block/{base}_slab_top"},
            }
        },
    )
    wall_map = {
        "minecraft:block/cobblestone_wall_post": f"{NS}:block/{base}_wall_post",
        "minecraft:block/cobblestone_wall_side": f"{NS}:block/{base}_wall_side",
        "minecraft:block/cobblestone_wall_side_tall": f"{NS}:block/{base}_wall_side_tall",
    }
    write_json(states / f"{base}_wall.json", swap_models(load_template("cobblestone_wall.json"), wall_map))
    fence_map = {
        "minecraft:block/oak_fence_post": f"{NS}:block/{base}_fence_post",
        "minecraft:block/oak_fence_side": f"{NS}:block/{base}_fence_side",
    }
    write_json(states / f"{base}_fence.json", swap_models(load_template("oak_fence.json"), fence_map))
    gate_map = {
        "minecraft:block/oak_fence_gate": f"{NS}:block/{base}_fence_gate",
        "minecraft:block/oak_fence_gate_open": f"{NS}:block/{base}_fence_gate_open",
        "minecraft:block/oak_fence_gate_wall": f"{NS}:block/{base}_fence_gate_wall",
        "minecraft:block/oak_fence_gate_wall_open": f"{NS}:block/{base}_fence_gate_wall_open",
    }
    write_json(states / f"{base}_fence_gate.json", swap_models(load_template("oak_fence_gate.json"), gate_map))
    button_map = {
        "minecraft:block/stone_button": f"{NS}:block/{base}_button",
        "minecraft:block/stone_button_pressed": f"{NS}:block/{base}_button_pressed",
    }
    write_json(states / f"{base}_button.json", swap_models(load_template("stone_button.json"), button_map))
    plate_map = {
        "minecraft:block/stone_pressure_plate": f"{NS}:block/{base}_pressure_plate",
        "minecraft:block/stone_pressure_plate_down": f"{NS}:block/{base}_pressure_plate_down",
    }
    write_json(states / f"{base}_pressure_plate.json", swap_models(load_template("stone_pressure_plate.json"), plate_map))


def write_items(base: str) -> None:
    parents = {
        base: f"{NS}:block/{base}",
        f"{base}_stairs": f"{NS}:block/{base}_stairs",
        f"{base}_slab": f"{NS}:block/{base}_slab",
        f"{base}_wall": f"{NS}:block/{base}_wall_inventory",
        f"{base}_fence": f"{NS}:block/{base}_fence_inventory",
        f"{base}_fence_gate": f"{NS}:block/{base}_fence_gate",
        f"{base}_button": f"{NS}:block/{base}_button_inventory",
        f"{base}_pressure_plate": f"{NS}:block/{base}_pressure_plate",
    }
    legacy = PACK / "assets" / NS / "models" / "item"
    modern = PACK / "assets" / NS / "items"
    for item_id, parent in parents.items():
        write_json(legacy / f"{item_id}.json", {"parent": parent})
        write_json(modern / f"{item_id}.json", {"model": {"type": "minecraft:model", "model": parent}})


def write_ctm(base: str) -> None:
    path = PACK / "assets" / NS / "optifine" / "ctm" / f"{base}.properties"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(ctm_properties(base), encoding="utf-8")


def pack_icon(rgb: np.ndarray) -> None:
    icon = Image.fromarray(rgb, "RGB").resize((64, 64), Image.Resampling.BOX)
    icon.save(PACK / "pack.png", optimize=True)


def contact_sheet(images: list[tuple[str, np.ndarray, np.ndarray]]) -> None:
    cell = 96
    label_h = 28
    cols = len(images)
    rows = 3
    sheet = Image.new("RGB", (cols * cell, rows * (cell + label_h)), (18, 18, 20))
    draw = ImageDraw.Draw(sheet)
    captions = ("albedo", "normal", "specular")
    for col, (name, albedo, spec) in enumerate(images):
        normal = np.zeros_like(albedo)
        # Show the tangent normal the way an artist reads it: R and G around mid grey.
        # The stored normal is RGBA; rebuild a view from the file later. Here use albedo row only
        # plus a spec RGB view and a glow view in the third row for the black marble.
        tiles = (
            Image.fromarray(albedo, "RGB").resize((cell, cell), Image.Resampling.BOX),
            None,
            None,
        )
        # placeholders filled below
        del tiles
        views = []
        views.append(Image.fromarray(albedo, "RGB").resize((cell, cell), Image.Resampling.BOX))
        n = np.array(Image.open(PACK / "assets" / NS / "textures" / "block" / f"{name}_n.png"))
        n_view = np.stack([n[..., 0], n[..., 1], n[..., 2]], axis=-1)
        views.append(Image.fromarray(n_view, "RGB").resize((cell, cell), Image.Resampling.NEAREST))
        s = np.array(Image.open(PACK / "assets" / NS / "textures" / "block" / f"{name}_s.png"))
        if name == "veined_black_blue_glow":
            glow = s[..., 3] != 255
            view = np.zeros((SIZE, SIZE, 3), np.uint8)
            view[..., 2] = np.where(glow, 255, 0).astype(np.uint8)
            view[..., 0] = np.where(glow, 20, 0).astype(np.uint8)
            views.append(Image.fromarray(view, "RGB").resize((cell, cell), Image.Resampling.NEAREST))
            captions_local = ("albedo", "normal", "vein glow only")
        else:
            spec_view = np.stack([s[..., 0], s[..., 1], np.full_like(s[..., 0], 40)], axis=-1)
            views.append(Image.fromarray(spec_view, "RGB").resize((cell, cell), Image.Resampling.NEAREST))
            captions_local = captions
        for row, view in enumerate(views):
            x = col * cell
            y = row * (cell + label_h)
            sheet.paste(view, (x, y + label_h))
            draw.text((x + 4, y + 4), f"{name}\n{captions_local[row]}", fill=(230, 230, 230))
    PREVIEW.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(PREVIEW, optimize=True)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    digest.update(path.read_bytes())
    return digest.hexdigest()


def build_zip() -> None:
    if ZIP_PATH.exists():
        ZIP_PATH.unlink()
    with zipfile.ZipFile(ZIP_PATH, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(PACK.rglob("*")):
            if path.is_file():
                archive.write(path, path.relative_to(PACK).as_posix())


def validate_pack() -> None:
    bases = [item[0] for item in FAMILIES]
    if len(bases) != 10:
        raise SystemExit("expected 10 families")
    ids = []
    for base in bases:
        ids.append(f"{NS}:{base}")
        for suffix in SUFFIXES:
            ids.append(f"{NS}:{base}{suffix}")
    if len(ids) != 80 or len(set(ids)) != 80:
        raise SystemExit(f"expected 80 unique ids, got {len(set(ids))}")
    states = list((PACK / "assets" / NS / "blockstates").glob("*.json"))
    if len(states) != 80:
        raise SystemExit(f"expected 80 blockstates, got {len(states)}")
    for path in (PACK / "assets" / NS / "optifine" / "ctm").glob("*.properties"):
        text = path.read_text(encoding="utf-8")
        if "method=ctm" not in text or "method=random" in text:
            raise SystemExit(f"{path.name} is not a fixed ctm rule")
        if "innerSeams=false" not in text or "connect=tile" not in text:
            raise SystemExit(f"{path.name} missing stitch settings")
        tiles = text.split("tiles=", 1)[1].split()
        if len(tiles) != 47 or len(set(tiles)) != 1:
            raise SystemExit(f"{path.name} must repeat one seamless tile 47 times")
    forbidden = list(PACK.rglob("*candle*")) + list(PACK.rglob("*Patrix*")) + list(PACK.rglob("*patrix*"))
    if forbidden:
        raise SystemExit(f"pack collided with candle or Patrix files: {forbidden}")
    minecraft_overrides = list((PACK / "assets").glob("minecraft/**/*"))
    if minecraft_overrides:
        raise SystemExit("pack must not override vanilla/Alacrity assets")


def main() -> None:
    if PACK.exists():
        for child in sorted(PACK.rglob("*"), reverse=True):
            if child.is_file():
                child.unlink()
            elif child.is_dir():
                child.rmdir()
    PACK.mkdir(parents=True, exist_ok=True)
    write_json(
        PACK / "pack.mcmeta",
        {
            "pack": {
                "pack_format": 15,
                "description": "Marble families. Clarity-style PBR. Connected textures.",
                "supported_formats": {"min_inclusive": 15, "max_inclusive": 999},
                "min_format": 15,
                "max_format": 999,
            }
        },
    )
    previews = []
    first_albedo = None
    for base, kind, color, seed in FAMILIES:
        rgb, grain, vein = build_field(kind, color, seed)
        normal, spec = normals_and_spec(kind, grain, vein, glow=(kind == "glow"))
        assert_family(base, kind, color, rgb, spec, vein)
        tex_dir = PACK / "assets" / NS / "textures" / "block"
        save_png(tex_dir / f"{base}.png", rgb)
        save_png(tex_dir / f"{base}_n.png", normal)
        save_png(tex_dir / f"{base}_s.png", spec)
        write_models(base)
        write_blockstates(base)
        write_items(base)
        write_ctm(base)
        previews.append((base, rgb, spec))
        if first_albedo is None:
            first_albedo = rgb
        print(
            f"{base}: albedo {rgb.reshape(-1, 3).mean(0).round(1).tolist()} "
            f"smooth {int(spec[..., 0].mean())} f0 {int(spec[..., 1].mean())} "
            f"vein {(vein > 0.35).mean():.3f}"
        )
    pack_icon(first_albedo)
    catalog = []
    for base, kind, color, _seed in FAMILIES:
        entry = {"base": base, "kind": kind, "palette": color, "ids": [f"{NS}:{base}"]}
        entry["ids"].extend(f"{NS}:{base}{suffix}" for suffix in SUFFIXES)
        entry["texture"] = f"{NS}:block/{base}"
        entry["ctm"] = f"assets/{NS}/optifine/ctm/{base}.properties"
        entry["pbr"] = [f"{base}_n.png", f"{base}_s.png"]
        if kind == "glow":
            entry["emissive"] = "specular alpha on blue veins only; stone alpha 255 (labPBR ignores 255)"
        catalog.append(entry)
    write_json(
        PACK / "marble-ids.json",
        {
            "namespace": NS,
            "texture_size": SIZE,
            "ctm_method": "ctm",
            "connect": "tile",
            "inner_seams": False,
            "random_variants": False,
            "pbr": "labPBR _n and _s",
            "friends_pack_path": "resourcepacks/_staging_norcom_marble_ctm",
            "zip": "resourcepacks/norcom_marble.zip",
            "enable_when_dig_ready": (
                "Add file/norcom_marble.zip after file/norcom-alacrity-candle-glass-fix.zip. "
                "Keep Alacrity and the candle overlay. Do not enable Patrix or continuity:glass_pane_culling_fix."
            ),
            "feed": "2.0.35 unchanged",
            "families": catalog,
        },
    )
    validate_pack()
    lines = []
    for path in sorted(PACK.rglob("*")):
        if path.is_file():
            lines.append(f"{sha256(path)}  {path.relative_to(PACK).as_posix()}")
    (PACK / "SHA256SUMS.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    build_zip()
    contact_sheet(previews)
    print(f"zip {ZIP_PATH} {sha256(ZIP_PATH)}")
    print(f"preview {PREVIEW}")


if __name__ == "__main__":
    main()
