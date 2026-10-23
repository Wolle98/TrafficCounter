using Avalonia.Media;
using TrafficCounter.Core;

namespace TrafficCounter.Annotator;

/// <summary>One colour and display name per vehicle class: quick buttons, timeline and counts use the same.</summary>
internal static class ClassColors
{
    /// <summary>Classes with a quick button, in the order shown.</summary>
    public static readonly VehicleClass[] QuickClasses =
    [
        VehicleClass.Car, VehicleClass.Van, VehicleClass.Truck, VehicleClass.Bus,
        VehicleClass.Motorcycle, VehicleClass.Bicycle, VehicleClass.EScooter, VehicleClass.Agricultural,
    ];

    public static Color Of(VehicleClass? vehicleClass) => vehicleClass switch
    {
        VehicleClass.Car => Color.FromRgb(0x37, 0x8A, 0xDD),
        VehicleClass.Van => Color.FromRgb(0x1D, 0x9E, 0x75),
        VehicleClass.Truck => Color.FromRgb(0xD8, 0x5A, 0x30),
        VehicleClass.Bus => Color.FromRgb(0xBA, 0x75, 0x17),
        VehicleClass.Motorcycle => Color.FromRgb(0x7F, 0x77, 0xDD),
        VehicleClass.Bicycle => Color.FromRgb(0x63, 0x99, 0x22),
        VehicleClass.EScooter => Color.FromRgb(0xD4, 0x53, 0x7E),
        VehicleClass.Agricultural => Color.FromRgb(0x8C, 0x6D, 0x31),
        _ => Color.FromRgb(0x88, 0x87, 0x80),
    };

    public static IBrush BrushOf(VehicleClass? vehicleClass) => new SolidColorBrush(Of(vehicleClass));

    public static string NameOf(VehicleClass? vehicleClass) => vehicleClass switch
    {
        null => "unclassified",
        VehicleClass.EScooter => "E-scooter",
        VehicleClass.Agricultural => "Agricultural",
        _ => vehicleClass.ToString()!,
    };
}
