namespace ShackRemote.Core.Media;

/// <summary>
/// Server: listet die Bildschirmausgänge der Grafikkarte so, wie ddagrab sie zählt (output_idx).
/// Dazu Windows-Anzeigenummer (aus "\\.\DISPLAYn") und Lage auf dem Desktop – damit der Client
/// die Knöpfe in derselben Reihenfolge wie in den Windows-Anzeigeeinstellungen zeigen kann.
/// </summary>
public static unsafe class DxgiOutputs
{
    public static List<MonitorInfo> List()
    {
        var result = new List<MonitorInfo>();
        Com.Check(DxgiNative.CreateDXGIFactory1(Com.IID_IDXGIFactory1, out var factory), "CreateDXGIFactory1");
        IntPtr adapter = IntPtr.Zero;
        try
        {
            // IDXGIFactory1::EnumAdapters1 = 12. Adapter 0 ist beim N100 die einzige (Intel-)GPU,
            // dieselbe, die ddagrab über vendor_id=0x8086 benutzt.
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Com.Vtbl(factory)[12])(factory, 0, &adapter);
            Com.Check(hr, "EnumAdapters1");

            for (uint i = 0; ; i++)
            {
                IntPtr output;
                // IDXGIAdapter::EnumOutputs = 7; DXGI_ERROR_NOT_FOUND (0x887A0002) = keine weiteren
                hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Com.Vtbl(adapter)[7])(adapter, i, &output);
                if (hr < 0) break;
                try
                {
                    DxgiOutputDesc desc;
                    // IDXGIOutput::GetDesc = 7
                    Com.Check(((delegate* unmanaged[Stdcall]<IntPtr, DxgiOutputDesc*, int>)Com.Vtbl(output)[7])(output, &desc), "GetDesc");
                    var device = new string(desc.DeviceName);
                    var r = desc.DesktopCoordinates;
                    result.Add(new MonitorInfo
                    {
                        Index = (int)i,
                        Display = DisplayNumber(device) ?? (int)i + 1,
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
