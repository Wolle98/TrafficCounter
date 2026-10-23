using System.Globalization;
using ClosedXML.Excel;
using Microsoft.Extensions.Localization;
using TrafficCounter.Core;

namespace TrafficCounter.App.Data;

/// <summary>Excel workbook of a period (roadmap stage 4): days, hours with coverage and weather, single vehicles.</summary>
public static class ExcelReport
{
    public static byte[] Create(
        CameraInfo camera, DateTime from, DateTime to, IReadOnlyList<PassageRow> passages, HashSet<DateTime> covered, IReadOnlyList<WeatherRow> weather,
        IStringLocalizer text)
    {
        using var workbook = new XLWorkbook();
        List<VehicleClass> classes = [.. TrafficStatistics.Classes.Select(c => c.Class).Where(c => passages.Any(p => p.Class == c))];

        AddBuckets(workbook.AddWorksheet(text["Excel.Days"]), text["Table.Day"], text["Format.ExcelDay"], TimeSpan.FromDays(1));
        AddBuckets(workbook.AddWorksheet(text["Excel.Hours"]), text["Table.Hour"], text["Format.ExcelHour"], TimeSpan.FromHours(1));

        IXLWorksheet single = workbook.AddWorksheet(text["Excel.Vehicles"]);
        string[] header = [text["Table.Time"], text["Table.Direction"], text["Table.Class"], text["Export.Speed"]];
        for (int c = 0; c < header.Length; c++)
        {
            single.Cell(1, c + 1).Value = header[c];
        }

        int row = 2;
        foreach (PassageRow passage in passages)
        {
            single.Cell(row, 1).Value = passage.At;
            single.Cell(row, 1).Style.DateFormat.Format = text["Format.ExcelDateTime"];
            single.Cell(row, 2).Value = camera.DirectionName(passage.Direction);
            single.Cell(row, 3).Value = text.ClassName(passage.Class);
            if (passage.SpeedKmh is float speed)
            {
                single.Cell(row, 4).Value = Math.Round(speed, 1);
            }

            row++;
        }

        Finish(single);

        IXLWorksheet info = workbook.AddWorksheet(text["Excel.Notes"]);
        info.Cell(1, 1).Value = text["Excel.Title", camera.Name, text.Date(from, "Format.DayRow"), text.Date(to.AddDays(-1), "Format.DayRow")].Value;
        info.Cell(2, 1).Value = text["Excel.NoteCounter"].Value;
        info.Cell(3, 1).Value = text["Excel.NotePerHour"].Value;
        info.Cell(4, 1).Value = text["Excel.NoteSpeed"].Value;
        info.Cell(5, 1).Value = text["Excel.NoteRain"].Value;
        info.Column(1).Width = 110;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();

        void AddBuckets(IXLWorksheet sheet, string label, string format, TimeSpan size)
        {
            List<Bucket> buckets = TrafficStatistics.Buckets(passages, covered, from, to, size, DateTime.Now);
            List<string> columns = [label, text["Table.Vehicles"], camera.DirectionA, camera.DirectionB, .. classes.Select(text.ClassName),
                text["Table.Measured"], text["Table.PerHour"], text["Excel.Rain"]];
            for (int c = 0; c < columns.Count; c++)
            {
                sheet.Cell(1, c + 1).Value = columns[c];
            }

            int r = 2;
            foreach (Bucket bucket in buckets.Where(b => b.Minutes > 0))
            {
                List<PassageRow> inBucket = [.. passages.Where(p => TrafficStatistics.Truncate(p.At, size) == bucket.Start)];
                int c = 1;
                sheet.Cell(r, c).Value = bucket.Start;
                sheet.Cell(r, c++).Style.DateFormat.Format = format;
                sheet.Cell(r, c++).Value = bucket.Count;
                sheet.Cell(r, c++).Value = inBucket.Count(p => p.Direction == TravelDirection.A);
                sheet.Cell(r, c++).Value = inBucket.Count(p => p.Direction == TravelDirection.B);
                foreach (VehicleClass vehicleClass in classes)
                {
                    sheet.Cell(r, c++).Value = inBucket.Count(p => p.Class == vehicleClass);
                }

                sheet.Cell(r, c).Value = bucket.Coverage;
                sheet.Cell(r, c++).Style.NumberFormat.Format = "0%";
                if (bucket.RatePerHour is double rate)
                {
                    sheet.Cell(r, c).Value = Math.Round(rate, 1);
                }

                c++;
                if (weather.Count > 0)
                {
                    double rain = weather.Where(w => TrafficStatistics.Truncate(w.Hour, size) == bucket.Start).Sum(w => (double)(w.PrecipitationMm ?? 0));
                    sheet.Cell(r, c).Value = Math.Round(rain, 1);
                }

                r++;
            }

            Finish(sheet);
        }
    }

    private static void Finish(IXLWorksheet sheet)
    {
        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        sheet.RangeUsed()?.SetAutoFilter();
        sheet.Columns().AdjustToContents();
    }
}
