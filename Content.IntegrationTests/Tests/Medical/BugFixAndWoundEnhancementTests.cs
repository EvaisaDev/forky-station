using Content.IntegrationTests.Fixtures;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organs;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Medical.Surgery;
using Content.Shared.Medical.Wounds;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class BugFixTests : GameTest
{
    [TestPrototypes]
    private const string Prototypes = $@"
- type: damageContainer
  id: BFT10
  supportedGroups:
    - Brute
    - Burn

- type: entity
  id: BFBody10
  components:
  - type: Damageable
    damageContainer: BFT10
  - type: Injurable
    damageContainer: BFT10
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      100: Critical
      200: Dead
  - type: Body

- type: entity
  id: BFBrain10
  components:
  - type: Organ
    category: Brain
  - type: Brain
    integrity: 100
    maxIntegrity: 100

- type: entity
  id: BFLimb10
  components:
  - type: Organ
  - type: ExternalOrgan
    maxDamage: 100
    minBrokenDamage: 50
    flags:
    - CanAmputate
    - CanBreak
    - HasTendon
  - type: Woundable

- type: entity
  id: BFSimple10
  components:
  - type: Damageable
    damageContainer: BFT10
  - type: Injurable
    damageContainer: BFT10
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      100: Critical
      200: Dead

- type: entity
  id: BFScalpel10
  components:
  - type: SurgeryTool
    action: Incision
    quality: 1.0
    sterile: true
  - type: Item
  - type: Sprite

- type: entity
  id: BFHemostat10
  components:
  - type: SurgeryTool
    action: Clamp
    quality: 1.0
    sterile: true
  - type: Item
  - type: Sprite

- type: entity
  id: BFRetractor10
  components:
  - type: SurgeryTool
    action: Retract
    quality: 1.0
    sterile: true
  - type: Item
  - type: Sprite

- type: entity
  id: BFCautery10
  components:
  - type: SurgeryTool
    action: Cauterize
    quality: 1.0
    sterile: true
  - type: Item
  - type: Sprite

- type: entity
  id: BFBoneSaw10
  components:
  - type: SurgeryTool
    action: BoneSaw
    quality: 1.0
    sterile: true
  - type: Item
  - type: Sprite

- type: entity
  id: BFBoneGlue10
  components:
  - type: SurgeryTool
    action: BoneGlue
    quality: 1.0
    sterile: true
  - type: Item
  - type: Sprite

- type: entity
  id: BFBoneSetter10
  components:
  - type: SurgeryTool
    action: BoneSet
    quality: 1.0
    sterile: true
  - type: Item
  - type: Sprite

- type: entity
  id: BFOrganFix10
  components:
  - type: SurgeryTool
    action: OrganFix
    quality: 1.0
    sterile: true
  - type: Item
  - type: Sprite

- type: entity
  id: BFNonSterile10
  components:
  - type: SurgeryTool
    action: Incision
    quality: 0.5
    sterile: false
  - type: Item
  - type: Sprite
";

    private async Task<(EntityUid Body, EntityUid Limb)> SpawnBody()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();
        var body = EntityUid.Invalid;
        var limb = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var cs = entMan.System<SharedContainerSystem>();
            body = entMan.SpawnEntity("BFBody10", map.MapCoords);
            cs.EnsureContainer<Container>(body, BodyComponent.ContainerID, out _);
            var c = cs.GetContainer(body, BodyComponent.ContainerID);
            limb = entMan.SpawnEntity("BFLimb10", map.MapCoords);
            cs.Insert(limb, c);
        });
        await server.WaitRunTicks(5);
        return (body, limb);
    }

    [Test]
    public async Task SimpleEntityCanDie()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();
        var uid = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            uid = server.ResolveDependency<IEntityManager>().SpawnEntity("BFSimple10", map.MapCoords);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var dmg = server.EntMan.System<DamageableSystem>();
            var spec = new DamageSpecifier();
            spec.DamageDict.Add("Blunt", FixedPoint2.New(250));
            dmg.TryChangeDamage(uid, spec, ignoreResistances: true);

            var ms = server.EntMan.GetComponent<MobStateComponent>(uid);
            Assert.That(ms.CurrentState, Is.EqualTo(MobState.Dead));
        });
    }

    [Test]
    public async Task AmputationRemovesLimb()
    {
        var server = Pair.Server;
        var (body, limb) = await SpawnBody();

        await server.WaitAssertion(() =>
        {
            var containerSys = server.EntMan.System<SharedContainerSystem>();
            var container = containerSys.GetContainer(body, BodyComponent.ContainerID);
            Assert.That(container.Contains(limb), Is.True);

            var ext = server.EntMan.GetComponent<ExternalOrganComponent>(limb);
            var ev = new LimbAmputateEvent((limb, ext), DropLimbType.Edge);
            server.EntMan.EventBus.RaiseLocalEvent(limb, ref ev);

            Assert.That(container.Contains(limb), Is.False);
            Assert.That((ext.Status & OrganStatusFlags.CutAway) != 0, Is.True);
        });
    }

    [Test]
    public async Task EncasedStageReachable()
    {
        var server = Pair.Server;
        var (_, limb) = await SpawnBody();

        await server.WaitAssertion(() =>
        {
            var sys = server.EntMan.System<SurgerySystem>();
            var ext = server.EntMan.GetComponent<ExternalOrganComponent>(limb);
            var coords = server.EntMan.GetComponent<TransformComponent>(limb).Coordinates;

            var s = server.EntMan.SpawnEntity("BFScalpel10", coords);
            sys.ExecuteStep(s, server.EntMan.GetComponent<SurgeryToolComponent>(s), limb, ext);
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.Incised));

            var h = server.EntMan.SpawnEntity("BFHemostat10", coords);
            sys.ExecuteStep(h, server.EntMan.GetComponent<SurgeryToolComponent>(h), limb, ext);
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.Clamped));

            var r = server.EntMan.SpawnEntity("BFRetractor10", coords);
            sys.ExecuteStep(r, server.EntMan.GetComponent<SurgeryToolComponent>(r), limb, ext);
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.Retracted));

            var bs = server.EntMan.SpawnEntity("BFBoneSaw10", coords);
            sys.ExecuteStep(bs, server.EntMan.GetComponent<SurgeryToolComponent>(bs), limb, ext);
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.Encased));
        });
    }

    [Test]
    public async Task BoneRepairThreeStep()
    {
        var server = Pair.Server;
        var (_, limb) = await SpawnBody();

        await server.WaitAssertion(() =>
        {
            var sys = server.EntMan.System<SurgerySystem>();
            var ext = server.EntMan.GetComponent<ExternalOrganComponent>(limb);
            var coords = server.EntMan.GetComponent<TransformComponent>(limb).Coordinates;

            ext.Status |= OrganStatusFlags.Broken;
            ext.SurgeryStage = SurgeryStage.Retracted;
            server.EntMan.Dirty(limb, ext);

            var g1 = server.EntMan.SpawnEntity("BFBoneGlue10", coords);
            var r1 = sys.ExecuteStep(g1, server.EntMan.GetComponent<SurgeryToolComponent>(g1), limb, ext);
            Assert.That(r1, Does.Contain("glue").IgnoreCase);

            var set = server.EntMan.SpawnEntity("BFBoneSetter10", coords);
            var r2 = sys.ExecuteStep(set, server.EntMan.GetComponent<SurgeryToolComponent>(set), limb, ext);
            Assert.That(r2, Does.Contain("set").IgnoreCase);

            var g2 = server.EntMan.SpawnEntity("BFBoneGlue10", coords);
            var r3 = sys.ExecuteStep(g2, server.EntMan.GetComponent<SurgeryToolComponent>(g2), limb, ext);
            Assert.That(r3, Does.Contain("repair").IgnoreCase);

            Assert.That((ext.Status & OrganStatusFlags.Broken) == 0, Is.True);
        });
    }
}

[TestFixture]
public sealed class WoundEnhancementTests : GameTest
{
    [TestPrototypes]
    private const string Prototypes = $@"
- type: damageContainer
  id: WET11
  supportedGroups:
    - Brute
    - Burn

- type: entity
  id: WEBody11
  components:
  - type: Damageable
    damageContainer: WET11
  - type: Injurable
    damageContainer: WET11
  - type: Body

- type: entity
  id: WELimb11
  components:
  - type: Organ
  - type: ExternalOrgan
    maxDamage: 100
    minBrokenDamage: 50
    flags:
    - CanAmputate
    - CanBreak
  - type: Woundable

- type: entity
  id: WEForceps11
  components:
  - type: SurgeryTool
    action: RemoveEmbedded
    quality: 1.0
    sterile: true
  - type: Item
  - type: Sprite

# Fast-healing test wound — uses WoundEffects with HealableOverride
- type: woundEffect
  id: WETestHealFast
  effectType: Healable
  config:
    healPerTick: 2.0

- type: woundEffect
  id: WETestPainLight
  effectType: Pain
  config:
    painAmount: 1.0
    freshPainAmount: 3.0

- type: entity
  parent: WoundBase
  id: WETestHeal
  name: test healing wound
  components:
  - type: Wound
    maximumDamage: 20
  - type: WoundDescription
    descriptions:
      0.0: wound-brute-small
  - type: WoundEffects
    effects:
    - id: WETestHealFast
    - id: WETestPainLight
    - id: GermTracking
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
            body = entMan.SpawnEntity("WEBody11", map.MapCoords);
            cs.EnsureContainer<Container>(body, BodyComponent.ContainerID, out _);
            var c = cs.GetContainer(body, BodyComponent.ContainerID);
            limb = entMan.SpawnEntity("WELimb11", map.MapCoords);
            cs.Insert(limb, c);
        });
        await server.WaitRunTicks(5);
        return (body, limb);
    }

    [Test]
    public async Task WoundStageTracks()
    {
        var server = Pair.Server;
        var (body, limb) = await Spawn();

        await server.WaitAssertion(() =>
        {
            var dmg = server.EntMan.System<DamageableSystem>();
            var spec = new DamageSpecifier();
            spec.DamageDict.Add("Blunt", FixedPoint2.New(30));
            dmg.TryChangeDamage(body, spec, ignoreResistances: true);

            var wc = server.EntMan.GetComponent<WoundableComponent>(limb);
            Assert.That(wc.Wounds.Count, Is.GreaterThan(0));

            foreach (var wUid in wc.Wounds)
            {
                if (server.EntMan.TryGetComponent<WoundComponent>(wUid, out var w))
                {
                    Assert.That(w.Stage, Is.GreaterThanOrEqualTo(0));
                    Assert.That(w.MaxStages, Is.GreaterThan(0));
                }
            }
        });
    }

    [Test]
    public async Task WoundHealsOverTime()
    {
        var server = Pair.Server;
        var (_, limb) = await Spawn();

        await server.WaitAssertion(() =>
        {
            var wc = server.EntMan.GetComponent<WoundableComponent>(limb);
            var coords = server.EntMan.GetComponent<TransformComponent>(limb).Coordinates;

            var wEnt = server.EntMan.SpawnEntity("WETestHeal", coords);
            var w = server.EntMan.GetComponent<WoundComponent>(wEnt);
            w.ParentWoundable = limb;
            w.Damage.DamageDict.Add("Blunt", FixedPoint2.New(20));
            server.EntMan.Dirty(wEnt, w);
            wc.Wounds.Add(wEnt);
            server.EntMan.Dirty(limb, wc);
        });

        await server.WaitRunTicks(120);

        await server.WaitAssertion(() =>
        {
            var wc = server.EntMan.GetComponent<WoundableComponent>(limb);
            Assert.That(wc.Wounds.Count, Is.GreaterThan(0));
            Assert.That(wc.TotalDamage.GetTotal(), Is.LessThan(FixedPoint2.New(20)),
                "Wound damage should decrease over time");
        });
    }

    [Test]
    public async Task WoundsMerge()
    {
        var server = Pair.Server;
        var (body, limb) = await Spawn();

        await server.WaitAssertion(() =>
        {
            var dmg = server.EntMan.System<DamageableSystem>();
            var spec1 = new DamageSpecifier();
            spec1.DamageDict.Add("Blunt", FixedPoint2.New(6));
            dmg.TryChangeDamage(body, spec1, ignoreResistances: true);

            var spec2 = new DamageSpecifier();
            spec2.DamageDict.Add("Blunt", FixedPoint2.New(6));
            dmg.TryChangeDamage(body, spec2, ignoreResistances: true);
        });

        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            var wc = server.EntMan.GetComponent<WoundableComponent>(limb);
            Assert.That(wc.Wounds.Count, Is.LessThanOrEqualTo(2),
                "Compatible wounds should merge, keeping count low");
        });
    }

    [Test]
    public async Task WoundHasGermTracking()
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
            Assert.That(wc.Wounds.Count, Is.GreaterThan(0));

            foreach (var wUid in wc.Wounds)
            {
                Assert.That(server.EntMan.TryGetComponent<WoundEffectsComponent>(wUid, out _), Is.True);
                var effects = server.EntMan.GetComponent<WoundEffectsComponent>(wUid);
                Assert.That(effects.Effects.Exists(e => e.Id == "GermTracking"), Is.True);
            }
        });
    }

    [Test]
    public async Task EmbeddedObjectTracks()
    {
        var server = Pair.Server;
        var (_, limb) = await Spawn();

        await server.WaitAssertion(() =>
        {
            var wc = server.EntMan.GetComponent<WoundableComponent>(limb);
            var coords = server.EntMan.GetComponent<TransformComponent>(limb).Coordinates;

            var wEnt = server.EntMan.SpawnEntity("WoundBruteSmall", coords);
            var w = server.EntMan.GetComponent<WoundComponent>(wEnt);
            w.ParentWoundable = limb;
            w.Damage.DamageDict.Add("Blunt", FixedPoint2.New(5));
            server.EntMan.Dirty(wEnt, w);

            var effects = server.EntMan.EnsureComponent<WoundEffectsComponent>(wEnt);
            var embedded = effects.Effects.Find(e => e.Id == "Embedded");
            if (embedded == null)
            {
                embedded = new WoundEffectInstance { Id = "Embedded" };
                effects.Effects.Add(embedded);
            }
            embedded.StringListParams.Add("shrapnel");
            server.EntMan.Dirty(wEnt, effects);

            wc.Wounds.Add(wEnt);
            server.EntMan.Dirty(limb, wc);

            Assert.That(embedded.StringListParams.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SurgicalIncisionIsSurgical()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var incWound = server.EntMan.SpawnEntity("WoundSurgicalIncision", map.MapCoords);
            var w = server.EntMan.GetComponent<WoundComponent>(incWound);
            Assert.That(w.IsSurgical, Is.True);
            Assert.That(w.HealPerTick, Is.EqualTo(0f).Within(0.01f));
        });
    }
}
