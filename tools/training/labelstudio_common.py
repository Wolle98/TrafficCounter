"""Shared settings of the Label Studio scripts (ADR-0026 samples, labelling rules ADR-0039)."""

import os
from pathlib import Path

# Label values in Label Studio, in the order of the YOLO class ids of the own model. Must match label-config.xml.
# EScooter is labelled apart from Bicycle (easier to learn) and counted as a class of its own (ADR-0054).
CLASSES = ["Car", "Van", "Truck", "Bus", "Motorcycle", "Bicycle", "Agricultural", "Trailer", "EScooter"]


def private_root() -> Path:
    """The private repository (images and labels never go into the public one)."""
    root = os.environ.get("TRAFFICCOUNTER_PRIVATE")
    if not root:
        raise SystemExit("Set TRAFFICCOUNTER_PRIVATE to the private repository.")
    return Path(root)


def datasets_root() -> Path:
    """Document root for Label Studio's local file serving; task image URLs are relative to it."""
    return private_root() / "datasets"


def image_url(relative: str) -> str:
    """URL under which Label Studio serves a file below datasets_root()."""
    return "/data/local-files/?d=" + relative.replace("\\", "/")


def image_path(url: str) -> Path:
    """Local file of an image URL from image_url()."""
    marker = "?d="
    if marker not in url:
        raise ValueError(f"Not a local-files URL: {url}")
    return datasets_root() / url.split(marker, 1)[1]
