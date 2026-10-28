r"""Label Studio tasks to review labelled cars that may be small vans (high-roof estates, ADR-0055).

Small vans such as Opel Combo or VW Caddy are labelled as Van since ADR-0055; before, many were labelled as Car.
This script picks the labelled images with a Car box that is probably one: the model also sees a van there (a Van
box of at least --min-confidence overlapping the Car box), or another image of the same vehicle is labelled Van.
Each becomes a new task whose proposals are the current labels, with the suspected boxes listed in "reasons". Once
submitted, the new annotation is the latest of that image and wins in train_model.py; a skipped task keeps the old.

    .venv\Scripts\python labelstudio_small_vans.py [--model <best.pt>] [--min-confidence 0.15] [--limit 400]

Writes datasets/labelstudio/tasks-<time>-smallvans.json in the private repository; import it in Label Studio.
"""

import argparse
import json
import sqlite3
from datetime import datetime
from pathlib import Path

from labelstudio_common import datasets_root, private_root


def latest_labels(db: Path) -> dict[str, tuple[dict, list]]:
    """(task data, result) of the latest submitted annotation per image, as train_model.py reads them."""
    con = sqlite3.connect(f"file:{db.as_posix()}?mode=ro", uri=True)
    rows = con.execute(
        "select t.data, c.result from task t join task_completion c on c.task_id = t.id "
        "where c.was_cancelled = 0 order by c.updated_at").fetchall()
    latest = {}
    for data, result in rows:
        d = json.loads(data)
        latest[f"{d['sample']}/{d['view']}"] = (d, json.loads(result))
    return latest


def boxes(result: list) -> list[tuple[str, float, float, float, float]]:
    """(class, x0, y0, x1, y1) in percent of the image."""
    out = []
    for r in result:
        if r.get("type") == "rectanglelabels":
            v = r["value"]
            out.append((v["rectanglelabels"][0], v["x"], v["y"], v["x"] + v["width"], v["y"] + v["height"]))
    return out


def iou(a, b) -> float:
    x0, y0, x1, y1 = max(a[0], b[0]), max(a[1], b[1]), min(a[2], b[2]), min(a[3], b[3])
    inter = max(0.0, x1 - x0) * max(0.0, y1 - y0)
    union = (a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - inter
    return inter / union if union > 0 else 0.0


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--model", default=str(private_root() / "models" / "runs" / "tc-v6p" / "weights" / "best.pt"))
    parser.add_argument("--min-confidence", type=float, default=0.15)
    parser.add_argument("--limit", type=int, default=400)
    args = parser.parse_args()

    from ultralytics import YOLO  # only here: the venv of Label Studio has no Ultralytics

    root = datasets_root()
    latest = latest_labels(root / "labelstudio" / "data" / "label_studio.sqlite3")
    van_samples = {d["sample"] for d, result in latest.values() if any(c == "Van" for c, *_ in boxes(result))}
    with_cars = {key: value for key, value in latest.items() if any(c == "Car" for c, *_ in boxes(value[1]))}

    model = YOLO(args.model)
    van_index = next(i for i, name in model.names.items() if name == "Van")
    picked = []
    for key, (data, result) in with_cars.items():
        image = root / "inbox" / data["sample"] / f"{data['view']}.jpg"
        if not image.exists():
            continue
        prediction = model.predict(str(image), conf=args.min_confidence, classes=[van_index], verbose=False)[0]
        h, w = prediction.orig_shape
        vans = [(100 * x0 / w, 100 * y0 / h, 100 * x1 / w, 100 * y1 / h, float(c))
                for (x0, y0, x1, y1), c in zip(prediction.boxes.xyxy.tolist(), prediction.boxes.conf.tolist())]
        reasons = []
        for cls, *box in boxes(result):
            if cls != "Car":
                continue
            best = max((v[4] for v in vans if iou(box, v[:4]) >= 0.5), default=None)
            if best is not None:
                reasons.append(f"model sees a van ({best:.2f}) in the car at x {box[0]:.0f}-{box[2]:.0f} %")
        if data["sample"] in van_samples and not reasons:
            reasons.append("another image of this vehicle is labelled Van")
        if reasons:
            picked.append((max((float(r.split("(")[1].split(")")[0]) for r in reasons if "(" in r), default=0.0), key, data, result, reasons))

    picked.sort(key=lambda p: -p[0])
    tasks = [{
        "data": {**data, "reasons": ["small van review (ADR-0055)", *reasons]},
        "predictions": [{"model_version": "current labels", "result": result}],
    } for _, _, data, result, reasons in picked[:args.limit]]
    out = root / "labelstudio" / f"tasks-{datetime.now():%Y%m%d-%H%M%S}-smallvans.json"
    out.write_text(json.dumps(tasks, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"{len(with_cars)} labelled images with a car, {len(picked)} suspected, {len(tasks)} tasks written to {out}")


if __name__ == "__main__":
    main()
