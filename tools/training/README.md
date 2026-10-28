# Model export and training

Python tools for the YOLO detector ([ADR-0006](../../docs/adr/0006-object-detection-yolo-onnx.md)). Training own models follows in stage 3 ([ADR-0018](../../docs/adr/0018-training-data.md)).

## Setup

Create a virtual environment once. Ultralytics pulls in PyTorch, which is several hundred MB.

```powershell
cd tools/training
python -m venv .venv
.venv\Scripts\pip install -r requirements.txt
```

## Exporting a pre-trained model

```powershell
.venv\Scripts\python export_model.py                            # models/yolo11s-1280x384.onnx
.venv\Scripts\python export_model.py --width 640 --height 192   # models/yolo11s-640x192.onnx
```

The script downloads `yolo11s.pt` (COCO) and exports it to ONNX.

- **Wide input** instead of 640×640: the counter only evaluates the horizontal band around the road ([ADR-0020](../../docs/adr/0020-local-traffic-situation.md)). This keeps small objects such as bicycles large enough. `yolo11s-640x192` is the best trade-off between accuracy and load and is used on the counting notebook ([ADR-0029](../../docs/adr/0029-computing-load.md)).
- **Options:** `--model` (e.g. `yolo11n.pt`, smaller and faster), `--width`/`--height` (multiples of 32), `--dynamic` (input size set in the detector options), `--out`, `--opset`.
- **Model files** (`*.onnx`, `*.pt`) are excluded in the public repo via `.gitignore`. Own, fine-tuned models belong in the private repo ([ADR-0002](../../docs/adr/0002-repository-split.md)).
- **Licence:** the Ultralytics models are licensed under AGPL-3.0, matching the project's licence ([ADR-0004](../../docs/adr/0004-license-agpl.md)).

## Targeted labelling (disagreements, keyboard review)

Labelling everything the service collects mostly produces cars the model already knows. Instead:

1. **Select:** `.venv-labelstudio\Scripts\python select_for_labelling.py --machine <counting machine> --from 2026-10-23T09:10 --limit 50` pairs the passages of the counting camera with the reference camera (UGREEN) by time and direction and picks the samples where they disagree (only one counted it, different class), of weak classes, or where the counting model was unsure. Agreeing ordinary cars and images labelled before are left out. Writes `datasets/review/review-<time>.json`, including the passages only the reference counted (they have no sample; cut them from recordings if one covers the time).
2. **Review:** `.venv\Scripts\python review_samples.py <that file>` shows each line image with the proposed boxes and why it was picked. Mostly Enter; keys for class (1–9), next box (Tab), delete, enlarge or shrink a box side (arrows, i j k l), skip, back; the mouse only for missing boxes or moving an edge. Labels go to `datasets/review/labels.json`.
3. **Train:** `train_model.py` reads these labels next to the Label Studio annotations; for the same image the review label wins.

## Offline tests with TensorRT (NVIDIA)

On a PC with an NVIDIA card the regression runs about 1.7 times faster per frame with TensorRT ([ADR-0041](../../docs/adr/0041-tensorrt-backend.md)); the counts are identical to DirectML (FP16). Build the NVIDIA variant once and install the TensorRT libraries into this venv (CUDA and cuDNN come with the CUDA build of PyTorch):

```powershell
.venv\Scripts\pip install --extra-index-url https://pypi.nvidia.com "tensorrt-cu12-libs>=10,<11"
dotnet build ..\..\src\TrafficCounter.Regression -c Release -p:InferenceBackend=Nvidia
..\..\src\TrafficCounter.Regression\bin-nvidia\Release\net10.0\TrafficCounter.Regression.exe --video <video> --model <model.onnx> `
    --provider TensorRT --nvidia-libs "$PWD\.venv\Lib\site-packages\torch\lib;$PWD\.venv\Lib\site-packages\tensorrt_libs"
```

The first run of a model builds the TensorRT engine (a few minutes) into `trt-cache` next to the model; later runs reuse it.

## INT8 quantisation

```powershell
.venv\Scripts\python quantize_model.py --model models/yolo11s-640x192.onnx --video <reference video> [--frames 200]
```

Calibrates with frames of a reference video (private repo; the band geometry comes from `<video>.site.json` or `--site`) and writes an INT8 model next to the input. Only the convolutions are quantised ([ADR-0029](../../docs/adr/0029-computing-load.md)). On the Iris Xe graphics INT8 was slower than FP32; it pays off rather on the CPU.

## Labelling the collected samples

The samples from the counting service (`datasets/inbox` in the private repo, [ADR-0026](../../docs/adr/0026-targeted-collection-of-training-samples.md)) are labelled in [Label Studio](https://labelstud.io/) (Apache-2.0), with the service's boxes as proposals. The rules (trailers, bicycles with rider, image edge) are in [ADR-0039](../../docs/adr/0039-labelling-rules.md).

Install once, in its own venv so it does not clash with Ultralytics:

```powershell
python -m venv .venv-labelstudio
.venv-labelstudio\Scripts\pip install label-studio
```

Then:

1. **Tasks:** `python labelstudio_tasks.py --limit 300` writes `datasets/labelstudio/tasks-<time>.json` for the next 300 new samples: bicycles and e-scooters first, then day, dusk and night in turn, each with the hardest cases first (not counted, wavering class). `--order hardest` puts not counted and night first instead. Each image (entry, line, exit) becomes one task with the proposals. `--views line` takes only the image at the line.
2. **Start:** `.\Start-LabelStudio.ps1` opens Label Studio at http://localhost:8089. Images are served from the private `datasets` folder, and Label Studio's own database stays in `datasets/labelstudio/data` (not versioned). Usage statistics are switched off.
3. **First start only:** create the local account. Then create a project:
   - *Labeling Interface > Code*: paste `labelstudio/label-config.xml`.
   - *Cloud Storage > Add Source Storage > Local files*: absolute path `<private repo>\datasets\inbox`. Do not sync it: the tasks come from the import, the storage only allows serving the images.
   - *Import*: upload the tasks file.
4. **Label:** correct the proposals, add missing boxes, delete wrong ones. Keys 1–8 select the class. *Submit*, or *Skip* if the image is unusable.
5. **Bicycles and e-scooters the model misses** never reach the service's collection. `TrafficCounter.Regression --extract-samples Bicycle` cuts them from the hand-counted recordings instead: three anonymised images per passage with the detector's boxes, into `datasets/inbox` like the collected samples. Run it again after new hand counts; existing samples are skipped.
6. **Small vans** (high-roof estates, [ADR-0055](../../docs/adr/0055-small-vans.md)) are labelled as `Van`. `.venv\Scripts\python labelstudio_small_vans.py` writes `datasets/labelstudio/tasks-<time>-smallvans.json` with the labelled images whose Car box is probably one: the model sees a van in it, or another image of the same vehicle is labelled Van. Import it into the same project; each task proposes the current labels, and once submitted it replaces them in the training.
7. **Export:** in the project *Export > JSON*, then `python labelstudio_to_yolo.py <export.json>`. It writes `datasets/labelled` (images, YOLO labels, `classes.txt`) in the private repo, the basis for fine-tuning.
