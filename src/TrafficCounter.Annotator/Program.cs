using System.Globalization;
using Avalonia;
using TrafficCounter.Core;

namespace TrafficCounter.Annotator;

internal static class Program
{
    private const string Help =
        """
        TrafficCounter annotator – count vehicles and mark test drives in a reference video (ADR-0027, ADR-0035).

        Usage:
          TrafficCounter.Annotator [<video.mp4>] [--line 0.5] [--speed true]
          TrafficCounter.Annotator import [--provider SqlServer|PostgreSql] <video.mp4> [<video.mp4> ...]

          (no video)   Opens the window with the recordings folder of the private repository; choose a video on the
                       left.

          import       Writes the counted passages into the database (TRAFFICCOUNTER_DB or the local default),
                       marked as hand-counted with video name and position. Importing again replaces them.

          --line <x>   Position of the vertical reference line as fraction of the image width (default 0.5).
                       Press a key when a vehicle's front wheel crosses it.

          --speed true Starts with "Annotate speed" switched on (ADR-0035): after a mark and its class the
                       speedometer reading at the line is entered in the speed box (whole km/h; Enter or Save stores
                       it, empty removes it). A note (n) such as "braked" flags a disturbed drive; the regression lists
                       such drives separately. The regression converts readings into true speeds with the
                       speedometer calibration (TrafficCounter:SpeedTest in the config).

        Writes <video>.gt.csv next to the video after every change, speeds included; an existing file is continued
        5 s before its last entry. A video with only an older <video>.speed.csv keeps writing that file.

        Keys:
          Left arrow / Right arrow   vehicle crossing, moving to the left / to the right
          Num4 / Num6                vehicle crossing to the left / right, class car
          0-8       class of the last entry: 0 unknown 1 car 2 van 3 truck 4 bus 5 motorcycle 6 bicycle
                    7 agricultural (tractor incl. trailers) 8 e-scooter
          b / B     review: jump to the next / previous bicycle (2 s before it, normal speed) and select it,
                    so 8 turns it into an e-scooter; for counts made before the e-scooter class existed
          n         note for the last entry (Enter saves, Esc cancels)
          v         speedometer reading of the last entry in km/h (test drive; Enter saves, empty removes it)
          u         undo last entry
          Delete    delete the entry closest to the current position (within 1 s)
          space     pause / play        , .  one frame back / forward (paused)
          j l       5 s back / forward  J L  1 min back / forward
          - +       slower / faster (0.5x to 16x)
          q q       quit (press twice; Esc works as well)

        The window offers the same as buttons: class and direction on the right, speed and image controls on top,
        navigation and the timeline (coloured by class, triangles for speeds) below, files and counts on the left.
        """;

    [STAThread]
    public static int Main(string[] args)
    {
        Environment.SetEnvironmentVariable("OPENCV_LOG_LEVEL", "ERROR");
        if (args.Length > 0 && args[0] is "-h" or "--help")
        {
            Console.WriteLine(Help);
            return 0;
        }

        if (args.Length > 0 && args[0] == "import")
        {
            return ImportCommand.RunAsync(args[1..]).GetAwaiter().GetResult();
        }

        string? video = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) ? args[0] : null;
        double line = 0.5;
        bool speedMode = false;
        for (int i = video is null ? 0 : 1; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--line":
                    line = double.Parse(args[i + 1], CultureInfo.InvariantCulture);
                    break;
                case "--speed":
                    speedMode = args[i + 1] is "true" or "on";
                    break;
            }
        }

        string folder = video is not null && Path.GetDirectoryName(Path.GetFullPath(video)) is { } dir
            ? dir
            : PrivateLocations.PrivateRoot is { } root && Directory.Exists(Path.Combine(root, "recordings"))
                ? Path.Combine(root, "recordings")
                : Environment.CurrentDirectory;

        try
        {
            return AppBuilder.Configure(() => new App(video, folder, line, speedMode))
                .UsePlatformDetect()
                .StartWithClassicDesktopLifetime([]);
        }
        catch (Exception ex)
        {
            // The ground truth is saved after every change, so nothing is lost; keep the reason for diagnosis.
            string log = Path.Combine(AppContext.BaseDirectory, "annotator-crash.log");
            File.AppendAllText(log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {video}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            Console.Error.WriteLine($"Unexpected error, details in {log}:");
            Console.Error.WriteLine(ex);
            return 3;
        }
    }
}
