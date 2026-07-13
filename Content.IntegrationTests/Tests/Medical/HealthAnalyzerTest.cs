using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Shared.FixedPoint;
using Content.Shared.MedicalScanner;
using Content.Shared.Medical.Wounds;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class HealthAnalyzerTest : GameTest
{
    [Test]
    public async Task ScanStateHasBaystationFields()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var state = new HealthAnalyzerUiState(null, float.NaN, float.NaN, null, null, null);
            Assert.That(state.BrainActivity, Is.EqualTo("Normal"));
            Assert.That(state.Limbs, Is.Not.Null);
            Assert.That(state.Reagents, Is.Not.Null);
        });
    }

    [Test]
    public async Task ScanLimbData()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var limbs = new List<LimbScanData>
            {
                new() { Name = "Left Arm", BruteDamage = FixedPoint2.New(30), BurnDamage = FixedPoint2.New(10), Fractured = true, Bleeding = false },
                new() { Name = "Right Leg", BruteDamage = FixedPoint2.Zero, BurnDamage = FixedPoint2.New(15), Fractured = false, Bleeding = true }
            };
            var state = new HealthAnalyzerUiState(null, float.NaN, float.NaN, null, null, null)
            {
                Limbs = limbs,
                BrainActivity = "Fading",
                PulseRate = 120,
                BloodOxygenation = 75,
                HasFractures = true
            };
            Assert.That(state.Limbs.Count, Is.EqualTo(2));
            Assert.That(state.BrainActivity, Is.EqualTo("Fading"));
            Assert.That(state.HasFractures, Is.True);
        });
    }

    [Test]
    public async Task ScanReagentData()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var reagents = new List<ReagentScanData>
            {
                new() { Name = "Dylovene", Quantity = FixedPoint2.New(15) },
                new() { Name = "Bicaridine", Quantity = FixedPoint2.New(10) }
            };
            var state = new HealthAnalyzerUiState(null, float.NaN, float.NaN, null, null, null) { Reagents = reagents };
            Assert.That(state.Reagents.Count, Is.EqualTo(2));
        });
    }
}
