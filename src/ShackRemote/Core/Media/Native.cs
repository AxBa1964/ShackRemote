using System.Runtime.InteropServices;

namespace ShackRemote.Core.Media;

/// <summary>
/// Schlanker COM-Zugriff ohne Zusatzbibliothek: Methoden werden über ihre Position in der
/// Funktionstabelle (vtable) aufgerufen. Die Positionen stammen aus d3d11.idl / dxgi.idl / dxgi1_2.idl
/// (IUnknown = 0..2, IDXGIObject bzw. ID3D11DeviceChild = 3..6).
/// </summary>
internal static unsafe class Com
{
    public static void** Vtbl(IntPtr p) => *(void***)p;

    public static void Release(ref IntPtr p)
    {
        if (p == IntPtr.Zero) return;
        ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Vtbl(p)[2])(p);
        p = IntPtr.Zero;
    }

    public static IntPtr QueryInterface(IntPtr p, Guid iid)
    {
        IntPtr result;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Vtbl(p)[0])(p, &iid, &result);
        Check(hr, "QueryInterface");
        return result;
    }

    public static void Check(int hr, string what)
    {
        if (hr < 0) throw new COMException($"{what} fehlgeschlagen (0x{hr:X8})", hr);
    }

    public static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    public static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    public static readonly Guid IID_IDXGIFactory2 = new("50c83a1c-e072-4c48-87b0-3630fa36a6d0");
    public static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
}

// ---- Strukturen (Layout wie in den Windows-Headern) ------------------------------------------------

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left, Top, Right, Bottom;
    public RECT(int l, int t, int r, int b) { Left = l; Top = t; Right = r; Bottom = b; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct DXGI_RATIONAL { public uint Numerator, Denominator; }

/// <summary>D3D11_VIDEO_PROCESSOR_CONTENT_DESC</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct VpContentDesc
{
    public uint InputFrameFormat;          // 0 = progressiv
    public DXGI_RATIONAL InputFrameRate;
    public uint InputWidth, InputHeight;
    public DXGI_RATIONAL OutputFrameRate;
    public uint OutputWidth, OutputHeight;
    public uint Usage;                     // 0 normal, 1 Tempo, 2 Qualität
}

/// <summary>D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC (Texture2D-Variante)</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct VpInputViewDesc
{
    public uint FourCC;
    public uint ViewDimension;             // 1 = TEXTURE2D
    public uint MipSlice, ArraySlice;
}

/// <summary>D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC (Union auf 12 Byte aufgefüllt)</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct VpOutputViewDesc
{
    public uint ViewDimension;             // 1 = TEXTURE2D
    public uint MipSlice, Unused1, Unused2;
}

/// <summary>D3D11_VIDEO_PROCESSOR_STREAM (x64: 72 Byte)</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct VpStream
{
    public int Enable;
    public uint OutputIndex, InputFrameOrField, PastFrames, FutureFrames;
    public IntPtr PastSurfaces, InputSurface, FutureSurfaces, PastSurfacesRight, InputSurfaceRight, FutureSurfacesRight;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VideoColorRgba { public float R, G, B, A; }

/// <summary>DXGI_SWAP_CHAIN_DESC1</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SwapChainDesc1
{
    public uint Width, Height, Format;
    public int Stereo;
    public uint SampleCount, SampleQuality;
    public uint BufferUsage, BufferCount, Scaling, SwapEffect, AlphaMode, Flags;
}

/// <summary>DXGI_OUTPUT_DESC</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct DxgiOutputDesc
{
    public fixed char DeviceName[32];
    public RECT DesktopCoordinates;
    public int AttachedToDesktop;
    public uint Rotation;
    public IntPtr Monitor;
}

internal static partial class DxgiNative
{
    [LibraryImport("dxgi.dll")]
    public static partial int CreateDXGIFactory1(in Guid riid, out IntPtr factory);
}
