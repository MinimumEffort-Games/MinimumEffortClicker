using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace MinimumEffortClicker.Vision;

internal static class CaptureNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint hwnd, ref Point point);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint hwnd, uint attribute, out Rect rect, int size);
    [DllImport("d3d11.dll")] internal static extern int D3D11CreateDevice(nint adapter, int driverType, nint software,
        uint flags, nint featureLevels, uint featureLevelCount, uint sdkVersion, out nint device, out int featureLevel, out nint context);
    [DllImport("d3d11.dll")] internal static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint device);

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, in Guid iid);
        nint CreateForMonitor(nint monitor, in Guid iid);
    }
    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess { nint GetInterface(in Guid iid); }
    internal static GraphicsCaptureItem Item(nint hwnd)
    {
        var factory = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        nint raw = factory.CreateForWindow(hwnd, new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760"));
        try { return GraphicsCaptureItem.FromAbi(raw); }
        finally { Marshal.Release(raw); }
    }
    internal static nint Texture(IDirect3DSurface surface) => surface.As<IDirect3DDxgiInterfaceAccess>()
        .GetInterface(new Guid("6F15AAF2-D208-4E89-9AB4-489535D34F9C"));
    internal static T Method<T>(nint instance, int slot) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));
}

/// <summary>One small CPU-readable staging region. Full frames remain on the capture device.</summary>
internal sealed class CaptureDevice : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct TextureDescription
    { public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccess, Misc; }
    [StructLayout(LayoutKind.Sequential)] private struct Box { public uint Left, Top, Front, Right, Bottom, Back; }
    [StructLayout(LayoutKind.Sequential)] private struct Mapped { public nint Data; public uint RowPitch, DepthPitch; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTexture(nint self, ref TextureDescription desc, nint initialData, out nint texture);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CopyRegion(nint self, nint destination, uint destinationSubresource, uint x, uint y, uint z, nint source, uint sourceSubresource, ref Box box);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int MapTexture(nint self, nint texture, uint subresource, uint map, uint flags, out Mapped mapped);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void UnmapTexture(nint self, nint texture, uint subresource);
    private nint device, context, staging;
    private readonly CopyRegion copy;
    private readonly MapTexture map;
    private readonly UnmapTexture unmap;
    private readonly int width, height;
    private readonly byte[] pixels;
    public IDirect3DDevice Device { get; }
    public CaptureDevice(int width, int height)
    {
        this.width = width; this.height = height; pixels = new byte[width * height * 4];
        try
        {
            int result = CaptureNative.D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out device, out _, out context);
            if (result < 0) Marshal.ThrowExceptionForHR(result);
            Guid dxgi = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, in dxgi, out var dxgiDevice));
            try
            {
                Marshal.ThrowExceptionForHR(CaptureNative.CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out var projected));
                try { Device = MarshalInterface<IDirect3DDevice>.FromAbi(projected); }
                finally { Marshal.Release(projected); }
            }
            finally { Marshal.Release(dxgiDevice); }
            var description = new TextureDescription { Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1,
                Format = 87, SampleCount = 1, Usage = 3, CpuAccess = 0x20000 };
            Marshal.ThrowExceptionForHR(CaptureNative.Method<CreateTexture>(device, 5)(device, ref description, 0, out staging));
            copy = CaptureNative.Method<CopyRegion>(context, 46);
            map = CaptureNative.Method<MapTexture>(context, 14);
            unmap = CaptureNative.Method<UnmapTexture>(context, 15);
        }
        catch { Dispose(); throw; }
    }
    public ReadOnlySpan<byte> Read(IDirect3DSurface surface, int x, int y)
    {
        nint source = CaptureNative.Texture(surface);
        try
        {
            var box = new Box { Left = (uint)x, Top = (uint)y, Right = (uint)(x + width), Bottom = (uint)(y + height), Back = 1 };
            copy(context, staging, 0, 0, 0, 0, source, 0, ref box);
            Marshal.ThrowExceptionForHR(map(context, staging, 0, 1, 0, out var mapped));
            try
            {
                for (int row = 0; row < height; row++) Marshal.Copy(mapped.Data + checked(row * (int)mapped.RowPitch), pixels, row * width * 4, width * 4);
            }
            finally { unmap(context, staging, 0); }
            return pixels;
        }
        finally { Marshal.Release(source); }
    }
    public void Dispose()
    {
        Device?.Dispose();
        if (staging != 0) { Marshal.Release(staging); staging = 0; }
        if (context != 0) { Marshal.Release(context); context = 0; }
        if (device != 0) { Marshal.Release(device); device = 0; }
    }
}
