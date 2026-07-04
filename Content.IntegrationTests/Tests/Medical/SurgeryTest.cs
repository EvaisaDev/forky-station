using Content.IntegrationTests.Fixtures;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organs;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Medical.Surgery;
using Content.Shared.Medical.Wounds;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class SurgeryTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = $@"
- type: damageContainer
  id: SurgDmgCont
  supportedGroups:
    - Brute
    - Burn

- type: entity
  id: SurgBody
  components:
  - type: Damageable
    damageContainer: SurgDmgCont
  - type: Injurable
    damageContainer: SurgDmgCont
  - type: Body

- type: entity
  id: SurgLimb
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
  id: SurgToolScalpel
  components:
  - type: SurgeryTool
    action: Incision
    quality: 1.0
    sterile: true
  - type: Item
  - type: Sprite

- type: entity
  id: SurgToolHemostat
  components:
  - type: SurgeryTool
    action: Clamp
    quality: 1.0
    sterile: true
  - type: Item
  - type: Sprite

- type: entity
  id: SurgToolRetractor
  components:
  - type: SurgeryTool
    action: Retract
    quality: 1.0
    sterile: true
  - type: Item
  - type: Sprite

- type: entity
  id: SurgToolCautery
  components:
  - type: SurgeryTool
    action: Cauterize
    quality: 1.0
    sterile: true
  - type: Item
  - type: Sprite
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
            body = entMan.SpawnEntity("SurgBody", map.MapCoords);
            containerSys.EnsureContainer<Container>(body, BodyComponent.ContainerID, out _);
            var c = containerSys.GetContainer(body, BodyComponent.ContainerID);
            limb = entMan.SpawnEntity("SurgLimb", map.MapCoords);
            containerSys.Insert(limb, c);
        });
        await server.WaitRunTicks(5);
        return (body, limb);
    }

    [Test]
    public async Task IncisionAdvancesStage()
    {
        var server = Pair.Server;
        var (_, limb) = await Spawn();
        await server.WaitAssertion(() =>
        {
            var sys = server.EntMan.System<SurgerySystem>();
            var ext = server.EntMan.GetComponent<ExternalOrganComponent>(limb);
            var tool = server.EntMan.SpawnEntity("SurgToolScalpel", server.EntMan.GetComponent<TransformComponent>(limb).Coordinates);
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.None));
            sys.ExecuteStep(tool, server.EntMan.GetComponent<SurgeryToolComponent>(tool), limb, ext);
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.Incised));
        });
    }

    [Test]
    public async Task FullProgression()
    {
        var server = Pair.Server;
        var (_, limb) = await Spawn();
        await server.WaitAssertion(() =>
        {
            var sys = server.EntMan.System<SurgerySystem>();
            var ext = server.EntMan.GetComponent<ExternalOrganComponent>(limb);
            var coords = server.EntMan.GetComponent<TransformComponent>(limb).Coordinates;

            var s = server.EntMan.SpawnEntity("SurgToolScalpel", coords);
            sys.ExecuteStep(s, server.EntMan.GetComponent<SurgeryToolComponent>(s), limb, ext);
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.Incised));

            var h = server.EntMan.SpawnEntity("SurgToolHemostat", coords);
            sys.ExecuteStep(h, server.EntMan.GetComponent<SurgeryToolComponent>(h), limb, ext);
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.Clamped));

            var r = server.EntMan.SpawnEntity("SurgToolRetractor", coords);
            sys.ExecuteStep(r, server.EntMan.GetComponent<SurgeryToolComponent>(r), limb, ext);
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.Retracted));

            var c = server.EntMan.SpawnEntity("SurgToolCautery", coords);
            sys.ExecuteStep(c, server.EntMan.GetComponent<SurgeryToolComponent>(c), limb, ext);
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.None));
        });
    }

    [Test]
    public async Task SurgeryToolValues()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var tool = server.EntMan.SpawnEntity("SurgToolScalpel", map.MapCoords);
            var comp = server.EntMan.GetComponent<SurgeryToolComponent>(tool);
            Assert.That(comp.Action, Is.EqualTo("Incision"));
            Assert.That(comp.Quality, Is.EqualTo(1.0f).Within(0.01f));
            Assert.That(comp.Sterile, Is.True);
        });
    }

    [Test]
    public async Task WrongStepReturnsError()
    {
        var server = Pair.Server;
        var (_, limb) = await Spawn();
        await server.WaitAssertion(() =>
        {
            var sys = server.EntMan.System<SurgerySystem>();
            var ext = server.EntMan.GetComponent<ExternalOrganComponent>(limb);
            var tool = server.EntMan.SpawnEntity("SurgToolHemostat",
                server.EntMan.GetComponent<TransformComponent>(limb).Coordinates);
            var result = sys.ExecuteStep(tool, server.EntMan.GetComponent<SurgeryToolComponent>(tool), limb, ext);
            Assert.That(result, Is.Not.Null, "Wrong step should return error message");
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.None), "Stage should not change");
        });
    }
}
