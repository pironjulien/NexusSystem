using Xunit;
using Nexus.Core;

namespace Nexus.Core.Tests;

public class SetupChecksTests
{
    [Fact]
    public void PhoneStatesComeFromAdbDevices()
    {
        Assert.Equal(new Phone(PhoneState.Connected, "SM F971B"), SetupChecks.ParseDevices(
            "List of devices attached\n192.168.1.67:42957     device product:h8qxeea model:SM_F971B device:h8q transport_id:5\n"));
        Assert.Equal(PhoneState.Unauthorized, SetupChecks.ParseDevices("List of devices attached\nR58N123 unauthorized usb:1-1 transport_id:2\n").State);
        Assert.Equal(PhoneState.None, SetupChecks.ParseDevices("* daemon started successfully\nList of devices attached\n\n").State);
    }

    [Fact]
    public void GmailIsConnectedOnlyWithItsScopes()
    {
        var ok = SetupChecks.ParseGmail("""{"present": true, "account": "a@b.c", "scopes": {"reauth_required": false}}""", "x.exe");
        Assert.Equal((true, "a@b.c"), (ok.Connected, ok.Account));
        Assert.False(SetupChecks.ParseGmail("""{"present": true, "scopes": {"reauth_required": true}}""", "x.exe").Connected);
        Assert.False(SetupChecks.ParseGmail("""{"present": false}""", "x.exe").Connected);
        Assert.False(SetupChecks.ParseGmail("not json", null).Connected);
    }

    [Fact]
    public void KeypadKeysAreReadFromTheGeneralProfile()
    {
        using var profile = new FakeProfile();
        Assert.False(Declarations.KeypadKeysPlaced(profile.Folders));
        profile.Write(@"AppData\Local\Logi\LogiPluginService\Applications\Loupedeck70\@_defaultwin\ApplicationInfo.json", """{"defaultProfileName": "P1"}""");
        profile.Write(@"AppData\Local\Logi\LogiPluginService\Applications\Loupedeck70\@_defaultwin\Profiles\P1\ProfileInfo.json", """{"a": "$Other___x"}""");
        Assert.False(Declarations.KeypadKeysPlaced(profile.Folders));
        profile.Write(@"AppData\Local\Logi\LogiPluginService\Applications\Loupedeck70\@_defaultwin\Profiles\P1\ProfileInfo.json", """{"a": "$NexusKeypad___Loupedeck.TrinitePlugin.ToucheCommand___t1"}""");
        Assert.True(Declarations.KeypadKeysPlaced(profile.Folders));
    }

    [Fact]
    public void ACatalogCopyIsTheSameProduct()
    {
        var copy = Products.Browser with { AboutFr = "autre texte", Group = "ai", Banner = "https://julienpiron.fr/x.webp" };
        Assert.True(copy == Products.Browser);
        Assert.Equal(Products.Browser.GetHashCode(), copy.GetHashCode());
    }
}
