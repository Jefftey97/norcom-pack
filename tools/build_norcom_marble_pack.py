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
PREVIEW = Path("/opt/cursor/artifacts/norcom-marble-after.png")
SHEET = Path("/opt/cursor/artifacts/norcom-marble-families.png")
NS = "norcom_marble"
SIZE = 256
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


def worley(size: int, cells: int, rng: np.random.Generator) -> tuple[np.ndarray, np.ndarray]:
    """Seamless crystal cells. Returns cell brightness and a 0-1 facet edge."""
    pts = rng.random((cells, cells, 2)).astype(np.float32)
    bright = rng.random((cells, cells)).astype(np.float32)
    ys = np.linspace(0, cells, size, endpoint=False).astype(np.float32)
    xs = np.linspace(0, cells, size, endpoint=False).astype(np.float32)
    y0 = np.floor(ys).astype(np.int32)
    x0 = np.floor(xs).astype(np.int32)
    best_d = np.full((size, size), np.float32(1e9))
    second_d = np.full((size, size), np.float32(1e9))
    best_b = np.zeros((size, size), np.float32)
    for oy in (-1, 0, 1):
        for ox in (-1, 0, 1):
            cy = (y0 + oy) % cells
            cx = (x0 + ox) % cells
            point = pts[cy[:, None], cx[None, :]]
            feat_y = (y0[:, None] + oy) + point[..., 0]
            feat_x = (x0[None, :] + ox) + point[..., 1]
            dy = ys[:, None] - feat_y
            dx = xs[None, :] - feat_x
            dist = dx * dx + dy * dy
            shade = bright[cy[:, None], cx[None, :]]
            closer = dist < best_d
            second_d = np.where(closer, best_d, np.minimum(second_d, dist))
            best_b = np.where(closer, shade, best_b)
            best_d = np.minimum(best_d, dist)
    edge = np.clip((np.sqrt(second_d) - np.sqrt(best_d)) * cells * 1.4, 0.0, 1.0)
    return best_b.astype(np.float32), edge.astype(np.float32)


def _cells(size: int, base: int) -> int:
    return max(3, min(size // 2, int(round(base * size / 128.0))))


def crystal_grain(size: int, seed: int, kind: str) -> np.ndarray:
    """Same micro-grain frequencies on every finish. Soft adds cloud and lowers contrast."""
    rng = np.random.default_rng(seed)
    facets, edge = worley(size, _cells(size, 36), rng)
    grit = fbm(size, seed + 5, ((_cells(size, 48), 0.4), (_cells(size, 72), 0.35), (_cells(size, 96), 0.25)))
    speckle = fbm(size, seed + 19, ((_cells(size, 80), 1.0),))
    cloud = fbm(size, seed + 29, ((_cells(size, 5), 0.65), (_cells(size, 9), 0.35)))
    spark = np.clip((grit - 0.5) * (1.15 if kind == "solid" else 0.85) + 0.5, 0.0, 1.0)
    if kind == "soft":
        grain = spark * 0.34 + facets * 0.18 + speckle * 0.16 + cloud * 0.32
        grain = 0.52 + (grain - 0.5) * 0.5
    elif kind == "solid":
        grain = spark * 0.40 + facets * 0.26 + speckle * 0.22 + cloud * 0.12
        grain = 0.46 + (grain - 0.5) * 0.92
    else:
        grain = spark * 0.36 + facets * 0.22 + speckle * 0.18 + cloud * 0.24
        grain = 0.5 + (grain - 0.5) * 0.64
    grain = grain * (0.9 + 0.1 * edge)
    return np.clip(grain, 0.0, 1.0).astype(np.float32)


def _noise1d(t: np.ndarray, knots: np.ndarray) -> np.ndarray:
    count = len(knots)
    x = t * count
    i0 = np.floor(x).astype(np.int32) % count
    frac = x - np.floor(x)
    frac = frac * frac * (3.0 - 2.0 * frac)
    i1 = (i0 + 1) % count
    return knots[i0] * (1.0 - frac) + knots[i1] * frac


def mineral_curve(
    size: int,
    rng: np.random.Generator,
    nx: int,
    ny: int,
    amp: float,
    t0: float = 0.0,
    t1: float = 1.0,
    samples: int = 360,
) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Irregular mineral line. Integer steps keep a full-span line seamless."""
    x0 = float(rng.random() * size)
    y0 = float(rng.random() * size)
    if nx == 0 and ny == 0:
        ny = 1
    t = np.linspace(t0, t1, samples, endpoint=False if t1 >= 1.0 else True)
    knots = rng.normal(0.0, 1.0, size=5).astype(np.float32)
    knots_fine = rng.normal(0.0, 1.0, size=9).astype(np.float32)
    wander = amp * _noise1d(t, knots) + amp * 0.45 * _noise1d(t, knots_fine)
    length = float(np.hypot(nx, ny))
    px = -ny / length
    py = nx / length
    cx = x0 + t * nx * size + px * wander
    cy = y0 + t * ny * size + py * wander
    if t1 < 1.0:
        span = max(t1 - t0, 1e-4)
        fade = np.sin(np.clip((t - t0) / span, 0.0, 1.0) * np.pi).astype(np.float32)
    else:
        fade = np.ones_like(t, dtype=np.float32)
    return cx.astype(np.float32), cy.astype(np.float32), fade


def curve_mask(size: int, cx: np.ndarray, cy: np.ndarray, radius: float, fade: np.ndarray | None = None) -> np.ndarray:
    ys = np.arange(size, dtype=np.float32)[:, None]
    xs = np.arange(size, dtype=np.float32)[None, :]
    acc = np.zeros((size, size), np.float32)
    step = 16
    for i in range(0, len(cx), step):
        cxx = cx[i : i + step][:, None, None]
        cyy = cy[i : i + step][:, None, None]
        dx = np.abs(xs[None, :, :] - cxx)
        dy = np.abs(ys[None, :, :] - cyy)
        dx = np.minimum(dx, size - dx)
        dy = np.minimum(dy, size - dy)
        dist = np.sqrt(dx * dx + dy * dy)
        weight = np.exp(-((dist / radius) ** 2))
        if fade is not None:
            weight = weight * fade[i : i + step][:, None, None]
        acc = np.maximum(acc, weight.max(axis=0))
    return acc


def marble_veins(size: int, seed: int) -> np.ndarray:
    """Shared organic vein stroke. Thin mineral branches, not a lattice."""
    rng = np.random.default_rng(seed + 90)
    scale = size / 128.0
    # About 20% thinner than the HQ black-marble stroke (emission fraction ~0.107).
    radius = 1.6 * scale
    samples = int(size * 2.4)
    cx, cy, fade = mineral_curve(size, rng, nx=1, ny=0, amp=7.5 * scale, samples=samples)
    mask = curve_mask(size, cx, cy, radius, fade)
    cx, cy, fade = mineral_curve(size, rng, nx=1, ny=1, amp=6.0 * scale, samples=samples)
    mask = np.maximum(mask, curve_mask(size, cx, cy, radius * 0.92, fade) * 0.94)
    cx, cy, fade = mineral_curve(
        size, rng, nx=1, ny=-1, amp=4.5 * scale, t0=0.2, t1=0.58, samples=max(120, samples // 3)
    )
    mask = np.maximum(mask, curve_mask(size, cx, cy, radius * 0.78, fade) * 0.82)
    breaker = fbm(size, seed + 77, ((_cells(size, 5), 0.6), (_cells(size, 9), 0.4)))
    keep = np.clip((breaker - 0.26) / 0.5, 0.0, 1.0)
    return np.clip(mask * (0.15 + 0.85 * keep), 0.0, 1.0)


def stone_colors(color: str) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    if color == "white":
        return (
            np.array([176, 170, 162], np.float32),
            np.array([248, 246, 242], np.float32),
            np.array([146, 150, 158], np.float32),
        )
    if color == "blue":
        # Cobalt. Blue stays well ahead of green.
        return (
            np.array([6, 22, 92], np.float32),
            np.array([36, 72, 188], np.float32),
            np.array([176, 198, 232], np.float32),
        )
    if color == "slate":
        return (
            np.array([16, 20, 32], np.float32),
            np.array([52, 60, 86], np.float32),
            np.array([72, 84, 118], np.float32),
        )
    return (
        np.array([6, 7, 9], np.float32),
        np.array([20, 22, 28], np.float32),
        np.array([64, 148, 255], np.float32),
    )


def _warp(field: np.ndarray, seed: int, amp: float) -> np.ndarray:
    size = field.shape[0]
    wx = fbm(size, seed, ((_cells(size, 3), 1.0),))
    wy = fbm(size, seed + 1, ((_cells(size, 3), 1.0),))
    yy, xx = np.mgrid[0:size, 0:size]
    sx = np.mod(xx + (wx - 0.5) * amp, size)
    sy = np.mod(yy + (wy - 0.5) * amp, size)
    x0 = np.floor(sx).astype(np.int32) % size
    y0 = np.floor(sy).astype(np.int32) % size
    x1 = (x0 + 1) % size
    y1 = (y0 + 1) % size
    fx = (sx - np.floor(sx)).astype(np.float32)
    fy = (sy - np.floor(sy)).astype(np.float32)
    return (
        field[y0, x0] * (1 - fx) * (1 - fy)
        + field[y0, x1] * fx * (1 - fy)
        + field[y1, x0] * (1 - fx) * fy
        + field[y1, x1] * fx * fy
    ).astype(np.float32)


def mottled_norcom_blue(seed: int) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """White clouds locked into deep blue, matching the veined blue concept cube."""
    cloud = fbm(SIZE, seed, ((_cells(SIZE, 3), 0.55), (_cells(SIZE, 6), 0.30), (_cells(SIZE, 12), 0.15)))
    cloud = _warp(cloud, seed + 8, SIZE * 0.12)
    grain = crystal_grain(SIZE, seed + 21, "veined")
    t = np.clip((cloud - 0.36) / 0.30, 0.0, 1.0)
    t = t * t * (3.0 - 2.0 * t)
    deep = np.array([24.0, 58.0, 138.0], np.float32)
    mid = np.array([62.0, 118.0, 196.0], np.float32)
    pale = np.array([228.0, 236.0, 246.0], np.float32)
    low = deep + (mid - deep) * np.clip(t * 1.35, 0.0, 1.0)[..., None]
    high = mid + (pale - mid) * np.clip((t - 0.42) / 0.58, 0.0, 1.0)[..., None]
    body = np.where(t[..., None] < 0.42, low, high)
    body = body + (grain[..., None] - 0.5) * 10.0
    vein = marble_veins(SIZE, seed)
    ink = np.array([32.0, 62.0, 128.0], np.float32)
    rgb = body * (1.0 - vein[..., None] * 0.55) + ink * (vein[..., None] * 0.55)
    return np.clip(rgb, 0, 255).astype(np.uint8), grain, vein


def satin_norcom_blue(seed: int, soft: bool) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Cube 3 is an even powder blue. Cube 4 is the same blue family with cloudy grain."""
    grain = crystal_grain(SIZE, seed, "soft" if soft else "solid")
    vein = np.zeros((SIZE, SIZE), np.float32)
    if soft:
        low = np.array([150.0, 162.0, 176.0], np.float32)
        high = np.array([198.0, 208.0, 220.0], np.float32)
        body = low + (high - low) * grain[..., None]
    else:
        # Tight range so the face reads as one satin color, not cobalt speckle.
        tone = 0.45 + (grain - 0.5) * 0.18
        low = np.array([132.0, 168.0, 206.0], np.float32)
        high = np.array([168.0, 198.0, 230.0], np.float32)
        body = low + (high - low) * tone[..., None]
    return np.clip(body, 0, 255).astype(np.uint8), grain, vein


def thin_cracks(seed: int) -> np.ndarray:
    """Jagged mineral cracks. Thickness stays near one pixel, so they are not contour bands."""
    field = fbm(SIZE, seed, ((_cells(SIZE, 5), 0.5), (_cells(SIZE, 9), 0.3), (_cells(SIZE, 16), 0.2)))
    jitter = fbm(SIZE, seed + 6, ((_cells(SIZE, 18), 0.7), (_cells(SIZE, 28), 0.3)))
    field = _warp(field, seed + 2, SIZE * 0.07)
    field = np.clip(field + (jitter - 0.5) * 0.22, 0.0, 1.0)
    gy, gx = np.gradient(field.astype(np.float32))
    grad = np.sqrt(gx * gx + gy * gy) + 1e-4
    dist = np.abs(field - 0.5) / grad
    mask = np.exp(-((dist / 1.05) ** 2)).astype(np.float32)
    breaker = fbm(SIZE, seed + 11, ((_cells(SIZE, 4), 0.6), (_cells(SIZE, 8), 0.4)))
    keep = np.clip((breaker - 0.40) / 0.34, 0.0, 1.0)
    return np.clip(mask * keep, 0.0, 1.0).astype(np.float32)


def neon_slate_veined(seed: int) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Dark slate with thin cyan-violet cracks. Color is in the albedo only."""
    grain = crystal_grain(SIZE, seed, "veined")
    low = np.array([18.0, 22.0, 34.0], np.float32)
    high = np.array([46.0, 54.0, 74.0], np.float32)
    body = low + (high - low) * grain[..., None]
    vein = thin_cracks(seed + 4)
    tint = fbm(SIZE, seed + 33, ((_cells(SIZE, 6), 1.0),))
    cyan = np.array([36.0, 118.0, 176.0], np.float32)
    violet = np.array([104.0, 68.0, 164.0], np.float32)
    vcol = cyan * (1.0 - tint[..., None]) + violet * tint[..., None]
    strength = 0.78
    rgb = body * (1.0 - vein[..., None] * strength) + vcol * (vein[..., None] * strength)
    return np.clip(rgb, 0, 255).astype(np.uint8), grain, vein


def build_field(kind: str, color: str, seed: int) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    if color == "blue" and kind == "veined":
        return mottled_norcom_blue(seed)
    if color == "blue" and kind == "solid":
        return satin_norcom_blue(seed, soft=False)
    if color == "blue" and kind == "soft":
        return satin_norcom_blue(seed, soft=True)
    if color == "slate" and kind == "veined":
        return neon_slate_veined(seed)
    grain = crystal_grain(SIZE, seed, kind)
    low, high, vein_color = stone_colors(color)
    body = low + (high - low) * grain[..., None]
    if kind in ("veined", "glow"):
        vein = marble_veins(SIZE, seed)
        # Slate veins stay dim in the albedo. Only the black marble emits.
        if color == "slate":
            strength = 0.42
        elif kind == "glow":
            strength = 0.9
        else:
            strength = 0.72
        rgb = body * (1.0 - vein[..., None] * strength) + vein_color * (vein[..., None] * strength)
    else:
        vein = np.zeros((SIZE, SIZE), np.float32)
        rgb = body
    return np.clip(rgb, 0, 255).astype(np.uint8), grain, vein


def normals_and_spec(
    kind: str, grain: np.ndarray, vein: np.ndarray, glow: bool
) -> tuple[np.ndarray, np.ndarray]:
    # Micro-relief follows the crystals. Veins are a shallow groove, not a ridge map.
    height = (grain - 0.5) * (0.34 if kind == "solid" else 0.18 if kind == "soft" else 0.24)
    height = height - vein * 0.16
    strength = 1.15 if kind == "solid" else 0.72 if kind == "soft" else 0.9
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
    ao = 248.0 - (1.0 - grain) * (10.0 if kind == "solid" else 5.0)
    normal[..., 2] = np.clip(ao, 220, 255).astype(np.uint8)
    normal[..., 3] = np.clip(236.0 + grain * 14.0 - vein * 12.0, 200, 255).astype(np.uint8)

    # Low roughness (smooth satin) with duller F0 on solids and soft so they are not wet plastic.
    # Veins sit slightly smoother and a touch more reflective than the field.
    if kind == "soft":
        smooth = 112.0 + (grain - 0.5) * 8.0
        porosity = 6.0
        f0 = 11.0 + grain * 2.0
    elif kind == "solid":
        smooth = 104.0 + (grain - 0.5) * 10.0
        porosity = 9.0
        f0 = 14.0 + grain * 2.0
    elif kind == "glow":
        smooth = 84.0 + (grain - 0.5) * 8.0
        smooth = smooth * (1.0 - vein) + 128.0 * vein
        porosity = 5.0
        f0 = 12.0 + vein * 4.0
    else:
        smooth = 114.0 + (grain - 0.5) * 8.0 + vein * 14.0
        porosity = 7.0
        f0 = 18.0 + grain * 2.0 + vein * 4.0
    spec = np.zeros((SIZE, SIZE, 4), np.uint8)
    spec[..., 0] = np.clip(smooth, 72, 148).astype(np.uint8)
    spec[..., 1] = np.clip(f0, 10, 32).astype(np.uint8)
    spec[..., 2] = np.clip(porosity + grain * 1.5, 0, 20).astype(np.uint8)
    spec[..., 3] = 255
    if glow:
        # 255 means "no emission" in labPBR. Only the blue vein pixels emit.
        emit = np.clip(40.0 + vein * 200.0, 0, 240)
        alpha = np.where(vein > 0.08, np.clip(emit, 1, 240), 255).astype(np.uint8)
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
        stone = vein <= 0.08
        vein_px = vein > 0.55
        if not np.all(spec[..., 3][stone] == 255):
            raise SystemExit(f"{name} stone emits; it must stay unlit")
        if int(vein_px.sum()) < 12 or int(spec[..., 3][vein_px].min()) < 1:
            raise SystemExit(f"{name} veins do not emit")
        if int(spec[..., 3][vein_px].max()) >= 255:
            raise SystemExit(f"{name} vein emission was stored as the ignored alpha")
        glow_frac = float(np.mean(spec[..., 3] != 255))
        if glow_frac > 0.12 or glow_frac < 0.05:
            raise SystemExit(f"{name} glow coverage {glow_frac:.3f} is outside the thin shared stroke")
        vein_mean = rgb[vein_px].mean(axis=0)
        if not (vein_mean[2] > vein_mean[1] + 40 and vein_mean[2] > vein_mean[0] + 40):
            raise SystemExit(f"{name} glowing veins are not blue: {vein_mean}")
    else:
        if not np.all(spec[..., 3] == 255):
            raise SystemExit(f"{name} should not emit")
    if kind in ("veined", "glow"):
        cover = float((vein > 0.45).mean())
        if cover < 0.015 or cover > 0.12:
            raise SystemExit(f"{name} vein coverage {cover:.3f} is out of range")
    else:
        if float(vein.max()) > 0:
            raise SystemExit(f"{name} solid has veins")
    small = Image.fromarray(rgb, "RGB").resize((16, 16), Image.Resampling.BOX)
    std = float(np.asarray(small).std())
    floor = 3.2 if kind == "soft" else 4.5
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


LABELS = {
    "veined_classic_white": "Veined White",
    "veined_norcom_blue": "Veined Blue",
    "veined_neon_slate": "Veined Slate",
    "solid_classic_white": "Solid White",
    "solid_norcom_blue": "Solid Blue",
    "solid_neon_slate": "Solid Slate",
    "soft_classic_white": "Soft White",
    "soft_norcom_blue": "Soft Blue",
    "soft_neon_slate": "Soft Slate",
    "veined_black_blue_glow": "Black Blue-Glow",
}


def _sheet(images: list[tuple[str, Image.Image]], title: str, path: Path, cols: int = 5) -> None:
    cell = 160
    label_h = 22
    title_h = 36
    rows = (len(images) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell + 16, title_h + rows * (cell + label_h) + 8), (14, 16, 20))
    draw = ImageDraw.Draw(sheet)
    draw.text((12, 10), title, fill=(236, 236, 236))
    for index, (label, tile) in enumerate(images):
        col = index % cols
        row = index // cols
        x = 8 + col * cell
        y = title_h + row * (cell + label_h)
        shown = tile.resize((cell - 12, cell - 12), Image.Resampling.NEAREST)
        sheet.paste(shown, (x, y))
        draw.text((x, y + cell - 10), label, fill=(210, 214, 220))
    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(path, optimize=True)


def contact_sheet(images: list[tuple[str, np.ndarray, np.ndarray]]) -> None:
    albedos = []
    normals = []
    for name, albedo, _spec in images:
        label = LABELS[name]
        albedos.append((label, Image.fromarray(albedo, "RGB")))
        pixels = np.array(Image.open(PACK / "assets" / NS / "textures" / "block" / f"{name}_n.png"))
        view = np.stack([pixels[..., 0], pixels[..., 1], pixels[..., 2]], axis=-1)
        normals.append((label, Image.fromarray(view, "RGB")))
    _sheet(albedos, "NORCOM marble HQ v2 — 10 base albedos", PREVIEW)
    glow_name = "veined_black_blue_glow"
    spec_img = np.array(Image.open(PACK / "assets" / NS / "textures" / "block" / f"{glow_name}_s.png"))
    glow = np.zeros((SIZE, SIZE, 3), np.uint8)
    lit = spec_img[..., 3] != 255
    glow[..., 2] = np.where(lit, 255, 12).astype(np.uint8)
    glow[..., 0] = np.where(lit, 24, 12).astype(np.uint8)
    glow[..., 1] = 12
    detail = normals + [(LABELS[glow_name] + " glow", Image.fromarray(glow, "RGB"))]
    _sheet(detail, "Normals, plus black-marble vein glow only", SHEET, cols=5)


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


FIX_BASES = (
    "veined_norcom_blue",
    "solid_norcom_blue",
    "soft_norcom_blue",
    "veined_neon_slate",
)


def fix_open_colors() -> None:
    """Replace only the open blue and neon-slate tiles. Leave passing textures byte-for-byte."""
    tex_dir = PACK / "assets" / NS / "textures" / "block"
    before = {path.name: sha256(path) for path in tex_dir.glob("*.png")}
    by_name = {base: (kind, color, seed) for base, kind, color, seed in FAMILIES}
    previews = []
    for base in FIX_BASES:
        kind, color, seed = by_name[base]
        rgb, grain, vein = build_field(kind, color, seed)
        glow = False
        normal, spec = normals_and_spec(kind, grain, vein, glow=glow)
        if np.any(spec[..., 3] != 255):
            raise SystemExit(f"{base} must not emit")
        if color == "blue" and rgb[..., 2].mean() <= rgb[..., 1].mean():
            raise SystemExit(f"{base} reads green")
        save_png(tex_dir / f"{base}.png", rgb)
        save_png(tex_dir / f"{base}_n.png", normal)
        save_png(tex_dir / f"{base}_s.png", spec)
        print(
            f"{base}: mean {rgb.reshape(-1, 3).mean(0).round(1).tolist()} "
            f"std {rgb.std():.1f} smooth {int(spec[..., 0].mean())} f0 {int(spec[..., 1].mean())}"
        )
    for name, digest in before.items():
        stem = name.replace("_n.png", "").replace("_s.png", "").replace(".png", "")
        if stem in FIX_BASES:
            continue
        if sha256(tex_dir / name) != digest:
            raise SystemExit(f"passing texture changed: {name}")
    lines = []
    for path in sorted(PACK.rglob("*")):
        if path.is_file() and path.name != "SHA256SUMS.txt":
            lines.append(f"{sha256(path)}  {path.relative_to(PACK).as_posix()}")
    (PACK / "SHA256SUMS.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    build_zip()
    for base, _kind, _color, _seed in FAMILIES:
        albedo = np.asarray(Image.open(tex_dir / f"{base}.png").convert("RGB"))
        previews.append((base, albedo, albedo))
    contact_sheet(previews)
    print(f"zip {ZIP_PATH} {sha256(ZIP_PATH)}")


if __name__ == "__main__":
    import sys

    if len(sys.argv) > 1 and sys.argv[1] == "--fix-blue-slate":
        fix_open_colors()
    else:
        main()
