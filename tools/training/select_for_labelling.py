r"""Picks the training samples worth labelling: where the two cameras disagree or the counting model is unsure.

The counting machine (own model) and the reference camera (UGREEN, COCO model) count the same road. Their passages
are paired by time and direction (the clock offset between them is estimated per day). A sample is a candidate if
its passage
- was counted by only one camera, or with a different class by the other one,
- belongs to a weak class (motorcycle, truck, bus, tractor, trailer, bicycle, e-scooter, van), or
- was collected because the counting model was unsure (class wavers, unsure detection, not counted).
Agreeing ordinary cars are left out: the model already knows them.

    .venv-labelstudio\Scripts\python select_for_labelling.py --from 2026-10-23T09:10 [--to ...] [--limit 50]
                                                            [--out review-<time>.json]

Writes the list for review_samples.py (and the passages only the reference camera counted, which have no sample
and can be cut from recordings) into datasets/review in the private repository. Needs sqlcmd and both databases on
this PC (TrafficCounter after a sync, TrafficCounter_Window).
"""

import argparse
import bisect
import json
import sqlite3
import statistics
import subprocess
from datetime import datetime, timedelta
from pathlib import Path

from labelstudio_common import datasets_root

WEAK = {"Motorcycle", "Truck", "Bus", "Agricultural", "Trailer", "Bicycle", "EScooter", "Van"}
CLASS_NAMES = {0: "Unknown", 1: "Car", 2: "Van", 3: "Truck", 4: "Bus", 5: "Motorcycle", 6: "Bicycle", 7: "Agricultural", 8: "EScooter"}
UNSURE_REASONS = ("class wavers", "unsure detection", "not counted", "recognised late")


def passages(database: str, start: datetime, end: datetime, machine: str | None) -> list[dict]:
    """Passages in local time from one database via sqlcmd."""
    # Local times get their offset (summer or winter time) from SQL Server.
    local = "cast('{}' as datetime2) at time zone 'W. Europe Standard Time'"
    where = f"p.PassedAt >= {local.format(start.isoformat())} and p.PassedAt < {local.format(end.isoformat())}"
    join = ""
    if machine:
        join = "join OperatingPeriod o on o.Id = p.OperatingPeriodId"
        where += f" and o.MachineName = '{machine}'"
    query = ("set nocount on; select convert(varchar(23), cast(p.PassedAt as datetimeoffset) at time zone "
             f"'W. Europe Standard Time', 121), p.Direction, p.VehicleClass from VehiclePassage p {join} where {where}")
    out = subprocess.run(["sqlcmd", "-S", "localhost", "-d", database, "-W", "-h", "-1", "-s", "|", "-Q", query],
                         capture_output=True, text=True, check=True).stdout
    rows = []
    for line in out.splitlines():
        parts = line.strip().split("|")
        if len(parts) == 3 and parts[0][:2] == "20":
            rows.append({"t": datetime.strptime(parts[0], "%Y-%m-%d %H:%M:%S.%f"), "dir": int(parts[1]),
                         "cls": CLASS_NAMES.get(int(parts[2]), "Unknown")})
    return sorted(rows, key=lambda r: r["t"])


def already_labelled(root: Path) -> set[str]:
    """Samples whose line image has a label already, in Label Studio or from review_samples.py."""
    done = set()
    db = root / "labelstudio" / "data" / "label_studio.sqlite3"
    if db.exists():
        con = sqlite3.connect(f"file:{db.as_posix()}?mode=ro", uri=True)
        for (data,) in con.execute("select t.data from task t join task_completion c on c.task_id = t.id where c.was_cancelled = 0"):
            d = json.loads(data)
            if d.get("view") == "line":
                done.add(d.get("sample"))
    labels = root / "review" / "labels.json"
    if labels.exists():
        done |= {key.rsplit("/", 1)[0] for key in json.loads(labels.read_text(encoding="utf-8"))}
    return done


def pair(own: list[dict], ref: list[dict], window: float = 1.5) -> tuple[list, list, list]:
    """(matched pairs, own only, reference only) after removing the per-day clock offset."""
    times = [r["t"] for r in ref]
    offsets: dict = {}
    for o in own:
        i = bisect.bisect_left(times, o["t"] - timedelta(seconds=5))
        near = [(r["t"] - o["t"]).total_seconds() for r in ref[i:i + 10]
                if r["dir"] == o["dir"] and abs((r["t"] - o["t"]).total_seconds()) <= 5]
        if near:
            offsets.setdefault(o["t"].date(), []).append(min(near, key=abs))
    offset = {day: statistics.median(v) for day, v in offsets.items()}
    used, pairs, own_only = set(), [], []
    for o in own:
        shift = offset.get(o["t"].date(), 0.0)
        i = bisect.bisect_left(times, o["t"] + timedelta(seconds=shift - window))
        candidates = [j for j in range(i, min(len(ref), i + 12))
                      if j not in used and ref[j]["dir"] == o["dir"]
                      and abs((ref[j]["t"] - o["t"]).total_seconds() - shift) <= window]
        if candidates:
            j = min(candidates, key=lambda k: abs((ref[k]["t"] - o["t"]).total_seconds() - shift))
            used.add(j)
            pairs.append((o, ref[j]))
        else:
            own_only.append(o)
    ref_only = [r for j, r in enumerate(ref) if j not in used]
    return pairs, own_only, ref_only


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--from", dest="start", required=True, help="local time, e.g. 2026-10-23T09:10")
    parser.add_argument("--to", dest="end", help="local time; default now")
    parser.add_argument("--machine", required=True, help="counting machine (MachineName) of the counting camera in the main database")
    parser.add_argument("--limit", type=int, default=50)
    parser.add_argument("--out", help="file name in datasets/review")
    args = parser.parse_args()

    start = datetime.fromisoformat(args.start)
    end = datetime.fromisoformat(args.end) if args.end else datetime.now()
    own = passages("TrafficCounter", start, end, args.machine)
    ref = passages("TrafficCounter_Window", start, end, None)
    pairs, own_only, ref_only = pair(own, ref)
    mismatch = [(o, r) for o, r in pairs if o["cls"] != r["cls"]]
    print(f"{len(own)} passages counting camera, {len(ref)} reference; {len(pairs)} paired, {len(mismatch)} with "
          f"different class, {len(own_only)} only counting camera, {len(ref_only)} only reference")

    # Why each counting-camera passage is interesting, keyed by its time.
    flags: dict[datetime, list[str]] = {}
    for o in own_only:
        flags.setdefault(o["t"], []).append("only this camera")
    for o, r in mismatch:
        flags.setdefault(o["t"], []).append(f"reference says {r['cls']}")

    root = datasets_root()
    done = already_labelled(root)
    candidates = []
    for meta in (root / "inbox").glob("*/*/sample.json"):
        if f"{meta.parent.parent.name}/{meta.parent.name}" in done or not (meta.parent / "line.jpg").exists():
            continue
        sample = json.loads(meta.read_text(encoding="utf-8-sig"))
        t = datetime.fromisoformat(sample["Time"]).replace(tzinfo=None)
        if not start <= t < end:
            continue
        reasons = []
        near = [k for k in flags if abs((k - t).total_seconds()) <= 2]
        for k in near:
            reasons += flags[k]
        if sample.get("Class") in WEAK:
            reasons.append(f"weak class {sample['Class']}")
        if any(u in " ".join(sample.get("Reasons") or []) for u in UNSURE_REASONS):
            reasons.append("model unsure: " + ", ".join(sample.get("Reasons") or []))
        if not reasons:
            continue
        score = (3 if near else 0) + (2 if sample.get("Class") in WEAK else 0) + (1 if sample.get("Lighting") != "Day" else 0)
        candidates.append({"sample": f"{meta.parent.parent.name}/{meta.parent.name}", "view": "line",
                           "time": t.isoformat(timespec="seconds"), "class": sample.get("Class"),
                           "lighting": sample.get("Lighting"), "why": reasons, "score": score})
    candidates.sort(key=lambda c: (-c["score"], c["time"]))
    chosen = candidates[:args.limit]

    out_dir = root / "review"
    out_dir.mkdir(parents=True, exist_ok=True)
    out = out_dir / (args.out or f"review-{datetime.now():%Y%m%d-%H%M%S}.json")
    out.write_text(json.dumps({
        "from": start.isoformat(), "to": end.isoformat(), "items": chosen,
        "reference_only": [{"time": r["t"].isoformat(timespec="milliseconds"), "direction": r["dir"], "class": r["cls"]}
                           for r in ref_only],
    }, indent=2), encoding="utf-8")
    print(f"{len(candidates)} candidate samples, {len(chosen)} written to {out}; "
          f"{len(ref_only)} passages only the reference counted (no sample; cut from recordings if covered)")


if __name__ == "__main__":
    main()
