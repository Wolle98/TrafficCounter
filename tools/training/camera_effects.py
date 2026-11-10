"""Motion blur and sensor noise for training (stage 3, ADR-0018).

At night and in the rain the camera exposes longer and amplifies the signal: moving vehicles smear along their way
and the image gets grainy. The labelled samples are mostly sharp daytime frames. So the training blurs the vehicles
horizontally (their direction of travel; the background stays sharp, as with a fixed camera) and adds noise to the
whole image. Hooked into Ultralytics' training transforms by install(); validation is untouched.
"""

import random

import cv2
import numpy as np


def _boxes_xyxy(labels: dict, w: int, h: int) -> np.ndarray:
    """Pixel corners of the label boxes, whatever format the transforms left them in."""
    instances = labels["instances"]
    boxes = instances.bboxes.astype(np.float32).copy()
    if len(boxes) == 0:
        return boxes.reshape(0, 4)
    if instances._bboxes.format == "xywh":
        boxes = np.column_stack([boxes[:, 0] - boxes[:, 2] / 2, boxes[:, 1] - boxes[:, 3] / 2,
                                 boxes[:, 0] + boxes[:, 2] / 2, boxes[:, 1] + boxes[:, 3] / 2])
    elif instances._bboxes.format == "ltwh":
        boxes = np.column_stack([boxes[:, 0], boxes[:, 1], boxes[:, 0] + boxes[:, 2], boxes[:, 1] + boxes[:, 3]])
    if instances.normalized:
        boxes *= np.array([w, h, w, h], dtype=np.float32)
    return boxes


class MotionBlur:
    """With probability `p`, blurs each vehicle box horizontally over 3 to `max_length` pixels."""

    def __init__(self, p: float = 0.3, max_length: int = 11, margin: float = 0.1):
        self.p = p
        self.max_length = max_length
        self.margin = margin

    def __call__(self, labels: dict) -> dict:
        if random.random() >= self.p:
            return labels
        img: np.ndarray = labels["img"]
        h, w = img.shape[:2]
        for x1, y1, x2, y2 in _boxes_xyxy(labels, w, h):
            length = random.randint(3, self.max_length)
            pad = int((x2 - x1) * self.margin) + length
            left, right = max(0, int(x1) - pad), min(w, int(x2) + pad)
            top, bottom = max(0, int(y1)), min(h, int(y2))
            if right - left < 2 or bottom - top < 2:
                continue
            kernel = np.full((1, length), 1.0 / length, dtype=np.float32)
            img[top:bottom, left:right] = cv2.filter2D(img[top:bottom, left:right], -1, kernel,
                                                       borderType=cv2.BORDER_REFLECT)
        labels["img"] = img
        return labels


class SensorNoise:
    """With probability `p`, adds Gaussian noise (sigma 2..`max_sigma` of 255), mostly luminance, a little colour."""

    def __init__(self, p: float = 0.3, max_sigma: float = 12.0, colour_share: float = 0.3):
        self.p = p
        self.max_sigma = max_sigma
        self.colour_share = colour_share

    def __call__(self, labels: dict) -> dict:
        if random.random() >= self.p:
            return labels
        img: np.ndarray = labels["img"]
        h, w = img.shape[:2]
        sigma = random.uniform(2.0, self.max_sigma)
        luminance = np.random.normal(0.0, sigma, (h, w, 1)).astype(np.float32)
        colour = np.random.normal(0.0, sigma * self.colour_share, (h, w, img.shape[2])).astype(np.float32)
        labels["img"] = np.clip(img.astype(np.float32) + luminance + colour, 0, 255).astype(np.uint8)
        return labels


def install(motion_blur: float, noise: float) -> None:
    """Appends MotionBlur and SensorNoise to the training transforms of every YOLO dataset built from now on."""
    from ultralytics.data import dataset

    original = dataset.v8_transforms

    def patched(*args, **kwargs):
        transforms = original(*args, **kwargs)
        if motion_blur > 0:
            transforms.append(MotionBlur(motion_blur))
        if noise > 0:
            transforms.append(SensorNoise(noise))
        return transforms

    dataset.v8_transforms = patched
