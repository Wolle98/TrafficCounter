"""Converts a Label Studio export (JSON) into a YOLO dataset in the private repository.

Export in Label Studio with "JSON" (not "JSON-MIN"), then:

    .venv-labelstudio\\Scripts\\python labelstudio_to_yolo.py <export.json>

Writes datasets/labelled/images/<day>_<sample>_<view>.jpg, labels/<same>.txt (class cx cy w h, normalised) and
classes.txt. Only tasks with a submitted, not skipped annotation are taken; an annotation without boxes is a valid
"no vehicle" image. Running it again with a newer export replaces the files of the same images.
"""

import argparse
import json
import shutil
from pathlib import Path

from labelstudio_common import CLASSES, datasets_root, image_path


def latest_annotation(task: dict) -> dict | None:
    annotations = [a for a in task.get("annotations") or [] if not a.get("was_cancelled")]
    return max(annotations, key=lambda a: a.get("updated_at") or a.get("created_at") or "", default=None)


def yolo_lines(annotation: dict) -> list[str]:
    lines = []
    for result in annotation.get("result") or []:
        if result.get("type") != "rectanglelabels":
            continue
        value = result["value"]
        label = (value.get("rectanglelabels") or [None])[0]
        if label not in CLASSES:
            raise ValueError(f"Unknown label {label!r}; extend CLASSES in labelstudio_common.py")
        if value.get("rotation"):
            raise ValueError("Rotated boxes are not supported; draw axis-parallel boxes")
        # Label Studio stores percent of the image, top-left corner.
        x0 = max(0.0, value["x"] / 100)
        y0 = max(0.0, value["y"] / 100)
        x1 = min(1.0, (value["x"] + value["width"]) / 100)
        y1 = min(1.0, (value["y"] + value["height"]) / 100)
        if x1 <= x0 or y1 <= y0:
            continue
        lines.append(f"{CLASSES.index(label)} {(x0 + x1) / 2:.6f} {(y0 + y1) / 2:.6f} {x1 - x0:.6f} {y1 - y0:.6f}")
    return lines


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("export", type=Path, help="Label Studio JSON export")
    args = parser.parse_args()

    out = datasets_root() / "labelled"
    (out / "images").mkdir(parents=True, exist_ok=True)
    (out / "labels").mkdir(parents=True, exist_ok=True)
    (out / "classes.txt").write_text("\n".join(CLASSES) + "\n", encoding="utf-8")

    tasks = json.loads(args.export.read_text(encoding="utf-8-sig"))
    written = boxes = empty = skipped = 0
    for task in tasks:
        annotation = latest_annotation(task)
        if annotation is None:
            skipped += 1
            continue
        source = image_path(task["data"]["image"])
        stem = f"{task['data']['sample'].replace('/', '_')}_{task['data']['view']}"
        lines = yolo_lines(annotation)
        shutil.copyfile(source, out / "images" / f"{stem}.jpg")
        (out / "labels" / f"{stem}.txt").write_text("".join(line + "\n" for line in lines), encoding="utf-8")
        written += 1
        boxes += len(lines)
        empty += not lines

    print(f"{written} labelled images ({boxes} boxes, {empty} without vehicles), {skipped} tasks without annotation -> {out}")


if __name__ == "__main__":
    main()
