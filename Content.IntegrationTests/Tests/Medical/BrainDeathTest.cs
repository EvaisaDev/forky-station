using Content.IntegrationTests.Fixtures;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class BrainDeathTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = $@"
- type: damageContainer
  id: BrainDmgCont
  supportedGroups:
    - Brute
    - Burn

- type: entity
  id: BrainBody
  components:
  - type: Damageable
    damageContainer: BrainDmgCont
  - type: Injurable
    damageContainer: BrainDmgCont
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      100: Critical
      200: Dead
  - type: Body

- type: entity
  id: BrainOrgan
  components:
  - type: Organ
    category: Brain
  - type: Brain
    integrity: 100
    maxIntegrity: 100

- type: entity
  id: BrainLimb
  components:
  - type: Organ
  - type: ExternalOrgan
    maxDamage: 100
    minBrokenDamage: 50
    flags:
    - CanAmputate
    - CanBreak
";

    private async Task<(EntityUid Body, EntityUid Brain)> Spawn()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();
        var body = EntityUid.Invalid;
        var brain = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var cs = entMan.System<SharedContainerSystem>();
            body = entMan.SpawnEntity("BrainBody", map.MapCoords);
            cs.EnsureContainer<Container>(body, BodyComponent.ContainerID, out _);
            var c = cs.GetContainer(body, BodyComponent.ContainerID);
            brain = entMan.SpawnEntity("BrainOrgan", map.MapCoords);
            cs.Insert(brain, c);
            var limb = entMan.SpawnEntity("BrainLimb", map.MapCoords);
            cs.Insert(limb, c);
        });
        await server.WaitRunTicks(5);
        return (body, brain);
    }

    [Test]
    public async Task BrainDamageDecreasesIntegrity()
    {
        var server = Pair.Server;
        var (_, brain) = await Spawn();
        await server.WaitAssertion(() =>
        {
            var bs = server.EntMan.System<BrainSystem>();
            var bc = server.EntMan.GetComponent<BrainComponent>(brain);
            bs.TakeBrainDamage((brain, bc), FixedPoint2.New(30));
            var raw = bc.Integrity;
            var val = (float)raw.Float();
            Assert.That(val, Is.EqualTo(70f).Within(1f));
        });
    }

    [Test]
    public async Task BrainHealRestoresIntegrity()
    {
        var server = Pair.Server;
        var (_, brain) = await Spawn();
        await server.WaitAssertion(() =>
        {
            var bs = server.EntMan.System<BrainSystem>();
            var bc = server.EntMan.GetComponent<BrainComponent>(brain);
            bs.TakeBrainDamage((brain, bc), FixedPoint2.New(40));
            var raw = bc.Integrity;
            var v = (float)raw.Float();
            Assert.That(v, Is.EqualTo(60f).Within(1f));
            bs.HealBrainDamage((brain, bc), FixedPoint2.New(25));
            raw = bc.Integrity;
            v = (float)raw.Float();
            Assert.That(v, Is.EqualTo(85f).Within(1f));
        });
    }

    [Test]
    public async Task BrainDeathKillsMob()
    {
        var server = Pair.Server;
        var (body, brain) = await Spawn();
        await server.WaitAssertion(() =>
        {
            var bs = server.EntMan.System<BrainSystem>();
            var bc = server.EntMan.GetComponent<BrainComponent>(brain);
            bs.TakeBrainDamage((brain, bc), FixedPoint2.New(100));
            var ms = server.EntMan.GetComponent<MobStateComponent>(body);
            Assert.That(ms.CurrentState, Is.EqualTo(MobState.Dead));
            Assert.That(bc.HasBeenDead, Is.True);
        });
    }

    [Test]
    public async Task DamageOnlyCausesCriticalNotDeath()
    {
        var server = Pair.Server;
        var (body, brain) = await Spawn();
        await server.WaitAssertion(() =>
        {
            var dmg = server.EntMan.System<DamageableSystem>();
            var spec = new DamageSpecifier();
            spec.DamageDict.Add("Blunt", FixedPoint2.New(150));
            dmg.TryChangeDamage(body, spec, ignoreResistances: true);
            var ms = server.EntMan.GetComponent<MobStateComponent>(body);
            Assert.That(ms.CurrentState, Is.EqualTo(MobState.Critical));
            var bc = server.EntMan.GetComponent<BrainComponent>(brain);
            var raw = bc.Integrity;
            Assert.That(raw, Is.GreaterThan(FixedPoint2.Zero));
        });
    }

    [Test]
    public async Task DeathRequiresBrainDeathEvenWithExcessDamage()
    {
        var server = Pair.Server;
        var (body, brain) = await Spawn();
        await server.WaitAssertion(() =>
        {
            var dmg = server.EntMan.System<DamageableSystem>();
            var spec = new DamageSpecifier();
            spec.DamageDict.Add("Blunt", FixedPoint2.New(250));
            dmg.TryChangeDamage(body, spec, ignoreResistances: true);
            var ms = server.EntMan.GetComponent<MobStateComponent>(body);
            Assert.That(ms.CurrentState, Is.EqualTo(MobState.Critical));

            var bs = server.EntMan.System<BrainSystem>();
            var bc = server.EntMan.GetComponent<BrainComponent>(brain);
            bs.TakeBrainDamage((brain, bc), FixedPoint2.New(100));
            Assert.That(ms.CurrentState, Is.EqualTo(MobState.Dead));
        });
    }
}
