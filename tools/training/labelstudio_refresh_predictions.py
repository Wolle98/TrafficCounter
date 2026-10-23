r"""Rebuilds the proposed boxes of imported but not yet labelled Label Studio tasks from the samples.

After `TrafficCounter.Regression --prelabel <inbox> --model <teacher.onnx>` the samples carry a stronger model's boxes
(TeacherBoxes). Tasks imported earlier still propose the counting model's boxes; this script replaces their
predictions in Label Studio's database with the current proposals (counting model's boxes, motor vehicles tightened
by the teacher, see labelstudio_tasks.tightened). Labelled tasks are left untouched. Label Studio must not be running;
a backup of the database is written next to it first.

    .venv-labelstudio\Scripts\python labelstudio_refresh_predictions.py [--min-confidence 0.2] [--dry-run]
"""

import argparse
import json
import shutil
import socket
import sqlite3
from datetime import datetime

from labelstudio_common import datasets_root
from labelstudio_tasks import boxes_to_results


def label_studio_running(port: int = 8089) -> bool:
    with socket.socket() as s:
        s.settimeout(0.5)
        return s.connect_ex(("127.0.0.1", port)) == 0


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--min-confidence", type=float, default=0.2, help="proposals below are left out")
    parser.add_argument("--dry-run", action="store_true", help="only count what would change")
    args = parser.parse_args()

    if label_studio_running():
        raise SystemExit("Label Studio is running; stop it first.")

    root = datasets_root()
    db_path = root / "labelstudio" / "data" / "label_studio.sqlite3"
    if not args.dry_run:
        shutil.copy2(db_path, db_path.with_name(f"{db_path.name}.bak-{datetime.now():%Y%m%d-%H%M%S}"))

    con = sqlite3.connect(db_path)
    rows = con.execute(
        "select t.id, t.data, (select count(*) from task_completion c where c.task_id = t.id) from task t").fetchall()
    updated = skipped_labelled = no_teacher = 0
    for task_id, data, labelled in rows:
        d = json.loads(data)
        if labelled:
            skipped_labelled += 1
            continue
        path = root / "inbox" / d.get("sample", "") / "sample.json"
        if not path.exists():
            continue
        sample = json.loads(path.read_text(encoding="utf-8-sig"))
        image = next((i for i in sample.get("Images") or [] if i and i["File"].split(".")[0] == d.get("view")), None)
        if image is None or "TeacherBoxes" not in image:
            no_teacher += 1
            continue
        results, score = boxes_to_results(image, args.min_confidence)
        if not args.dry_run:
            con.execute("update prediction set result = ?, score = ?, model_version = ? where task_id = ?",
                        (json.dumps(results), score, f"counting-service+{sample.get('TeacherModel')}", task_id))
        updated += 1
    if not args.dry_run:
        con.commit()
    print(f"{'would update' if args.dry_run else 'updated'} {updated} tasks; {skipped_labelled} labelled ones left alone; "
          f"{no_teacher} without teacher boxes")


if __name__ == "__main__":
    main()
