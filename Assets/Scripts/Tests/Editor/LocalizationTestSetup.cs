using CorruptionDuPortail.Domain.Localization;
using NUnit.Framework;

/// <summary>
/// T36: every EditMode test runs in French, the source language the assertions are written in, whatever language the
/// developer picked in the game or the machine speaks. No namespace on purpose: an NUnit SetUpFixture outside any
/// namespace covers the whole assembly. A test about another language sets it itself and puts French back.
/// </summary>
[SetUpFixture]
public class LocalizationTestSetup
{
    [OneTimeSetUp]
    public void ForceFrench() => Loc.SetLanguage(GameLanguage.French);
}
