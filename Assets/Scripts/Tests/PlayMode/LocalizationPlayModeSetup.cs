using CorruptionDuPortail.Domain.Localization;
using NUnit.Framework;

/// <summary>
/// T36: every PlayMode test runs in French, the source language the assertions are written in. Entering play mode
/// picks the developer's saved or system language (<c>LocalizationRuntime</c>); this runs after it. No namespace on
/// purpose: an NUnit SetUpFixture outside any namespace covers the whole assembly.
/// </summary>
[SetUpFixture]
public class LocalizationPlayModeSetup
{
    [OneTimeSetUp]
    public void ForceFrench() => Loc.SetLanguage(GameLanguage.French);
}
