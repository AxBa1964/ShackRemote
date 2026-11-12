namespace ShackRemote.Core.Media;

/// <summary>
/// Server: listet alle Bildschirme über ALLE Grafikkarten (z. B. Prozessorgrafik + Steckkarte),
/// mit DXGI-Adapter und -Ausgang (so zählt ddagrab), Hersteller (für die Wahl des Encoders),
/// Windows-Anzeigenummer und Lage auf dem Desktop.
/// </summary>
public static unsafe class DxgiOutputs
{
    private const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;

    /// <summary>Alle Monitore aller Grafikkarten; Index = laufende Nummer für StartVideo.</summary>
    public static List<MonitorInfo> List()
    {
        var result = new List<MonitorInfo>();
        Com.Check(DxgiNative.CreateDXGIFactory1(Com.IID_IDXGIFactory1, out var factory), "CreateDXGIFactory1");
        try
        {
            for (uint a = 0; ; a++)
            {
                IntPtr adapter;
                // IDXGIFactory1::EnumAdapters1 = 12; gleiche Reihenfolge wie EnumAdapters (danach wählt FFmpeg das Gerät)
                int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Com.Vtbl(factory)[12])(factory, a, &adapter);
                if (hr < 0) break;   // DXGI_ERROR_NOT_FOUND: keine weiteren Grafikkarten
                try
                {
                    DxgiAdapterDesc1 desc;
                    // IDXGIAdapter1::GetDesc1 = 10
                    Com.Check(((delegate* unmanaged[Stdcall]<IntPtr, DxgiAdapterDesc1*, int>)Com.Vtbl(adapter)[10])(adapter, &desc), "GetDesc1");
                    if ((desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0) continue;   // "Microsoft Basic Render Driver"
                    var adapterName = new string(desc.Description);

                    for (uint o = 0; ; o++)
                    {
                        IntPtr output;
                        // IDXGIAdapter::EnumOutputs = 7
                        hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Com.Vtbl(adapter)[7])(adapter, o, &output);
                        if (hr < 0) break;
                        try
                        {
                            DxgiOutputDesc od;
                            // IDXGIOutput::GetDesc = 7
                            Com.Check(((delegate* unmanaged[Stdcall]<IntPtr, DxgiOutputDesc*, int>)Com.Vtbl(output)[7])(output, &od), "GetDesc");
                            if (od.AttachedToDesktop == 0) continue;
                            var device = new string(od.DeviceName);
                            var r = od.DesktopCoordinates;
                            result.Add(new MonitorInfo
                            {
                                Index = result.Count,
                                Adapter = (int)a,
                                Output = (int)o,
                                Vendor = desc.VendorId,
                                AdapterName = adapterName,
                                Display = DisplayNumber(device) ?? result.Count + 1,
                                DeviceName = device,
                                Left = r.Left,
                                Top = r.Top,
                                Width = r.Right - r.Left,
                                Height = r.Bottom - r.Top,
                                Primary = r.Left == 0 && r.Top == 0,
                            });
                        }
                        finally
                        {
                            Com.Release(ref output);
                        }
                    }
                }
                finally
                {
                    Com.Release(ref adapter);
                }
            }
        }
        finally
        {
            Com.Release(ref factory);
        }
        return result;
    }

    private static int? DisplayNumber(string deviceName)
    {
        // "\\.\DISPLAY3" -> 3
        int pos = deviceName.LastIndexOf("DISPLAY", StringComparison.OrdinalIgnoreCase);
        if (pos < 0) return null;
        var digits = new string(deviceName.Skip(pos + 7).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out int n) ? n : null;
    }
}
