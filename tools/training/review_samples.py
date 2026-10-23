r"""Keyboard-driven review of training samples: confirm or correct the proposed boxes, image by image.

A faster alternative to Label Studio for small, targeted batches (select_for_labelling.py). Shows the line image of a
sample with the proposed boxes (counting model, motor vehicles tightened by the teacher) and the reason it was
picked. Most images only need Enter. Labels go to datasets/review/labels.json in the private repository;
train_model.py reads them next to the Label Studio annotations (a review label wins over Label Studio for the same
image). Labelling rules: ADR-0039 (rider in the two-wheeler's box, trailer as its own box, no pedestrians).

    .venv\Scripts\python review_samples.py <datasets/review/review-....json>

Keys:
    Enter / Space   accept the boxes as shown, next image (no boxes = valid "no vehicle" image)
    Tab             select the next box (or click a box)
    1-9             class of the selected box, or of the next new box: 1 Car 2 Van 3 Truck 4 Bus 5 Motorcycle
                    6 Bicycle 7 Agricultural 8 Trailer 9 EScooter
    Del / x         delete the selected box
    arrows          enlarge the selected box by 4 px on that side (e.g. up: include the rider)
    i j k l         shrink it on the top, left, bottom, right side
    mouse drag      draw a new box; dragging near an edge of the selected box moves that edge
    u               undo the last change on this image
    s               skip (unusable image), b / Backspace back, q / Esc quit (everything is saved)
"""

import argparse
import copy
import json
import sys
from datetime import datetime
from pathlib import Path

import cv2
import numpy as np

from labelstudio_common import CLASSES, datasets_root
from labelstudio_tasks import tightened

COLORS = {"Car": (180, 120, 40), "Van": (180, 180, 60), "Truck": (60, 60, 220), "Bus": (40, 140, 240),
          "Motorcycle": (170, 90, 170), "Bicycle": (80, 170, 80), "Agricultural": (40, 90, 140),
          "Trailer": (140, 140, 140), "EScooter": (40, 200, 220)}
KEY_TAB, KEY_ENTER, KEY_SPACE, KEY_ESC, KEY_BACKSPACE = 9, 13, 32, 27, 8
KEY_LEFT, KEY_UP, KEY_RIGHT, KEY_DOWN, KEY_DELETE = 2424832, 2490368, 2555904, 2621440, 3014656
STEP = 4
HEADER = 54


class Review:
    def __init__(self, list_file: Path):
        self.root = datasets_root()
        info = json.loads(list_file.read_text(encoding="utf-8"))
        self.items = info["items"]
        self.labels_file = self.root / "review" / "labels.json"
        self.labels = json.loads(self.labels_file.read_text(encoding="utf-8")) if self.labels_file.exists() else {}
        self.index = next((i for i, it in enumerate(self.items) if self.key(it) not in self.labels), len(self.items))
        self.next_class = "Car"
        self.drag = None
        self.load()

    @staticmethod
    def key(item: dict) -> str:
        return f"{item['sample']}/{item['view']}"

    def load(self) -> None:
        if self.index >= len(self.items):
            return
        item = self.items[self.index]
        folder = self.root / "inbox" / item["sample"]
        self.image = cv2.imread(str(folder / f"{item['view']}.jpg"))
        sample = json.loads((folder / "sample.json").read_text(encoding="utf-8-sig"))
        meta = next(i for i in sample["Images"] if i and i["File"].split(".")[0] == item["view"])
        existing = self.labels.get(self.key(item))
        if existing and existing.get("boxes") is not None:
            self.boxes = copy.deepcopy(existing["boxes"])
        else:
            self.boxes = [{"class": b["Class"], "x": b["X"], "y": b["Y"], "w": b["Width"], "h": b["Height"]}
                          for b in tightened(meta) if b.get("Class") in CLASSES and (b.get("Confidence") or 0) >= 0.2]
        self.selected = 0 if self.boxes else None
        self.history = []
        h, w = self.image.shape[:2]
        self.scale = min(1.0, 1600 / w)

    def remember(self) -> None:
        self.history.append((copy.deepcopy(self.boxes), self.selected))

    def save(self, status: str) -> None:
        item = self.items[self.index]
        self.labels[self.key(item)] = {
            "status": status, "boxes": self.boxes if status == "ok" else None,
            "width": self.image.shape[1], "height": self.image.shape[0], "at": datetime.now().isoformat(timespec="seconds"),
        }
        self.labels_file.parent.mkdir(parents=True, exist_ok=True)
        tmp = self.labels_file.with_suffix(".tmp")
        tmp.write_text(json.dumps(self.labels, indent=1), encoding="utf-8")
        tmp.replace(self.labels_file)

    def draw(self) -> np.ndarray:
        view = cv2.resize(self.image, None, fx=self.scale, fy=self.scale, interpolation=cv2.INTER_AREA)
        for k, b in enumerate(self.boxes):
            c = COLORS.get(b["class"], (255, 255, 255))
            p0 = (int(b["x"] * self.scale), int(b["y"] * self.scale))
            p1 = (int((b["x"] + b["w"]) * self.scale), int((b["y"] + b["h"]) * self.scale))
            cv2.rectangle(view, p0, p1, (255, 255, 255) if k == self.selected else c, 3 if k == self.selected else 2)
            cv2.putText(view, b["class"], (p0[0] + 3, max(14, p0[1] - 5)), cv2.FONT_HERSHEY_SIMPLEX, 0.55, c, 2)
        if self.drag and self.drag["mode"] == "new":
            x0, y0, x1, y1 = self.drag["start"] + self.drag["end"]
            cv2.rectangle(view, (x0, y0 - HEADER), (x1, y1 - HEADER), COLORS[self.next_class], 1)
        item = self.items[self.index]
        head = np.zeros((HEADER, view.shape[1], 3), np.uint8)
        line1 = (f"{self.index + 1}/{len(self.items)}  {item['time']}  {item.get('lighting') or ''}  counted: "
                 f"{item.get('class') or '-'}  next new box: {self.next_class}")
        line2 = "why: " + "; ".join(item.get("why") or [])
        cv2.putText(head, line1, (8, 20), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 255, 255), 1)
        cv2.putText(head, line2[:180], (8, 44), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (180, 220, 255), 1)
        return np.vstack([head, view])

    def to_image(self, x: int, y: int) -> tuple[float, float]:
        return x / self.scale, (y - HEADER) / self.scale

    def edge_at(self, x: float, y: float) -> str | None:
        if self.selected is None:
            return None
        b = self.boxes[self.selected]
        tol = 10 / self.scale
        inside_y = b["y"] - tol <= y <= b["y"] + b["h"] + tol
        inside_x = b["x"] - tol <= x <= b["x"] + b["w"] + tol
        if inside_y and abs(x - b["x"]) <= tol:
            return "left"
        if inside_y and abs(x - (b["x"] + b["w"])) <= tol:
            return "right"
        if inside_x and abs(y - b["y"]) <= tol:
            return "top"
        if inside_x and abs(y - (b["y"] + b["h"])) <= tol:
            return "bottom"
        return None

    def on_mouse(self, event, x, y, flags, _param) -> None:
        ix, iy = self.to_image(x, y)
        if event == cv2.EVENT_LBUTTONDOWN:
            edge = self.edge_at(ix, iy)
            if edge:
                self.remember()
                self.drag = {"mode": edge}
            else:
                hit = [k for k, b in enumerate(self.boxes) if b["x"] <= ix <= b["x"] + b["w"] and b["y"] <= iy <= b["y"] + b["h"]]
                if hit:
                    self.selected = min(hit, key=lambda k: self.boxes[k]["w"] * self.boxes[k]["h"])
                    self.drag = None
                else:
                    self.drag = {"mode": "new", "start": (x, y), "end": (x, y)}
        elif event == cv2.EVENT_MOUSEMOVE and self.drag:
            if self.drag["mode"] == "new":
                self.drag["end"] = (x, y)
            else:
                self.move_edge(self.drag["mode"], ix, iy)
        elif event == cv2.EVENT_LBUTTONUP and self.drag:
            if self.drag["mode"] == "new":
                (x0, y0), (x1, y1) = (self.to_image(*self.drag["start"]), self.to_image(x, y))
                if abs(x1 - x0) > 5 and abs(y1 - y0) > 5:
                    self.remember()
                    self.boxes.append({"class": self.next_class, "x": min(x0, x1), "y": min(y0, y1),
                                       "w": abs(x1 - x0), "h": abs(y1 - y0)})
                    self.selected = len(self.boxes) - 1
            self.drag = None

    def move_edge(self, edge: str, ix: float, iy: float) -> None:
        b = self.boxes[self.selected]
        right, bottom = b["x"] + b["w"], b["y"] + b["h"]
        if edge == "left":
            b["x"] = min(ix, right - 2)
            b["w"] = right - b["x"]
        elif edge == "right":
            b["w"] = max(2, ix - b["x"])
        elif edge == "top":
            b["y"] = min(iy, bottom - 2)
            b["h"] = bottom - b["y"]
        else:
            b["h"] = max(2, iy - b["y"])

    def nudge(self, side: str, amount: float) -> None:
        if self.selected is None:
            return
        self.remember()
        b = self.boxes[self.selected]
        h, w = self.image.shape[:2]
        if side == "left":
            new_x = min(max(0, b["x"] - amount), b["x"] + b["w"] - 2)
            b["w"] += b["x"] - new_x
            b["x"] = new_x
        elif side == "right":
            b["w"] = max(2, min(w - b["x"], b["w"] + amount))
        elif side == "top":
            new_y = min(max(0, b["y"] - amount), b["y"] + b["h"] - 2)
            b["h"] += b["y"] - new_y
            b["y"] = new_y
        else:
            b["h"] = max(2, min(h - b["y"], b["h"] + amount))

    def run(self) -> None:
        window = "Review samples"
        cv2.namedWindow(window, cv2.WINDOW_AUTOSIZE)
        cv2.setMouseCallback(window, self.on_mouse)
        while self.index < len(self.items):
            cv2.imshow(window, self.draw())
            key = cv2.waitKeyEx(30)
            if key == -1:
                continue
            char = chr(key) if 0 <= key < 256 else ""
            if key in (KEY_ENTER, KEY_SPACE):
                self.save("ok")
                self.index += 1
                self.load()
            elif char == "s":
                self.save("skip")
                self.index += 1
                self.load()
            elif char == "b" or key == KEY_BACKSPACE:
                self.index = max(0, self.index - 1)
                self.load()
            elif key == KEY_ESC or char == "q":
                break
            elif key == KEY_TAB and self.boxes:
                self.selected = 0 if self.selected is None else (self.selected + 1) % len(self.boxes)
            elif char in "123456789" and char:
                cls = CLASSES[int(char) - 1]
                self.next_class = cls
                if self.selected is not None:
                    self.remember()
                    self.boxes[self.selected]["class"] = cls
            elif (key == KEY_DELETE or char == "x") and self.selected is not None:
                self.remember()
                del self.boxes[self.selected]
                self.selected = (min(self.selected, len(self.boxes) - 1)) if self.boxes else None
            elif char == "u" and self.history:
                self.boxes, self.selected = self.history.pop()
            elif key in (KEY_LEFT, KEY_RIGHT, KEY_UP, KEY_DOWN):
                self.nudge({KEY_LEFT: "left", KEY_RIGHT: "right", KEY_UP: "top", KEY_DOWN: "bottom"}[key], STEP / self.scale)
            elif char in "ijkl" and char:
                self.nudge({"i": "top", "j": "left", "k": "bottom", "l": "right"}[char], -STEP / self.scale)
        cv2.destroyAllWindows()
        done = sum(1 for it in self.items if self.key(it) in self.labels)
        print(f"{done} of {len(self.items)} images reviewed; labels in {self.labels_file}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("list", type=Path, help="selection from select_for_labelling.py")
    args = parser.parse_args()
    if not args.list.exists():
        sys.exit(f"{args.list} not found")
    Review(args.list).run()


if __name__ == "__main__":
    main()
