using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace CodexUsageTray.Widgets;

internal static class WidgetProviderComGuids
{
    public const string IClassFactory = "00000001-0000-0000-C000-000000000046";
    public const string IUnknown = "00000000-0000-0000-C000-000000000046";
}

[ComImport]
[ComVisible(false)]
[Guid(WidgetProviderComGuids.IClassFactory)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);

    [PreserveSig]
    int LockServer(bool fLock);
}

[ComVisible(true)]
internal sealed class WidgetProviderFactory<T> : IClassFactory
    where T : IWidgetProvider, new()
{
    public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
    {
        ppvObject = IntPtr.Zero;

        if (pUnkOuter != IntPtr.Zero)
        {
            return ClassENoAggregation;
        }

        if (riid == Guid.Parse(WidgetProviderComGuids.IUnknown)
            || riid == typeof(IWidgetProvider).GUID
            || riid == typeof(T).GUID)
        {
            ppvObject = MarshalInspectable<IWidgetProvider>.FromManaged(new T());
            return 0;
        }

        return ENoInterface;
    }

    public int LockServer(bool fLock)
    {
        return 0;
    }

    private const int ClassENoAggregation = unchecked((int)0x80040110);
    private const int ENoInterface = unchecked((int)0x80004002);
}
