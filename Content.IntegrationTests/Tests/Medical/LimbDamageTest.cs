using Content.IntegrationTests.Fixtures;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organs;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class LimbDamageTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = $@"
- type: damageContainer
  id: LimbDmgCont
  supportedGroups:
    - Brute
    - Burn

- type: entity
  id: LimbDmgBody
  components:
  - type: Damageable
    damageContainer: LimbDmgCont
  - type: Injurable
    damageContainer: LimbDmgCont
  - type: Body

- type: entity
  id: LimbDmgLimb
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
            var containerSys = entMan.System<SharedContainerSystem>();
            body = entMan.SpawnEntity("LimbDmgBody", map.MapCoords);
            containerSys.EnsureContainer<Container>(body, BodyComponent.ContainerID, out _);
            var c = containerSys.GetContainer(body, BodyComponent.ContainerID);
            limb = entMan.SpawnEntity("LimbDmgLimb", map.MapCoords);
            containerSys.Insert(limb, c);
        });
        await server.WaitRunTicks(5);
        return (body, limb);
    }

    [Test]
    public async Task BruteDamageAppliedToLimb()
    {
        var server = Pair.Server;
        var (body, limb) = await Spawn();
        await server.WaitAssertion(() =>
        {
            var dmg = server.EntMan.System<DamageableSystem>();
            var spec = new DamageSpecifier();
            spec.DamageDict.Add("Blunt", FixedPoint2.New(30));
            dmg.TryChangeDamage(body, spec, ignoreResistances: true);
            var ext = server.EntMan.GetComponent<ExternalOrganComponent>(limb);
            Assert.That((float)ext.BruteDamage.Float(), Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task BurnDamageAppliedToLimb()
    {
        var server = Pair.Server;
        var (body, limb) = await Spawn();
        await server.WaitAssertion(() =>
        {
            var dmg = server.EntMan.System<DamageableSystem>();
            var spec = new DamageSpecifier();
            spec.DamageDict.Add("Heat", FixedPoint2.New(25));
            dmg.TryChangeDamage(body, spec, ignoreResistances: true);
            var ext = server.EntMan.GetComponent<ExternalOrganComponent>(limb);
            Assert.That((float)ext.BurnDamage.Float(), Is.GreaterThan(0));
        });
    }
}
