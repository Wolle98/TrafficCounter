using System.Xml.Linq;

namespace TrafficCounter.Service.Tests;

public class DisplayParamSwitchTests
{
    /// <summary>Two months of the camera's schedule (ISAPI DisplayParamSwitch, mode "month").</summary>
    private static XDocument Schedule(string mode = "month") => XDocument.Parse($"""
        <?xml version="1.0" encoding="UTF-8"?>
        <DisplayParamSwitch version="2.0" xmlns="http://www.std-cgi.com/ver20/XMLSchema">
        <mode>{mode}</mode>
        <Month><PeriodList size="12">
        <Period><id>10</id><TimeRangeList size="4">
        <TimeRange><beginTime>00:00</beginTime><endTime>08:30</endTime><linkScene>lowIllumination</linkScene></TimeRange>
        <TimeRange><beginTime>08:30</beginTime><endTime>15:00</endTime><linkScene>normal</linkScene></TimeRange>
        <TimeRange><beginTime>15:00</beginTime><endTime>18:00</endTime><linkScene>backlight</linkScene></TimeRange>
        <TimeRange><beginTime>18:00</beginTime><endTime>24:00</endTime><linkScene>lowIllumination</linkScene></TimeRange>
        </TimeRangeList></Period>
        <Period><id>11</id><TimeRangeList size="4">
        <TimeRange><beginTime>00:00</beginTime><endTime>08:00</endTime><linkScene>lowIllumination</linkScene></TimeRange>
        <TimeRange><beginTime>08:00</beginTime><endTime>13:30</endTime><linkScene>normal</linkScene></TimeRange>
        <TimeRange><beginTime>13:30</beginTime><endTime>16:30</endTime><linkScene>backlight</linkScene></TimeRange>
        <TimeRange><beginTime>16:30</beginTime><endTime>24:00</endTime><linkScene>lowIllumination</linkScene></TimeRange>
        </TimeRangeList></Period>
        </PeriodList></Month>
        </DisplayParamSwitch>
        """);

    private static string[] Bounds(XDocument doc, int month)
    {
        XNamespace ns = doc.Root!.Name.Namespace;
        XElement period = doc.Root.Descendants(ns + "Period").Single(p => (string?)p.Element(ns + "id") == month.ToString());
        return [.. period.Descendants(ns + "TimeRange").Select(r => $"{(string)r.Element(ns + "beginTime")!}-{(string)r.Element(ns + "endTime")!}")];
    }

    [Fact]
    public void Day_and_night_start_of_the_current_month_are_moved_the_backlight_range_kept()
    {
        XDocument doc = Schedule();
        DisplayParamSwitchChange change = DisplayParamSwitch.Adjust(doc, 10, new TimeOnly(7, 30), new TimeOnly(17, 0));

        Assert.True(change.Changed);
        Assert.Equal(["00:00-07:30", "07:30-15:00", "15:00-17:00", "17:00-24:00"], Bounds(doc, 10));
        Assert.Equal(["00:00-08:00", "08:00-13:30", "13:30-16:30", "16:30-24:00"], Bounds(doc, 11)); // other months untouched
    }

    [Fact]
    public void A_boundary_in_between_is_moved_only_to_stay_inside_the_day()
    {
        XDocument doc = Schedule();
        DisplayParamSwitch.Adjust(doc, 11, new TimeOnly(8, 30), new TimeOnly(13, 0));

        Assert.Equal(["00:00-08:30", "08:30-12:30", "12:30-13:00", "13:00-24:00"], Bounds(doc, 11));
    }

    [Fact]
    public void Right_times_and_other_modes_are_left_alone()
    {
        Assert.False(DisplayParamSwitch.Adjust(Schedule(), 10, new TimeOnly(8, 30), new TimeOnly(18, 0)).Changed);
        Assert.False(DisplayParamSwitch.Adjust(Schedule("disable"), 10, new TimeOnly(7, 30), new TimeOnly(17, 0)).Changed);
    }
}
