using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organs;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Medical.Wounds;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class WoundSystemTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = $@"
- type: damageContainer
  id: WSDmgCont
  supportedGroups:
    - Brute
    - Burn

- type: entity
  id: WSB
  components:
  - type: Damageable
    damageContainer: WSDmgCont
  - type: Injurable
    damageContainer: WSDmgCont
  - type: Body

- type: entity
  id: WSL
  components:
  - type: Organ
  - type: ExternalOrgan
    maxDamage: 100
    minBrokenDamage: 50
    flags:
    - CanAmputate
    - CanBreak
  - type: Woundable

- type: woundEffect
  id: WSTestBleed
  effectType: Bleeding
  config:
    baseBleedAmount: 2.0

- type: woundEffect
  id: WSTestPain
  effectType: Pain
  config:
    painAmount: 5.0
    freshPainAmount: 10.0

- type: entity
  parent: WoundBase
  id: WSTestWound
  components:
  - type: Wound
    maximumDamage: 20
  - type: WoundDescription
    descriptions:
      0.0: test-wound
  - type: WoundEffects
    effects:
    - id: WSTestBleed
    - id: Tendable
    - id: Clampable
    - id: WSTestPain
";

    private async Task<(EntityUid Body, EntityUid Limb)> Spawn()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();
        var body = EntityUid.Invalid;
        var limb = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var cs = entMan.System<SharedContainerSystem>();
            body = entMan.SpawnEntity("WSB", map.MapCoords);
            cs.EnsureContainer<Container>(body, BodyComponent.ContainerID, out _);
            var c = cs.GetContainer(body, BodyComponent.ContainerID);
            limb = entMan.SpawnEntity("WSL", map.MapCoords);
            cs.Insert(limb, c);
        });
        await server.WaitRunTicks(5);
        return (body, limb);
    }

    [Test]
    public async Task DamageCreatesWounds()
    {
        var server = Pair.Server;
        var (body, limb) = await Spawn();
        await server.WaitAssertion(() =>
        {
            var dmg = server.EntMan.System<DamageableSystem>();
            var spec = new DamageSpecifier();
            spec.DamageDict.Add("Blunt", FixedPoint2.New(15));
            dmg.TryChangeDamage(body, spec, ignoreResistances: true);
            var wc = server.EntMan.GetComponent<WoundableComponent>(limb);
            Assert.That(wc.Wounds, Has.Count.GreaterThanOrEqualTo(1));
        });
    }

    [Test]
    public async Task BleedingWoundReportsBleed()
    {
        var server = Pair.Server;
        var (_, limb) = await Spawn();
        await server.WaitAssertion(() =>
        {
            var wc = server.EntMan.GetComponent<WoundableComponent>(limb);
            var coords = server.EntMan.GetComponent<TransformComponent>(limb).Coordinates;
            var we = server.EntMan.SpawnEntity("WSTestWound", coords);
            var w = server.EntMan.GetComponent<WoundComponent>(we);
            w.ParentWoundable = limb;
            w.Damage.DamageDict.Add("Blunt", FixedPoint2.New(10));
            server.EntMan.Dirty(we, w);

            // Initialize bleeding effect runtime state
            var effects = server.EntMan.GetComponent<WoundEffectsComponent>(we);
            foreach (var instance in effects.Effects)
            {
                var id = instance.Id;
                if (id == "WSTestBleed" || id == "WSTestBleed")
                {
                    instance.FloatParams["currentBleedAmount"] = 2.0f;
                }
            }

            wc.Wounds.Add(we);
            server.EntMan.Dirty(limb, wc);

            var ev = new GetBleedLevelEvent(FixedPoint2.Zero);
            server.EntMan.EventBus.RaiseLocalEvent(we, ref ev);
            Assert.That(ev.BleedAmount, Is.GreaterThan(FixedPoint2.Zero));
        });
    }

    [Test]
    public async Task TendingReducesBleed()
    {
        var server = Pair.Server;
        var (_, limb) = await Spawn();
        await server.WaitAssertion(() =>
        {
            var wc = server.EntMan.GetComponent<WoundableComponent>(limb);
            var coords = server.EntMan.GetComponent<TransformComponent>(limb).Coordinates;
            var we = server.EntMan.SpawnEntity("WSTestWound", coords);

            // Initialize bleeding runtime state
            var effects = server.EntMan.GetComponent<WoundEffectsComponent>(we);
            foreach (var instance in effects.Effects)
            {
                if (instance.Id == "WSTestBleed")
                    instance.FloatParams["currentBleedAmount"] = 2.0f;
            }

            var ev = new GetBleedLevelEvent(FixedPoint2.Zero);
            server.EntMan.EventBus.RaiseLocalEvent(we, ref ev);
            var before = ev.BleedAmount;

            var tendInstance = effects.Effects.Find(e => e.Id == "Tendable");
            Assert.That(tendInstance, Is.Not.Null);
            tendInstance!.FloatParams["tended"] = 1;
            server.EntMan.Dirty(we, effects);

            ev = new GetBleedLevelEvent(FixedPoint2.Zero);
            server.EntMan.EventBus.RaiseLocalEvent(we, ref ev);
            Assert.That(ev.BleedAmount, Is.LessThan(before));
        });
    }

    [Test]
    public async Task ClampingReducesBleed()
    {
        var server = Pair.Server;
        var (_, limb) = await Spawn();
        await server.WaitAssertion(() =>
        {
            var coords = server.EntMan.GetComponent<TransformComponent>(limb).Coordinates;
            var we = server.EntMan.SpawnEntity("WSTestWound", coords);

            var effects = server.EntMan.GetComponent<WoundEffectsComponent>(we);
            foreach (var instance in effects.Effects)
            {
                if (instance.Id == "WSTestBleed")
                    instance.FloatParams["currentBleedAmount"] = 2.0f;
            }

            var ev = new GetBleedLevelEvent(FixedPoint2.Zero);
            server.EntMan.EventBus.RaiseLocalEvent(we, ref ev);
            var before = ev.BleedAmount;

            var clampInstance = effects.Effects.Find(e => e.Id == "Clampable");
            Assert.That(clampInstance, Is.Not.Null);
            clampInstance!.FloatParams["clamped"] = 1;
            server.EntMan.Dirty(we, effects);

            ev = new GetBleedLevelEvent(FixedPoint2.Zero);
            server.EntMan.EventBus.RaiseLocalEvent(we, ref ev);
            Assert.That(ev.BleedAmount, Is.LessThan(before));
            Assert.That(ev.BleedAmount, Is.GreaterThan(FixedPoint2.Zero));
        });
    }

    [Test]
    public async Task PainfulWoundReportsPain()
    {
        var server = Pair.Server;
        var (_, limb) = await Spawn();
        await server.WaitAssertion(() =>
        {
            var coords = server.EntMan.GetComponent<TransformComponent>(limb).Coordinates;
            var we = server.EntMan.SpawnEntity("WSTestWound", coords);
            var ev = new GetPainEvent(FixedPoint2.Zero, FixedPoint2.Zero);
            server.EntMan.EventBus.RaiseLocalEvent(we, ref ev);
            Assert.That(ev.PainAmount, Is.GreaterThan(FixedPoint2.Zero));
        });
    }

    [Test]
    public async Task MultipleWoundsAggregate()
    {
        var server = Pair.Server;
        var (_, limb) = await Spawn();
        await server.WaitAssertion(() =>
        {
            var wc = server.EntMan.GetComponent<WoundableComponent>(limb);
            var coords = server.EntMan.GetComponent<TransformComponent>(limb).Coordinates;

            var w1 = server.EntMan.SpawnEntity("WSTestWound", coords);
            var w1c = server.EntMan.GetComponent<WoundComponent>(w1);
            w1c.ParentWoundable = limb;
            w1c.Damage.DamageDict.Add("Blunt", FixedPoint2.New(5));
            server.EntMan.Dirty(w1, w1c);

            var w2 = server.EntMan.SpawnEntity("WSTestWound", coords);
            var w2c = server.EntMan.GetComponent<WoundComponent>(w2);
            w2c.ParentWoundable = limb;
            w2c.Damage.DamageDict.Add("Blunt", FixedPoint2.New(8));
            server.EntMan.Dirty(w2, w2c);

            wc.Wounds.Add(w1);
            wc.Wounds.Add(w2);
            server.EntMan.Dirty(limb, wc);
        });
        await server.WaitRunTicks(5);
        await server.WaitAssertion(() =>
        {
            var wc = server.EntMan.GetComponent<WoundableComponent>(limb);
            Assert.That(wc.TotalDamage.GetTotal(), Is.GreaterThan(FixedPoint2.New(10)));
        });
    }
}
