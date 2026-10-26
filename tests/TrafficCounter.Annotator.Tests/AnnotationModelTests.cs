using TrafficCounter.Core;
using TrafficCounter.Evaluation;

namespace TrafficCounter.Annotator.Tests;

public sealed class AnnotationModelTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("tc-annotator-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Video => Path.Combine(_directory, "video.mp4");

    private static GroundTruthEntry At(double seconds, ImageMotion motion = ImageMotion.LeftToRight, VehicleClass? vehicleClass = VehicleClass.Car) =>
        new(TimeSpan.FromSeconds(seconds), motion, vehicleClass);

    [Fact]
    public void Adding_saves_and_selects_and_the_same_crossing_is_recorded_once()
    {
        var model = new AnnotationModel(Video);

        Assert.True(model.Add(At(1)));
        Assert.False(model.Add(At(1)));
        Assert.True(model.Add(At(1, ImageMotion.RightToLeft)));

        Assert.Equal(1, model.SelectedIndex);
        Assert.Equal(2, new AnnotationModel(Video).Entries.Count);
    }

    [Fact]
    public void Speed_is_stored_in_the_ground_truth_and_checked()
    {
        var model = new AnnotationModel(Video);
        model.Add(At(5));

        Assert.True(model.SetSpeed(31));
        Assert.False(model.SetSpeed(250));
        Assert.Equal(GroundTruthFile.PathFor(Video), model.Path);
        Assert.Equal(31f, new AnnotationModel(Video).Entries.Single().SpeedKmh);

        Assert.True(model.SetSpeed(null));
        Assert.Null(new AnnotationModel(Video).Entries.Single().SpeedKmh);
    }

    [Fact]
    public void A_video_with_only_an_old_speed_file_keeps_writing_it()
    {
        GroundTruthFile.Write(GroundTruthFile.SpeedPathFor(Video), [At(3) with { SpeedKmh = 30 }]);

        var model = new AnnotationModel(Video);

        Assert.Equal(GroundTruthFile.SpeedPathFor(Video), model.Path);
        Assert.Single(model.Entries);
    }

    [Fact]
    public void Delete_near_removes_the_closest_entry_within_one_second_and_undo_the_selected()
    {
        var model = new AnnotationModel(Video);
        model.Add(At(10));
        model.Add(At(20));

        model.DeleteNear(TimeSpan.FromSeconds(12));
        Assert.Equal(2, model.Entries.Count);
        model.DeleteNear(TimeSpan.FromSeconds(10.6));
        Assert.Equal([20.0], model.Entries.Select(e => e.Offset.TotalSeconds));

        model.SelectedIndex = 0;
        model.UndoSelected();
        Assert.Empty(model.Entries);
    }

    [Fact]
    public void Bicycle_search_wraps_forward_but_not_backward_and_neighbours_skip_the_current_entry()
    {
        var model = new AnnotationModel(Video);
        model.Add(At(10, vehicleClass: VehicleClass.Bicycle));
        model.Add(At(20));
        model.Add(At(30, vehicleClass: VehicleClass.Bicycle));
        model.SelectedIndex = -1;

        Assert.Equal(2, model.FindBicycle(TimeSpan.FromSeconds(15), forward: true));
        Assert.Equal(0, model.FindBicycle(TimeSpan.FromSeconds(35), forward: true));
        Assert.Null(model.FindBicycle(TimeSpan.FromSeconds(5), forward: false));

        Assert.Equal(1, model.Neighbour(TimeSpan.FromSeconds(10), forward: true));
        Assert.Equal(1, model.Neighbour(TimeSpan.FromSeconds(30), forward: false));
    }

    [Fact]
    public void A_video_only_opened_gets_no_annotation_file()
    {
        var model = new AnnotationModel(Video);

        model.Save();

        Assert.False(File.Exists(model.Path));
    }

    [Fact]
    public void Next_steps_from_the_selected_entry_even_if_the_frame_lies_well_before_it()
    {
        var model = new AnnotationModel(Video);
        model.Add(At(10));
        model.Add(At(20));
        model.Add(At(20.05, ImageMotion.RightToLeft));
        model.SelectedIndex = 1;

        // A gap in the recording: the frame shown for the entry at 20 s is at 19.5 s.
        Assert.Equal(1, model.Neighbour(TimeSpan.FromSeconds(19.5), forward: true));
        Assert.Equal(2, model.Neighbour(TimeSpan.FromSeconds(19.5), forward: true, fromSelected: true));
        Assert.Equal(0, model.Neighbour(TimeSpan.FromSeconds(19.5), forward: false, fromSelected: true));
    }

    [Fact]
    public void Next_with_skip_cars_finds_the_next_other_class()
    {
        var model = new AnnotationModel(Video);
        model.Add(At(10));
        model.Add(At(20));
        model.Add(At(30, vehicleClass: VehicleClass.Van));
        model.Add(At(40, vehicleClass: null));
        model.SelectedIndex = 0;

        Assert.Equal(2, model.Neighbour(TimeSpan.FromSeconds(10), forward: true, fromSelected: true, skipCars: true));
        Assert.Equal(3, model.Neighbour(TimeSpan.FromSeconds(35), forward: true, skipCars: true));
        Assert.Null(model.Neighbour(TimeSpan.FromSeconds(25), forward: false, skipCars: true));
    }

    [Fact]
    public void Counts_per_class_and_direction()
    {
        var model = new AnnotationModel(Video);
        model.Add(At(1));
        model.Add(At(2, ImageMotion.RightToLeft));
        model.Add(At(3, vehicleClass: VehicleClass.Bicycle));

        IReadOnlyList<ClassCount> counts = model.Counts();

        Assert.Equal([new ClassCount(VehicleClass.Car, 1, 1), new ClassCount(VehicleClass.Bicycle, 0, 1)], counts);
    }

    [Fact]
    public void Timeline_shows_the_rarest_class_of_a_stretch_and_where_speeds_are()
    {
        var model = new AnnotationModel(Video);
        model.Add(At(1));
        model.Add(At(2));
        model.Add(At(3, vehicleClass: VehicleClass.Bicycle));
        model.Add(At(60));
        model.SetSpeed(30);

        IReadOnlyList<TimelineMark> marks = model.Marks(TimeSpan.FromSeconds(100), buckets: 10);

        Assert.Equal([new TimelineMark(0, VehicleClass.Bicycle, false), new TimelineMark(6, VehicleClass.Car, true)], marks);
    }
}
