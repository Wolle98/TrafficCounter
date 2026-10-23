r"""Fine-tunes a YOLO model on the labelled training samples and exports it to ONNX (stage 3, ADR-0018, ADR-0039).

Reads the submitted annotations straight from Label Studio's database (read-only, Label Studio may keep running),
builds a YOLO dataset in the private repository and trains on the GPU. Images taken while a hand-counted test
recording ran (recordings with a .gt.csv whose tags contain --test-tag, default "nc400") are left out, so the
regression on those recordings stays a fair test. All views of one sample go to the same split.

    .venv\Scripts\python train_model.py [--name tc-v1] [--base yolo11s.pt] [--epochs 100] [--batch 16]
                                        [--imgsz 640] [--val-share 0.15] [--test-tag nc400] [--pixel-patches 0.5]
                                        [--dry-run]

Writes datasets/training/<name> (images, labels, data.yaml), the run to models/runs/<name> and the exported model
to models/<name>-640x192.onnx, all in the private repository.
"""

import argparse
import hashlib
import json
import os
import shutil
import sqlite3
from datetime import datetime
from pathlib import Path

from labelstudio_common import CLASSES, private_root
from labelstudio_to_yolo import yolo_lines


def test_windows(root: Path, tag: str) -> list[tuple[str, datetime, datetime]]:
    """Start and end of the hand-counted recordings used as test set."""
    windows = []
    for gt in sorted((root / "recordings").glob("*.gt.csv")):
        meta = gt.with_name(gt.name.removesuffix(".gt.csv") + ".json")
        if not meta.exists():
            continue
        info = json.loads(meta.read_text(encoding="utf-8-sig"))
        if tag in (info.get("Tags") or []) and info.get("Started") and info.get("Ended"):
            windows.append((meta.name.removesuffix(".mp4.json"), datetime.fromisoformat(info["Started"]),
                            datetime.fromisoformat(info["Ended"])))
    return windows


def annotations(root: Path) -> list[tuple[dict, dict]]:
    """(task data, latest annotation) of every labelled image: Label Studio and review_samples.py."""
    db = root / "datasets" / "labelstudio" / "data" / "label_studio.sqlite3"
    con = sqlite3.connect(f"file:{db.as_posix()}?mode=ro", uri=True)
    rows = con.execute(
        "select t.id, t.data, c.result, c.updated_at from task t join task_completion c on c.task_id = t.id "
        "where c.was_cancelled = 0 order by c.updated_at").fetchall()
    latest = {}
    for task_id, data, result, _ in rows:
        d = json.loads(data)
        latest[f"{d['sample']}/{d['view']}"] = (d, {"result": json.loads(result)})
    # Labels from review_samples.py (pixel boxes) win over Label Studio for the same image; skipped images are left out.
    review = root / "datasets" / "review" / "labels.json"
    if review.exists():
        for key, label in json.loads(review.read_text(encoding="utf-8")).items():
            sample, view = key.rsplit("/", 1)
            if label.get("status") != "ok":
                latest.pop(key, None)
                continue
            w, h = label["width"], label["height"]
            result = [{"type": "rectanglelabels", "value": {
                "x": 100 * b["x"] / w, "y": 100 * b["y"] / h, "width": 100 * b["w"] / w, "height": 100 * b["h"] / h,
                "rotation": 0, "rectanglelabels": [b["class"]]}} for b in label["boxes"]]
            latest[key] = ({"sample": sample, "view": view}, {"result": result})
    return list(latest.values())


def split_of(sample: str, val_share: float) -> str:
    """Stable split per sample, so entry, line and exit image of one vehicle never end up on both sides."""
    h = int(hashlib.sha1(sample.encode("utf-8")).hexdigest()[:8], 16) / 0xFFFFFFFF
    return "val" if h < val_share else "train"


def build_dataset(root: Path, out: Path, val_share: float, tag: str) -> dict:
    windows = test_windows(root, tag)
    counts = {"train": 0, "val": 0, "test-excluded": 0, "missing": 0}
    boxes = {c: 0 for c in CLASSES}
    if out.exists():
        shutil.rmtree(out)
    for split in ("train", "val"):
        (out / "images" / split).mkdir(parents=True)
        (out / "labels" / split).mkdir(parents=True)
    for data, annotation in annotations(root):
        sample_dir = root / "datasets" / "inbox" / data["sample"]
        image = sample_dir / f"{data['view']}.jpg"
        meta = sample_dir / "sample.json"
        if not image.exists() or not meta.exists():
            counts["missing"] += 1
            continue
        time = datetime.fromisoformat(json.loads(meta.read_text(encoding="utf-8-sig"))["Time"])
        if any(start <= time <= end for _, start, end in windows):
            counts["test-excluded"] += 1
            continue
        split = split_of(data["sample"], val_share)
        stem = f"{data['sample'].replace('/', '_')}_{data['view']}"
        lines = yolo_lines(annotation)
        try:
            os.link(image, out / "images" / split / f"{stem}.jpg")
        except OSError:
            shutil.copyfile(image, out / "images" / split / f"{stem}.jpg")
        (out / "labels" / split / f"{stem}.txt").write_text("".join(line + "\n" for line in lines), encoding="utf-8")
        counts[split] += 1
        for line in lines:
            boxes[CLASSES[int(line.split()[0])]] += 1
    (out / "data.yaml").write_text(
        f"path: {out.as_posix()}\ntrain: images/train\nval: images/val\nnames:\n"
        + "".join(f"  {i}: {name}\n" for i, name in enumerate(CLASSES)), encoding="utf-8")
    print(f"test recordings left out ({tag}): {', '.join(w[0] for w in windows) or 'none'}")
    print(f"images: {counts}")
    print(f"boxes: {boxes}")
    return counts


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--name", default="tc-v1")
    parser.add_argument("--base", default="yolo11s.pt", help="pre-trained weights to start from")
    parser.add_argument("--epochs", type=int, default=100)
    parser.add_argument("--batch", type=int, default=16)
    parser.add_argument("--imgsz", type=int, default=640)
    parser.add_argument("--val-share", type=float, default=0.15)
    parser.add_argument("--test-tag", default="nc400", help="recordings with this tag are the test set")
    parser.add_argument("--pixel-patches", type=float, default=0.0,
                        help="probability of random pixelation patches per training image (pixel_patches.py)")
    parser.add_argument("--dry-run", action="store_true", help="only build the dataset")
    args = parser.parse_args()

    root = private_root()
    dataset = root / "datasets" / "training" / args.name
    build_dataset(root, dataset, args.val_share, args.test_tag)
    if args.dry_run:
        return

    from ultralytics import YOLO  # imported late: slow, and not needed for --dry-run

    if args.pixel_patches > 0:
        import pixel_patches
        pixel_patches.install(args.pixel_patches)

    runs = root / "models" / "runs"
    model = YOLO(args.base)
    model.train(data=str(dataset / "data.yaml"), epochs=args.epochs, batch=args.batch, imgsz=args.imgsz,
                device=0, project=str(runs), name=args.name, exist_ok=True, patience=30, plots=True)
    best = runs / args.name / "weights" / "best.pt"
    exported = Path(YOLO(str(best)).export(format="onnx", imgsz=(192, 640), opset=17, simplify=True))
    target = root / "models" / f"{args.name}-640x192.onnx"
    shutil.copyfile(exported, target)
    print(f"model: {target}")


if __name__ == "__main__":
    main()
