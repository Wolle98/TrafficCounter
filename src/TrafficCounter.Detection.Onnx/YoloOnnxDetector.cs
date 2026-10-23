using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using TrafficCounter.Core;
using TrafficCounter.Core.Geometry;
using TrafficCounter.Vision;

namespace TrafficCounter.Detection.Onnx;

/// <summary>
/// Ultralytics YOLO model exported to ONNX (ADR-0006), run with DirectML or on the CPU (ADR-0007).
/// Supports raw head output and NMS-free end-to-end output; class names come from the model metadata.
/// </summary>
public sealed class YoloOnnxDetector : IObjectDetector, IPersonDetections
{
    private readonly InferenceSession _session;
    private readonly YoloDetectorOptions _options;
    private readonly string _inputName;
    private readonly int _inputWidth;
    private readonly int _inputHeight;
    private readonly Dictionary<int, VehicleClass> _classMap;
    private readonly float[] _inputBuffer;
    private readonly int? _personClass;

    public YoloOnnxDetector(YoloDetectorOptions options, ILogger<YoloOnnxDetector> logger)
    {
        _options = options;
        (_session, ExecutionProvider) = CreateSession(options, logger);
        ModelName = Path.GetFileNameWithoutExtension(options.ModelPath);

        NodeMetadata input = _session.InputMetadata.Values.Single();
        _inputName = _session.InputMetadata.Keys.Single();
        int[] shape = input.Dimensions; // [1, 3, H, W]; -1 for dynamic
        _inputHeight = shape[2] > 0 ? shape[2] : options.InputHeight;
        _inputWidth = shape[3] > 0 ? shape[3] : options.InputWidth;
        _inputBuffer = new float[3 * _inputHeight * _inputWidth];

        Dictionary<int, string> names = _session.ModelMetadata.CustomMetadataMap.TryGetValue("names", out string? metadata)
            ? YoloPostprocessing.ParseClassNames(metadata)
            : throw new InvalidDataException($"Model '{options.ModelPath}' has no class names in its metadata.");
        _classMap = names
            .Where(n => options.ClassMapping.ContainsKey(n.Value))
            .ToDictionary(n => n.Key, n => options.ClassMapping[n.Value]);
        _personClass = options.ReportPersons ? names.Where(n => n.Value == "person").Select(n => (int?)n.Key).FirstOrDefault() : null;

        logger.LogInformation(
            "Detector {Model} on {Provider}, input {Width}x{Height}, classes used: {Classes}",
            ModelName, ExecutionProvider, _inputWidth, _inputHeight,
            string.Join(", ", _classMap.Select(c => $"{names[c.Key]}→{c.Value}")));
    }

    public string ModelName { get; }

    public string ExecutionProvider { get; }

    public IReadOnlyList<BoundingBox> LastPersons { get; private set; } = [];

    /// <summary>True if people are reported: <see cref="YoloDetectorOptions.ReportPersons"/> is set and the model knows them.</summary>
    public bool DetectsPersons => _personClass is not null;

    public IReadOnlyList<Core.Detection> Detect(Mat image, Rect region)
    {
        using Mat roi = new(image, region);
        Letterbox letterbox = Letterbox.For(roi.Width, roi.Height, _inputWidth, _inputHeight);
        FillInput(roi, letterbox);

        var tensor = new DenseTensor<float>(_inputBuffer, [1, 3, _inputHeight, _inputWidth]);
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results =
            _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, tensor)]);
        var output = (DenseTensor<float>)results[0].Value;
        ReadOnlySpan<int> dims = output.Dimensions;

        bool endToEnd = dims.Length == 3 && dims[2] == 6;
        List<RawDetection> raw = endToEnd
            ? YoloPostprocessing.DecodeEndToEnd(output.Buffer.Span, dims[1], _options.MinConfidence, _classMap.ContainsKey)
            : YoloPostprocessing.DecodeRaw(output.Buffer.Span, dims[1], dims[2], _options.MinConfidence, _classMap.ContainsKey);

        // People in a separate pass: the suppression of overlapping boxes is class-agnostic and would otherwise
        // let a rider's box remove the bicycle. A low threshold, because missing a face is worse than an extra blur.
        if (_personClass is int person)
        {
            List<RawDetection> people = endToEnd
                ? YoloPostprocessing.DecodeEndToEnd(output.Buffer.Span, dims[1], 0.15f, c => c == person)
                : YoloPostprocessing.DecodeRaw(output.Buffer.Span, dims[1], dims[2], 0.15f, c => c == person);
            LastPersons = [.. YoloPostprocessing.NonMaximumSuppression(people, _options.NmsIoU).Select(d => letterbox.ToSource(d.Box, region.X, region.Y))];
        }

        return
        [
            .. YoloPostprocessing.NonMaximumSuppression(raw, _options.NmsIoU)
                .Select(d => new Core.Detection(letterbox.ToSource(d.Box, region.X, region.Y), _classMap[d.ClassIndex], d.Score)),
        ];
    }

    public void Dispose() => _session.Dispose();

    /// <summary>Letterboxes the region into the input buffer as RGB, CHW, 0..1 (grey padding 114 like Ultralytics).</summary>
    private void FillInput(Mat roi, Letterbox letterbox)
    {
        int width = (int)Math.Round(roi.Width * letterbox.Scale);
        int height = (int)Math.Round(roi.Height * letterbox.Scale);
        using Mat resized = new();
        Cv2.Resize(roi, resized, new Size(width, height), 0, 0,
            _options.Resize == ResizeMethod.Area && letterbox.Scale < 1 ? InterpolationFlags.Area : InterpolationFlags.Linear);
        using Mat padded = new(_inputHeight, _inputWidth, MatType.CV_8UC3, new Scalar(114, 114, 114));
        using (Mat target = new(padded, new Rect((int)letterbox.PadX, (int)letterbox.PadY, width, height)))
        {
            resized.CopyTo(target);
        }

        // OpenCV stores BGR; the model expects RGB planes scaled to 0..1.
        using Mat blob = CvDnn.BlobFromImage(padded, 1 / 255.0, default, default, swapRB: true, crop: false);
        Marshal.Copy(blob.Data, _inputBuffer, 0, _inputBuffer.Length);
    }

    private static (InferenceSession Session, string Provider) CreateSession(YoloDetectorOptions options, ILogger logger)
    {
        if (!File.Exists(options.ModelPath))
        {
            throw new FileNotFoundException($"Model file '{options.ModelPath}' not found. Export it with tools/training/export_model.py.", options.ModelPath);
        }

        ExecutionProviderPreference preferred = options.ExecutionProvider != ExecutionProviderPreference.Auto
            ? options.ExecutionProvider
            : IsOpenVinoBuild ? ExecutionProviderPreference.OpenVino
            : IsNvidiaBuild ? ExecutionProviderPreference.TensorRT
            : ExecutionProviderPreference.DirectML;

        if (preferred is ExecutionProviderPreference.TensorRT or ExecutionProviderPreference.Cuda)
        {
            AddNvidiaLibraryPaths(options.NvidiaLibraryPaths);
            if (preferred == ExecutionProviderPreference.TensorRT)
            {
                try
                {
                    string cache = string.IsNullOrWhiteSpace(options.TensorRtCachePath)
                        ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(options.ModelPath))!, "trt-cache")
                        : options.TensorRtCachePath;
                    Directory.CreateDirectory(cache);
                    using SessionOptions trtOptions = BaseOptions(options);
                    using var trt = new OrtTensorRTProviderOptions();
                    trt.UpdateOptions(new Dictionary<string, string>
                    {
                        ["device_id"] = options.DeviceId.ToString(CultureInfo.InvariantCulture),
                        ["trt_fp16_enable"] = options.TensorRtFp16 ? "1" : "0",
                        ["trt_engine_cache_enable"] = "1",
                        ["trt_engine_cache_path"] = cache,
                        ["trt_timing_cache_enable"] = "1",
                        ["trt_timing_cache_path"] = cache,
                    });
                    trtOptions.AppendExecutionProvider_Tensorrt(trt);
                    trtOptions.AppendExecutionProvider_CUDA(options.DeviceId); // nodes TensorRT does not take
                    return (new InferenceSession(options.ModelPath, trtOptions), $"TensorRT (device {options.DeviceId}{(options.TensorRtFp16 ? ", FP16" : "")})");
                }
                catch (Exception ex) when (options.ExecutionProvider == ExecutionProviderPreference.Auto && ex is OnnxRuntimeException or EntryPointNotFoundException or DllNotFoundException)
                {
                    logger.LogWarning(ex, "TensorRT not usable, falling back to CUDA");
                }
            }

            try
            {
                using SessionOptions cudaOptions = BaseOptions(options);
                cudaOptions.AppendExecutionProvider_CUDA(options.DeviceId);
                return (new InferenceSession(options.ModelPath, cudaOptions), $"CUDA (device {options.DeviceId})");
            }
            catch (Exception ex) when (options.ExecutionProvider == ExecutionProviderPreference.Auto && ex is OnnxRuntimeException or EntryPointNotFoundException or DllNotFoundException)
            {
                logger.LogWarning(ex, "CUDA not usable, falling back to CPU");
            }
        }
        else if (preferred == ExecutionProviderPreference.OpenVino)
        {
            try
            {
                using SessionOptions openVinoOptions = BaseOptions(options);
                openVinoOptions.AppendExecutionProvider("OpenVINO", new Dictionary<string, string> { ["device_type"] = options.OpenVinoDevice });
                return (new InferenceSession(options.ModelPath, openVinoOptions), $"OpenVINO ({options.OpenVinoDevice})");
            }
            catch (Exception ex) when (options.ExecutionProvider == ExecutionProviderPreference.Auto && ex is OnnxRuntimeException or EntryPointNotFoundException)
            {
                logger.LogWarning(ex, "OpenVINO not usable, falling back to CPU");
            }
        }
        else if (preferred == ExecutionProviderPreference.DirectML)
        {
            try
            {
                using var gpuOptions = BaseOptions(options);
                gpuOptions.EnableMemoryPattern = false; // required by DirectML
                gpuOptions.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                gpuOptions.AppendExecutionProvider_DML(options.DeviceId);
                return (new InferenceSession(options.ModelPath, gpuOptions), $"DirectML (device {options.DeviceId})");
            }
            catch (Exception ex) when (options.ExecutionProvider == ExecutionProviderPreference.Auto && ex is OnnxRuntimeException or EntryPointNotFoundException)
            {
                logger.LogWarning(ex, "DirectML not usable, falling back to CPU");
            }
        }

        using SessionOptions cpuOptions = BaseOptions(options);
        return (new InferenceSession(options.ModelPath, cpuOptions), "CPU");
    }

    /// <summary>True in the build with NVIDIA's TensorRT/CUDA runtime instead of DirectML (ADR-0041).</summary>
    internal static bool IsNvidiaBuild =>
#if NVIDIA
        true;
#else
        false;
#endif

    /// <summary>Puts the CUDA/cuDNN/TensorRT library folders in front of PATH, so ONNX Runtime finds them.</summary>
    private static void AddNvidiaLibraryPaths(IReadOnlyList<string> folders)
    {
        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        List<string> missing = [.. folders.Where(f => Directory.Exists(f) && !path.Split(';').Contains(f, StringComparer.OrdinalIgnoreCase))];
        if (missing.Count > 0)
        {
            Environment.SetEnvironmentVariable("PATH", string.Join(';', missing) + ";" + path);
        }
    }

    /// <summary>True in the build with Intel's OpenVINO runtime instead of DirectML (ADR-0029).</summary>
    internal static bool IsOpenVinoBuild =>
#if OPENVINO
        true;
#else
        false;
#endif

    private static SessionOptions BaseOptions(YoloDetectorOptions options)
    {
        var sessionOptions = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        if (options.IntraOpThreads > 0)
        {
            sessionOptions.IntraOpNumThreads = options.IntraOpThreads;
        }

        return sessionOptions;
    }
}
