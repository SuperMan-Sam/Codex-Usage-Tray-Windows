using CodexUsageTray.Core.Models;
using CodexUsageTray.Core.Services;

namespace CodexUsageTray.Tests.Services;

[TestClass]
public sealed class TrayIconStateResolverTests
{
    [TestMethod]
    public void Resolve_WhenRemainingIsAboveTwenty_ReturnsNormal()
    {
        UsageSnapshot snapshot = new("70% remaining", "30% used", string.Empty, 70, DateTimeOffset.Now, UsageStatus.Available);

        TrayIconState state = TrayIconStateResolver.Resolve(snapshot);

        Assert.AreEqual(TrayIconState.Normal, state);
    }

    [TestMethod]
    public void Resolve_WhenRemainingIsTwenty_ReturnsWarning()
    {
        UsageSnapshot snapshot = new("20% remaining", "80% used", string.Empty, 20, DateTimeOffset.Now, UsageStatus.Available);

        TrayIconState state = TrayIconStateResolver.Resolve(snapshot);

        Assert.AreEqual(TrayIconState.Warning, state);
    }

    [TestMethod]
    public void Resolve_WhenRemainingIsTen_ReturnsCritical()
    {
        UsageSnapshot snapshot = new("10% remaining", "90% used", string.Empty, 10, DateTimeOffset.Now, UsageStatus.Available);

        TrayIconState state = TrayIconStateResolver.Resolve(snapshot);

        Assert.AreEqual(TrayIconState.Critical, state);
    }

    [TestMethod]
    public void Resolve_WhenStatusIsUnknown_ReturnsUnknown()
    {
        UsageSnapshot snapshot = UsageSnapshot.Unknown(DateTimeOffset.Now);

        TrayIconState state = TrayIconStateResolver.Resolve(snapshot);

        Assert.AreEqual(TrayIconState.Unknown, state);
    }
}
