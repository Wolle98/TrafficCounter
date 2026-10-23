"""Export an Ultralytics YOLO model to ONNX for TrafficCounter (ADR-0006).

The counter analyses a wide horizontal band of the frame (ADR-0020), so the default input is 1280 x 384
instead of the square 640 x 640 - small objects such as bicycles stay large enough.

Usage:
    python export_model.py                      # yolo11s (COCO), 384 x 1280 -> models/yolo11s-1280x384.onnx
    python export_model.py --model yolo11n.pt   # smaller/faster model, e.g. for CPU-only machines
    python export_model.py --width 640 --height 192   # smaller input: about a quarter of the computation
    python export_model.py --dynamic            # any input size, chosen by Detector:InputWidth/InputHeight

Load settings for weaker hardware are described in ADR-0029 and docs/betrieb.md. A model with a fixed input size
is usually somewhat faster than a dynamic one.
    python export_model.py --model runs/detect/train/weights/best.pt --out D:/Code/TrafficCounterPrivate/models
"""

import argparse
import shutil
from pathlib import Path

from ultralytics import YOLO


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--model", default="yolo11s.pt", help="Ultralytics weights (downloaded if a known name)")
    parser.add_argument("--height", type=int, default=384, help="input height, multiple of 32")
    parser.add_argument("--width", type=int, default=1280, help="input width, multiple of 32")
    parser.add_argument("--out", default="models", help="target directory for the .onnx file")
    parser.add_argument("--opset", type=int, default=17)
    parser.add_argument("--dynamic", action="store_true", help="dynamic input size (set it in the detector options)")
    args = parser.parse_args()

    if args.height % 32 or args.width % 32:
        parser.error("height and width must be multiples of 32")

    model = YOLO(args.model)
    exported = Path(model.export(format="onnx", imgsz=(args.height, args.width), opset=args.opset, simplify=True, dynamic=args.dynamic))

    target_dir = Path(args.out)
    target_dir.mkdir(parents=True, exist_ok=True)
    size = "dynamic" if args.dynamic else f"{args.width}x{args.height}"
    target = target_dir / f"{Path(args.model).stem}-{size}.onnx"
    shutil.move(str(exported), target)
    print(f"Model written to {target}")
    print("Class names are stored in the ONNX metadata ('names') and read by the detector.")


if __name__ == "__main__":
    main()
