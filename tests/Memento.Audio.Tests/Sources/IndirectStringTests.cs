using Memento.Audio.Sources;

namespace Memento.Audio.Tests.Sources;

/// <summary>
/// Which session display names Memento resolves with SHLoadIndirectString: only installed package resources and modules
/// under Windows or Program Files, never a network share (resolving one would send NTLM credentials to its host).
/// </summary>
public sealed class IndirectStringTests
{
    private static readonly string[] Roots = [@"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)"];

    [Theory]
    [InlineData(@"@C:\Windows\System32\AudioSrv.Dll,-202")]
    [InlineData(@"@c:\windows\system32\audiosrv.dll,202")]
    [InlineData(@"@C:/Windows/System32/AudioSrv.Dll,-202")]
    [InlineData(@"@C:\Program Files\Vendor\App\app.exe,-1;v2")]
    [InlineData(@"@C:\Program Files (x86)\Vendor\app.dll,-7")]
    [InlineData("@{Microsoft.WindowsCalculator_11.2405.2.0_x64__8wekyb3d8bbwe?ms-resource://Microsoft.WindowsCalculator/Resources/AppStoreName}")]
    [InlineData("@{Contoso.App_1.0.0.0_neutral__abcdefghjkmnp?ms-resource:AppName}")]
    public void LocalSystemResourcesAreResolved(string source) =>
        Assert.True(ProcessInfoNative.IsLocalResource(source, Roots));

    [Theory]
    [InlineData(@"@\\attacker.example\share\x.dll,-1")]
    [InlineData(@"@//attacker.example/share/x.dll,-1")]
    [InlineData(@"@\/attacker.example\share\x.dll,-1")]
    [InlineData(@"@\\?\UNC\attacker.example\share\x.dll,-1")]
    [InlineData(@"@\\?\C:\Windows\System32\x.dll,-1")]
    [InlineData(@"@\\.\pipe\x,-1")]
    [InlineData(@"@\??\UNC\attacker.example\share\x.dll,-1")]
    [InlineData(@"@{\\attacker.example\share\resources.pri?ms-resource://x/y}")]
    [InlineData(@"@{D:\Downloads\resources.pri?ms-resource://x/y}")]
    [InlineData(@"@D:\Downloads\evil.dll,-1")]
    [InlineData(@"@C:\Windows\..\Temp\evil.dll,-1")]
    [InlineData(@"@C:\WindowsEvil\x.dll,-1")]
    [InlineData(@"@C:\Program Files Evil\x.dll,-1")]
    [InlineData(@"@x.dll,-1")]
    [InlineData(@"@System32\x.dll,-1")]
    [InlineData(@"@C:x.dll,-1")]
    [InlineData(@"@\Windows\System32\x.dll,-1")]
    [InlineData(@"@C:\Windows\System32\x.dll")]
    [InlineData(@"@C:\Windows\System32\x.dll,abc")]
    [InlineData(@"@%MEMENTO_UNSET_VARIABLE_FOR_TESTS%\x.dll,-1")]
    [InlineData(@"C:\Windows\System32\x.dll,-1")]
    [InlineData("@")]
    [InlineData("")]
    public void AnythingElseIsNotResolved(string source) =>
        Assert.False(ProcessInfoNative.IsLocalResource(source, Roots));

    [Fact]
    public void EnvironmentVariablesExpandBeforeTheCheck()
    {
        Assert.True(ProcessInfoNative.IsLocalResource(@"@%SystemRoot%\System32\AudioSrv.Dll,-202"));
        Assert.False(ProcessInfoNative.IsLocalResource(@"@%USERPROFILE%\evil.dll,-1"));
        Assert.Null(ProcessInfoNative.LoadIndirectString(@"@\\attacker.example\share\x.dll,-1"));
        Assert.Null(AppIdentity.CleanDisplayName(@"@\\attacker.example\share\x.dll,-1"));
    }
}
