"""Random pixelation patches for training (ADR-0026 addendum 2026-10-18).

All training images are anonymised: number plates and heads are pixelated. In operation the model sees clean
frames. So that it does not learn "pixelated block = vehicle part", the training pixelates random rectangles
anywhere in the image (road, background, vehicles), with block sizes like those of the anonymisation after the
resize to the model input. Hooked into Ultralytics' training transforms by install(); validation is untouched.
"""

import random

import cv2
import numpy as np


class PixelPatches:
    """Pixelates 1 to `max_patches` random rectangles of an image with probability `p`."""

    def __init__(self, p: float = 0.5, max_patches: int = 4, min_share: float = 0.03, max_share: float = 0.2,
                 blocks: tuple[int, int] = (3, 6)):
        self.p = p
        self.max_patches = max_patches
        self.min_share = min_share
        self.max_share = max_share
        self.blocks = blocks

    def __call__(self, labels: dict) -> dict:
        if random.random() >= self.p:
            return labels
        img: np.ndarray = labels["img"]
        h, w = img.shape[:2]
        for _ in range(random.randint(1, self.max_patches)):
            pw = max(2, int(w * random.uniform(self.min_share, self.max_share)))
            ph = max(2, int(h * random.uniform(self.min_share, self.max_share)))
            x, y = random.randint(0, max(0, w - pw)), random.randint(0, max(0, h - ph))
            block = random.randint(*self.blocks)
            region = img[y:y + ph, x:x + pw]
            small = cv2.resize(region, (max(1, pw // block), max(1, ph // block)), interpolation=cv2.INTER_AREA)
            img[y:y + ph, x:x + pw] = cv2.resize(small, (pw, ph), interpolation=cv2.INTER_NEAREST)
        labels["img"] = img
        return labels


def install(p: float) -> None:
    """Appends PixelPatches to the training transforms of every YOLO dataset built from now on."""
    from ultralytics.data import dataset

    original = dataset.v8_transforms

    def patched(*args, **kwargs):
        transforms = original(*args, **kwargs)
        transforms.append(PixelPatches(p))
        return transforms

    dataset.v8_transforms = patched
