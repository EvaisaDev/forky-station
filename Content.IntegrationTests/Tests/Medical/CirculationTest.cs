using Content.IntegrationTests.Fixtures;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class CirculationTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = $@"
- type: damageContainer
  id: CircDmgCont
  supportedGroups:
    - Brute
    - Burn

- type: entity
  id: CircBody
  components:
  - type: Damageable
    damageContainer: CircDmgCont
  - type: Injurable
    damageContainer: CircDmgCont
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      100: Critical
      200: Dead
  - type: Body
  - type: BloodOxygenation
  - type: BloodType
    group: O
    rhPositive: true

- type: entity
  id: CircBrain
  components:
  - type: Organ
    category: Brain
  - type: Brain
    integrity: 100
    maxIntegrity: 100

- type: entity
  id: CircHeart
  components:
  - type: Organ
  - type: HeartCondition
    efficiency: 1.0
    beating: true

- type: entity
  id: CircLungs
  components:
  - type: Organ
  - type: LungCondition
    efficiency: 1.0
";

    private async Task<(EntityUid Body, EntityUid Heart, EntityUid Lungs)> Spawn()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();
        var body = EntityUid.Invalid;
        var heart = EntityUid.Invalid;
        var lungs = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var cs = entMan.System<SharedContainerSystem>();
            body = entMan.SpawnEntity("CircBody", map.MapCoords);
            cs.EnsureContainer<Container>(body, BodyComponent.ContainerID, out _);
            var c = cs.GetContainer(body, BodyComponent.ContainerID);
            var brain = entMan.SpawnEntity("CircBrain", map.MapCoords);
            cs.Insert(brain, c);
            heart = entMan.SpawnEntity("CircHeart", map.MapCoords);
            cs.Insert(heart, c);
            lungs = entMan.SpawnEntity("CircLungs", map.MapCoords);
            cs.Insert(lungs, c);
        });
        await server.WaitRunTicks(5);
        return (body, heart, lungs);
    }

    [Test]
    public async Task OxygenationStartsHealthy()
    {
        var server = Pair.Server;
        var (body, _, _) = await Spawn();
        await server.WaitAssertion(() =>
        {
            var oxy = server.EntMan.GetComponent<BloodOxygenationComponent>(body);
            Assert.That(oxy.Oxygenation, Is.EqualTo(1.0f).Within(0.01f));
            Assert.That(oxy.PulseLevel, Is.EqualTo(2));
            Assert.That(oxy.CardiacArrest, Is.False);
        });
    }

    [Test]
    public async Task SetArrestStopsPulse()
    {
        var server = Pair.Server;
        var (body, _, _) = await Spawn();
        await server.WaitAssertion(() =>
        {
            var oxy = server.EntMan.GetComponent<BloodOxygenationComponent>(body);
            var sys = server.EntMan.System<BloodOxygenationSystem>();
            sys.SetCardiacArrest(body, oxy, true);
            Assert.That(oxy.CardiacArrest, Is.True);
            Assert.That(oxy.PulseLevel, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task RestartHeartWhenStable()
    {
        var server = Pair.Server;
        var (body, _, _) = await Spawn();
        await server.WaitAssertion(() =>
        {
            var oxy = server.EntMan.GetComponent<BloodOxygenationComponent>(body);
            var sys = server.EntMan.System<BloodOxygenationSystem>();
            sys.SetCardiacArrest(body, oxy, true);
            var restarted = sys.RestartHeart(body);
            Assert.That(restarted, Is.True);
            Assert.That(oxy.CardiacArrest, Is.False);
        });
    }

    [Test]
    public async Task RestartHeartFailsWhenO2Low()
    {
        var server = Pair.Server;
        var (body, _, lungs) = await Spawn();
        await server.WaitAssertion(() =>
        {
            var oxy = server.EntMan.GetComponent<BloodOxygenationComponent>(body);
            var lung = server.EntMan.GetComponent<LungConditionComponent>(lungs);
            var sys = server.EntMan.System<BloodOxygenationSystem>();

            lung.Efficiency = 0f;
            server.EntMan.Dirty(lungs, lung);
            oxy.Oxygenation = 0.5f;
            sys.SetCardiacArrest(body, oxy, true);

            var restarted = sys.RestartHeart(body);
            Assert.That(restarted, Is.False);
            Assert.That(oxy.CardiacArrest, Is.True);
        });
    }

    [Test]
    public async Task HeartConditionFlags()
    {
        var server = Pair.Server;
        var (_, heart, _) = await Spawn();
        await server.WaitAssertion(() =>
        {
            var hc = server.EntMan.GetComponent<HeartConditionComponent>(heart);
            Assert.That(hc.Beating, Is.True);
            Assert.That(hc.Efficiency, Is.EqualTo(1.0f).Within(0.01f));
        });
    }

    [Test]
    public async Task LungConditionFlags()
    {
        var server = Pair.Server;
        var (_, _, lungs) = await Spawn();
        await server.WaitAssertion(() =>
        {
            var lc = server.EntMan.GetComponent<LungConditionComponent>(lungs);
            Assert.That(lc.Efficiency, Is.EqualTo(1.0f).Within(0.01f));
            lc.Efficiency = 0.3f;
            server.EntMan.Dirty(lungs, lc);
            Assert.That(lc.Efficiency, Is.EqualTo(0.3f).Within(0.01f));
        });
    }

    [Test]
    public async Task BloodTypeCompatibility()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var oNeg = new BloodTypeComponent { Group = BloodGroup.O, RhPositive = false };
            Assert.That(oNeg.IsCompatibleWith(BloodGroup.O, false), Is.True);
            Assert.That(oNeg.IsCompatibleWith(BloodGroup.A, false), Is.False);

            var aPos = new BloodTypeComponent { Group = BloodGroup.A, RhPositive = true };
            Assert.That(aPos.IsCompatibleWith(BloodGroup.O, true), Is.True);
            Assert.That(aPos.IsCompatibleWith(BloodGroup.B, true), Is.False);

            var abPos = new BloodTypeComponent { Group = BloodGroup.AB, RhPositive = true };
            Assert.That(abPos.IsCompatibleWith(BloodGroup.A, true), Is.True);
            Assert.That(abPos.IsCompatibleWith(BloodGroup.O, false), Is.True);

            var abNeg = new BloodTypeComponent { Group = BloodGroup.AB, RhPositive = false };
            Assert.That(abNeg.IsCompatibleWith(BloodGroup.A, true), Is.False);
            Assert.That(abNeg.IsCompatibleWith(BloodGroup.A, false), Is.True);
        });
    }
}
