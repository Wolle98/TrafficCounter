using System.Management;
using OpenCvSharp;
using TrafficCounter.Vision;

namespace TrafficCounter.CameraProbe;

internal static class DeviceList
{
    private const int MaxIndex = 6;

    /// <param name="namesOnly">Only list the DirectShow names; opens no camera (safe while the service runs).</param>
    public static void Run(bool namesOnly = false)
    {
        Console.WriteLine("DirectShow devices (index: name; use the name with DeviceName / --device-name):");
        IReadOnlyList<string> directShow = DirectShowDevices.List();
        for (int i = 0; i < directShow.Count; i++)
        {
            Console.WriteLine($"  {i}: {directShow[i]}");
        }

        if (namesOnly)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Camera devices known to Windows:");
        List<string> names = QueryWindowsCameras();
        if (names.Count == 0)
        {
            Console.WriteLine("  (none)");
        }

        foreach (string name in names)
        {
            Console.WriteLine($"  {name}");
        }

        Console.WriteLine();
        Console.WriteLine("Device indices usable with DirectShow (--device):");
        bool any = false;
        for (int index = 0; index < MaxIndex; index++)
        {
            using var capture = new VideoCapture(index, VideoCaptureAPIs.DSHOW);
            if (!capture.IsOpened())
            {
                continue;
            }

            using Mat frame = new();
            string state = capture.Read(frame) && !frame.Empty() ? $"{frame.Width}x{frame.Height} (default mode)" : "opens, but delivers no frame";
            Console.WriteLine($"  {index}: {state}");
            any = true;
        }

        if (!any)
        {
            Console.WriteLine("  (none) – is the camera connected and not used by another application?");
        }
    }

    private static List<string> QueryWindowsCameras()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, PNPClass FROM Win32_PnPEntity WHERE PNPClass = 'Camera' OR PNPClass = 'Image'");
        return [.. searcher.Get().Cast<ManagementObject>().Select(o => $"{o["Name"]} [{o["PNPClass"]}]")];
    }
}
