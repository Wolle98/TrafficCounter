"""Turns training samples (datasets/inbox, ADR-0026) into Label Studio tasks with the model's boxes as proposals.

Every image of a sample (entry, line, exit) becomes one task. The boxes from sample.json are pre-annotations, so
labelling means correcting rather than drawing. Bicycles and e-scooters come first (also the ones cut from hand
counts), then day, dusk and night in turn, each with the hardest cases first: not counted, wavering class. Samples already written to an earlier task file are skipped (list in exported.txt), so the
script can run again for new samples.

    .venv-labelstudio\\Scripts\\python labelstudio_tasks.py [--limit 300] [--views line] [--min-confidence 0.2] [--class EScooter]

Writes datasets/labelstudio/tasks-<time>.json in the private repository; import it in Label Studio (Import button).
"""

import argparse
import json
from datetime import datetime
from pathlib import Path

from labelstudio_common import CLASSES, datasets_root, image_url

VIEWS = ["entry", "line", "exit"]


def priority(sample: dict) -> tuple:
    """Smaller sorts first: the cases the current model gets wrong are worth the most labelling time."""
    reasons = " ".join(sample.get("Reasons") or [])
    lighting = sample.get("Lighting") or ""
    return (
        0 if "not counted" in reasons else 1,
        {"Night": 0, "Dusk": 1}.get(lighting, 2),
        0 if "class wavers" in reasons or "rare class" in reasons else 1,
        1 if reasons.strip() == "random sample" else 0,
        sample.get("Time") or "",
    )


def is_two_wheeler(sample: dict) -> bool:
    """Bicycles and e-scooters, from the service or cut from hand counts: the scarcest class, always first."""
    reasons = " ".join(sample.get("Reasons") or [])
    return "hand count" in reasons or "rare class Bicycle" in reasons or sample.get("Class") in ("Bicycle", "EScooter")


def balanced(samples: list) -> list:
    """Two-wheelers first, then day, dusk and night in turn, each hardest first; so a batch is not all night."""
    first = sorted((s for s in samples if is_two_wheeler(s[2])), key=lambda s: priority(s[2]))
    buckets = {}
    for s in sorted((s for s in samples if not is_two_wheeler(s[2])), key=lambda s: priority(s[2])):
        buckets.setdefault(s[2].get("Lighting") or "", []).append(s)
    queues = [buckets.pop(light, []) for light in ("Day", "Dusk", "Night")] + list(buckets.values())
    rest = []
    while any(queues):
        for queue in queues:
            if queue:
                rest.append(queue.pop(0))
    return first + rest


MOTOR_VEHICLES = {"Car", "Van", "Truck", "Bus"}


def iou(a: dict, b: dict) -> float:
    w = min(a["X"] + a["Width"], b["X"] + b["Width"]) - max(a["X"], b["X"])
    h = min(a["Y"] + a["Height"], b["Y"] + b["Height"]) - max(a["Y"], b["Y"])
    inter = max(0.0, w) * max(0.0, h)
    union = a["Width"] * a["Height"] + b["Width"] * b["Height"] - inter
    return inter / union if union > 0 else 0.0


def tightened(image: dict) -> list:
    """The counting model's boxes, where a car, van, truck or bus is replaced by the teacher's box of the same vehicle.

    The teacher (yolo11x, TrafficCounter.Regression --prelabel) draws motor vehicles tighter, but it runs on the
    anonymised images and misses or invents many of them there (18.10.: 65 % of the labelled motor vehicles found,
    1,080 false boxes on 1,325 images). So it only refines boxes the counting model found; class and confidence stay
    the counting model's. Two-wheelers keep the counting model's box, which is extended to the rider anyway (ADR-0039).
    """
    teacher = [t for t in image.get("TeacherBoxes") or [] if t.get("Class") in MOTOR_VEHICLES]
    boxes, used = [], set()
    for box in image.get("Boxes") or []:
        if box.get("Class") in MOTOR_VEHICLES and teacher:
            score, best = max((iou(box, t), k) for k, t in enumerate(teacher))
            t = teacher[best]
            # Only a tighter box, never a cut-off one: on the anonymised images the teacher often finds just part of
            # a bus or truck (20.10.: a tenth of the replaced bus boxes were at most 65 % as wide).
            if score >= 0.5 and best not in used and t["Width"] >= 0.85 * box["Width"] and t["Height"] >= 0.85 * box["Height"]:
                used.add(best)
                box = {**box, "X": t["X"], "Y": t["Y"], "Width": t["Width"], "Height": t["Height"]}
        boxes.append(box)
    return boxes


def boxes_to_results(image: dict, min_confidence: float, teacher: bool = False) -> tuple[list, float | None]:
    width, height = image["Width"], image["Height"]
    results, confidences = [], []
    # With --teacher only the stronger model's boxes are proposed; it matched the labels much worse (see tightened).
    for index, box in enumerate((image.get("TeacherBoxes") if teacher else None) or tightened(image)):
        label = box.get("Class")
        confidence = box.get("Confidence") or 0
        if label not in CLASSES or confidence < min_confidence:
            continue
        x0, y0 = max(0.0, box["X"]), max(0.0, box["Y"])
        x1, y1 = min(float(width), box["X"] + box["Width"]), min(float(height), box["Y"] + box["Height"])
        if x1 <= x0 or y1 <= y0:
            continue
        confidences.append(confidence)
        results.append({
            "id": f"p{index}",
            "from_name": "label",
            "to_name": "image",
            "type": "rectanglelabels",
            "original_width": width,
            "original_height": height,
            "image_rotation": 0,
            "score": confidence,
            "value": {
                "x": 100 * x0 / width,
                "y": 100 * y0 / height,
                "width": 100 * (x1 - x0) / width,
                "height": 100 * (y1 - y0) / height,
                "rotation": 0,
                "rectanglelabels": [label],
            },
        })
    return results, (sum(confidences) / len(confidences) if confidences else None)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--limit", type=int, default=0, help="at most this many samples (0: all new ones)")
    parser.add_argument("--views", default=",".join(VIEWS), help="images per sample, e.g. 'line' or 'entry,line,exit'")
    parser.add_argument("--min-confidence", type=float, default=0.2, help="proposals below are left out")
    parser.add_argument("--teacher", action="store_true", help="propose the teacher boxes (--prelabel) instead of the counting model's")
    parser.add_argument("--class", dest="vehicle_class", help="only samples of this class, e.g. EScooter (others stay for later)")
    parser.add_argument("--all", action="store_true", help="ignore exported.txt and include samples written before")
    parser.add_argument("--order", choices=["balanced", "hardest"], default="balanced",
                        help="balanced: two-wheelers first, then day/dusk/night in turn; hardest: not counted and night first")
    args = parser.parse_args()

    root = datasets_root()
    inbox = root / "inbox"
    out_dir = root / "labelstudio"
    out_dir.mkdir(parents=True, exist_ok=True)
    exported_file = out_dir / "exported.txt"
    exported = set() if args.all or not exported_file.exists() else set(exported_file.read_text().split())
    views = [v.strip() for v in args.views.split(",") if v.strip()]

    samples = []
    for path in sorted(inbox.glob("*/*/sample.json")):
        name = f"{path.parent.parent.name}/{path.parent.name}"
        sample = json.loads(path.read_text(encoding="utf-8"))
        if name not in exported and (args.vehicle_class is None or sample.get("Class") == args.vehicle_class):
            samples.append((name, path, sample))
    samples = balanced(samples) if args.order == "balanced" else sorted(samples, key=lambda s: priority(s[2]))
    if args.limit:
        samples = samples[: args.limit]

    tasks = []
    for name, path, sample in samples:
        images = {Path(i["File"]).stem: i for i in sample.get("Images") or [] if i}
        for view in views:
            image = images.get(view)
            if image is None or not (path.parent / image["File"]).exists():
                continue  # not counted: no image at the line
            results, score = boxes_to_results(image, args.min_confidence, args.teacher)
            task = {
                "data": {
                    "image": image_url(f"inbox/{name}/{image['File']}"),
                    "sample": name,
                    "view": view,
                    "lighting": sample.get("Lighting") or "",
                    "reasons": ", ".join(sample.get("Reasons") or []),
                    "counted_class": sample.get("Class") or "",
                },
                "predictions": [{"model_version": (sample.get("TeacherModel") if args.teacher else None) or "counting-service", "result": results, **({"score": score} if score else {})}],
            }
            tasks.append(task)

    if not tasks:
        print("No new samples.")
        return

    out = out_dir / f"tasks-{datetime.now():%Y%m%d-%H%M%S}.json"
    out.write_text(json.dumps(tasks, indent=1), encoding="utf-8")
    with exported_file.open("a", encoding="utf-8") as f:
        f.writelines(name + "\n" for name, _, _ in samples)
    print(f"{len(samples)} samples, {len(tasks)} tasks -> {out}")


if __name__ == "__main__":
    main()
