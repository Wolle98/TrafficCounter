"""Quantize an exported TrafficCounter ONNX model to INT8 (ADR-0029).

INT8 roughly halves the inference time on CPUs and Intel graphics (OpenVINO) at a usually small loss of accuracy -
check it with the regression before using the model. Calibration uses frames of a reference video from the private
repository; they are read into memory only, nothing is written except the model.

Usage:
    python quantize_model.py --model ../../models/yolo11s-640x192.onnx --video D:/Code/TrafficCounterPrivate/recordings/20260929-124955.mp4
"""

import argparse
import json
from pathlib import Path

import cv2
import numpy as np
from onnxruntime.quantization import CalibrationDataReader, QuantFormat, QuantType, quantize_static
from onnxruntime.quantization.shape_inference import quant_pre_process


def letterbox(image: np.ndarray, width: int, height: int) -> np.ndarray:
    """Same preprocessing as YoloOnnxDetector: letterbox with grey 114, RGB, CHW, 0..1."""
    scale = min(width / image.shape[1], height / image.shape[0])
    resized = cv2.resize(image, (round(image.shape[1] * scale), round(image.shape[0] * scale)), interpolation=cv2.INTER_LINEAR)
    padded = np.full((height, width, 3), 114, dtype=np.uint8)
    top = (height - resized.shape[0]) // 2
    left = (width - resized.shape[1]) // 2
    padded[top:top + resized.shape[0], left:left + resized.shape[1]] = resized
    return (cv2.cvtColor(padded, cv2.COLOR_BGR2RGB).transpose(2, 0, 1)[None].astype(np.float32)) / 255.0


class VideoFrames(CalibrationDataReader):
    """Evenly spaced frames of the video's evaluation band."""

    def __init__(self, video: Path, band: tuple[float, float], frames: int, input_name: str, width: int, height: int):
        capture = cv2.VideoCapture(str(video))
        count = int(capture.get(cv2.CAP_PROP_FRAME_COUNT))
        self._inputs = []
        for index in np.linspace(0, count - 1, frames).astype(int):
            capture.set(cv2.CAP_PROP_POS_FRAMES, int(index))
            ok, frame = capture.read()
            if not ok:
                continue
            top, bottom = int(band[0] * frame.shape[0]), int(band[1] * frame.shape[0])
            self._inputs.append({input_name: letterbox(frame[top:bottom], width, height)})
        capture.release()
        self._iterator = iter(self._inputs)

    def get_next(self):
        return next(self._iterator, None)


def band_of(video: Path, site: Path | None) -> tuple[float, float]:
    """Evaluation band of the video: a band-only recording is analysed completely, otherwise the calibrated band."""
    meta = Path(str(video) + ".json")
    if meta.exists() and json.loads(meta.read_text(encoding="utf-8")).get("Band"):
        return 0.0, 1.0
    site_file = site or Path(str(video) + ".site.json")
    geometry = json.loads(site_file.read_text(encoding="utf-8"))["TrafficCounter"]["Site"]
    return geometry["BandTop"], geometry["BandBottom"]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--model", required=True, help="exported .onnx model with a fixed input size")
    parser.add_argument("--video", required=True, help="reference video for calibration (private repository)")
    parser.add_argument("--site", help="site configuration, default <video>.site.json")
    parser.add_argument("--frames", type=int, default=200, help="number of calibration frames")
    args = parser.parse_args()

    model = Path(args.model)
    prepared = model.with_name(model.stem + "-prep.onnx")
    quant_pre_process(str(model), str(prepared))

    import onnx
    graph_input = onnx.load(str(prepared)).graph.input[0]
    dims = [d.dim_value for d in graph_input.type.tensor_type.shape.dim]
    if dims[2] <= 0 or dims[3] <= 0:
        parser.error("the model needs a fixed input size (export without --dynamic)")

    reader = VideoFrames(Path(args.video), band_of(Path(args.video), Path(args.site) if args.site else None),
                         args.frames, graph_input.name, dims[3], dims[2])
    target = model.with_name(model.stem + "-int8.onnx")
    # Only the convolutions: the detection head concatenates box coordinates (0..input size) and class scores
    # (0..1) into one tensor, and a shared INT8 scale for both would wipe out the scores.
    quantize_static(str(prepared), str(target), reader, quant_format=QuantFormat.QDQ, op_types_to_quantize=["Conv"],
                    activation_type=QuantType.QUInt8, weight_type=QuantType.QInt8, per_channel=True)
    prepared.unlink()
    print(f"INT8 model written to {target}")


if __name__ == "__main__":
    main()
