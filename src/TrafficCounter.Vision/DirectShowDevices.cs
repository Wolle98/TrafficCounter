using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;

namespace TrafficCounter.Vision;

/// <summary>
/// Names of the DirectShow video capture devices in DirectShow order, which is the index OpenCV's DSHOW backend
/// uses. The index of a camera changes when a webcam is plugged in or the order of USB enumeration changes; the
/// name does not (on the notebook, 0 is the built-in webcam and the street camera 1).
/// </summary>
public static class DirectShowDevices
{
    private static readonly Guid SystemDeviceEnum = new("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
    private static readonly Guid VideoInputDeviceCategory = new("860BB310-5D01-11d0-BD3B-00A0C911CE86");
    private static readonly Guid PropertyBagId = new("55272A00-42CB-11CE-8135-00AA004BB851");

    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<string> List()
    {
        List<string> names = [];
        Type? type = Type.GetTypeFromCLSID(SystemDeviceEnum);
        if (type is null || Activator.CreateInstance(type) is not ICreateDevEnum devEnum)
        {
            return names;
        }

        try
        {
            Guid category = VideoInputDeviceCategory;
            // S_FALSE (1): the category is empty.
            if (devEnum.CreateClassEnumerator(ref category, out IEnumMoniker? monikers, 0) != 0 || monikers is null)
            {
                return names;
            }

            var batch = new IMoniker[1];
            while (monikers.Next(1, batch, IntPtr.Zero) == 0)
            {
                IMoniker moniker = batch[0];
                try
                {
                    Guid bagId = PropertyBagId;
                    moniker.BindToStorage(null!, null, ref bagId, out object bagObject);
                    var bag = (IPropertyBag)bagObject;
                    object value = "";
                    names.Add(bag.Read("FriendlyName", ref value, IntPtr.Zero) == 0 ? value as string ?? "" : "");
                    Marshal.ReleaseComObject(bag);
                }
                finally
                {
                    Marshal.ReleaseComObject(moniker);
                }
            }

            Marshal.ReleaseComObject(monikers);
        }
        finally
        {
            Marshal.ReleaseComObject(devEnum);
        }

        return names;
    }

    /// <summary>Index of the first device whose name contains <paramref name="name"/> (case-insensitive), or null.</summary>
    [SupportedOSPlatform("windows")]
    public static int? Find(string name)
    {
        IReadOnlyList<string> names = List();
        for (int i = 0; i < names.Count; i++)
        {
            if (names[i].Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return null;
    }

    [ComImport]
    [Guid("29840822-5B84-11D0-BD3B-00A0C911CE86")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateDevEnum
    {
        [PreserveSig]
        int CreateClassEnumerator(ref Guid type, out IEnumMoniker? enumMoniker, int flags);
    }

    [ComImport]
    [Guid("55272A00-42CB-11CE-8135-00AA004BB851")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyBag
    {
        [PreserveSig]
        int Read([MarshalAs(UnmanagedType.LPWStr)] string propertyName, ref object value, IntPtr errorLog);

        [PreserveSig]
        int Write([MarshalAs(UnmanagedType.LPWStr)] string propertyName, ref object value);
    }
}
