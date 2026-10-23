using Microsoft.ML.OnnxRuntime;

namespace TrafficCounter.Detection.Onnx.Tests;

public class OnnxRuntimeTests
{
    [Fact]
    public void Native_runtime_loads_with_the_accelerator_of_the_build_and_the_CPU_provider()
    {
        string[] providers = OrtEnv.Instance().GetAvailableProviders();

        // Default build: DirectML; -p:InferenceBackend=OpenVino: Intel OpenVINO (ADR-0029).
        Assert.Contains(YoloOnnxDetector.IsOpenVinoBuild ? "OpenVINOExecutionProvider" : "DmlExecutionProvider", providers);
        Assert.Contains("CPUExecutionProvider", providers);
    }

    [Fact]
    public void Missing_model_gives_a_helpful_error()
    {
        var options = new YoloDetectorOptions { ModelPath = "does-not-exist.onnx" };

        FileNotFoundException ex = Assert.Throws<FileNotFoundException>(() =>
            new YoloOnnxDetector(options, Microsoft.Extensions.Logging.Abstractions.NullLogger<YoloOnnxDetector>.Instance));

        Assert.Contains("export_model.py", ex.Message);
    }
}
