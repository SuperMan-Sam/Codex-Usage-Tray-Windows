using System.Runtime.InteropServices;

namespace CodexUsageTray.Widgets;

internal sealed class WidgetProviderRegistrationService : IDisposable
{
    public static readonly Guid ProviderClassId = Guid.Parse("2d07364d-dd8b-4c1a-b709-022ff3188385");

    private readonly object _factory;
    private uint _registrationCookie;

    private WidgetProviderRegistrationService(object factory, uint registrationCookie)
    {
        _factory = factory;
        _registrationCookie = registrationCookie;
    }

    public static WidgetProviderRegistrationService? TryRegister()
    {
        object factory = new WidgetProviderFactory<CodexUsageWidgetProvider>();
        int result = CoRegisterClassObject(ProviderClassId, factory, ClsctxLocalServer, RegclsMultipleUse, out uint cookie);
        if (result < 0)
        {
            return null;
        }

        return new WidgetProviderRegistrationService(factory, cookie);
    }

    public void Dispose()
    {
        GC.KeepAlive(_factory);

        if (_registrationCookie != 0)
        {
            CoRevokeClassObject(_registrationCookie);
            _registrationCookie = 0;
        }
    }

    private const uint ClsctxLocalServer = 0x4;
    private const uint RegclsMultipleUse = 0x1;

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
        [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        uint dwClsContext,
        uint flags,
        out uint lpdwRegister);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint dwRegister);
}
