using TrafficCounter.Core;

namespace TrafficCounter.Detection.Onnx;

public enum ExecutionProviderPreference
{
    /// <summary>
    /// DirectML if a DirectX 12 GPU is usable, otherwise CPU (ADR-0007). In the OpenVINO build
    /// (<c>-p:InferenceBackend=OpenVino</c>) OpenVINO first, then CPU (ADR-0029). In the NVIDIA build
    /// (<c>-p:InferenceBackend=Nvidia</c>) TensorRT first, then CUDA, then CPU (ADR-0041).
    /// </summary>
    Auto,
    DirectML,
    Cpu,

    /// <summary>Intel OpenVINO (integrated graphics, NPU or CPU); only in the OpenVINO build.</summary>
    OpenVino,

    /// <summary>NVIDIA TensorRT, with CUDA for parts TensorRT cannot run; only in the NVIDIA build.</summary>
    TensorRT,

    /// <summary>NVIDIA CUDA without TensorRT; only in the NVIDIA build.</summary>
    Cuda,
}

public sealed class YoloDetectorOptions
{
    /// <summary>Path to the ONNX model (exported with tools/training/export_model.py).</summary>
    public string ModelPath { get; set; } = "models/yolo11s.onnx";

    public ExecutionProviderPreference ExecutionProvider { get; set; } = ExecutionProviderPreference.Auto;

    /// <summary>OpenVINO device: "GPU" (integrated graphics), "CPU", "NPU" or "AUTO".</summary>
    public string OpenVinoDevice { get; set; } = "GPU";

    /// <summary>DirectML adapter or CUDA device index, if several GPUs are present (e.g. integrated and dedicated).</summary>
    public int DeviceId { get; set; }

    /// <summary>TensorRT: compute in half precision (much faster on RTX cards; YOLO loses no accuracy).</summary>
    public bool TensorRtFp16 { get; set; } = true;

    /// <summary>
    /// TensorRT: folder for the built engines. Building one takes minutes; with the cache only the first start of a
    /// model does. Empty: "trt-cache" next to the model.
    /// </summary>
    public string? TensorRtCachePath { get; set; }

    /// <summary>
    /// NVIDIA build: folders with the CUDA, cuDNN and TensorRT libraries (e.g. from the PyTorch and TensorRT Python
    /// packages), put in front of PATH before the first session is created. Empty if they are on PATH already.
    /// </summary>
    public List<string> NvidiaLibraryPaths { get; set; } = [];

    /// <summary>
    /// Input size used when the model has dynamic dimensions. A wide input suits the horizontal evaluation band
    /// (ADR-0020); fixed-size models ignore it.
    /// </summary>
    public int InputWidth { get; set; } = 1280;

    public int InputHeight { get; set; } = 384;

    /// <summary>Detections below this confidence are discarded (the tracker still uses those above 0.1).</summary>
    public float MinConfidence { get; set; } = 0.1f;

    /// <summary>IoU above which overlapping boxes are merged – across classes, so a van is not both car and truck.</summary>
    public float NmsIoU { get; set; } = 0.5f;

    public int IntraOpThreads { get; set; }

    /// <summary>
    /// How the band is scaled to the model input. Linear (as in Ultralytics training) uses only about 4 of the 9 source
    /// pixels per model pixel at a 3:1 reduction; Area averages all of them, which lowers the image noise at night.
    /// </summary>
    public ResizeMethod Resize { get; set; } = ResizeMethod.Linear;

    /// <summary>
    /// Also decode people and report them through <see cref="Vision.IPersonDetections"/>, for anonymising training
    /// samples (ADR-0026). They are not returned as detections and never counted.
    /// </summary>
    public bool ReportPersons { get; set; }

    /// <summary>Model class name → target class. Unmapped classes (e.g. person) are ignored.</summary>
    public Dictionary<string, VehicleClass> ClassMapping { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["car"] = VehicleClass.Car,
        ["truck"] = VehicleClass.Truck,
        ["bus"] = VehicleClass.Bus,
        ["motorcycle"] = VehicleClass.Motorcycle,
        ["bicycle"] = VehicleClass.Bicycle,

        // Classes of the own model (ADR-0039); "Trailer" stays unmapped until the counting handles it.
        ["van"] = VehicleClass.Van,
        ["agricultural"] = VehicleClass.Agricultural,
        ["escooter"] = VehicleClass.EScooter,
    };
}

public enum ResizeMethod
{
    Linear,
    Area,
}
