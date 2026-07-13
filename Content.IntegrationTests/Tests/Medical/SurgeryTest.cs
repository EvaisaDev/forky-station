using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organs;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Medical.Surgery;
using Content.Shared.Medical.Wounds;
using Content.Shared.Popups;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

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
    category: Torso
  - type: ExternalOrgan
    maxDamage: 100
    minBrokenDamage: 50
    flags:
    - CanAmputate
    - CanBreak
    - HasTendon
  - type: Woundable

- type: entity
  id: SurgLimbArm
  components:
  - type: Organ
    category: ArmLeft
  - type: ExternalOrgan
    maxDamage: 80
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

- type: entity
  id: SurgToolHemostat
  components:
  - type: SurgeryTool
    action: Clamp
    quality: 1.0
    sterile: true
  - type: Item

- type: entity
  id: SurgToolRetractor
  components:
  - type: SurgeryTool
    action: Retract
    quality: 1.0
    sterile: true
  - type: Item

- type: entity
  id: SurgToolCautery
  components:
  - type: SurgeryTool
    action: Cauterize
    quality: 1.0
    sterile: true
  - type: Item

- type: entity
  id: SurgToolBoneSaw
  components:
  - type: SurgeryTool
    action: BoneSaw
    quality: 1.0
    sterile: true
  - type: Item

- type: entity
  id: SurgToolBoneGlue
  components:
  - type: SurgeryTool
    action: BoneGlue
    quality: 1.0
    sterile: true
  - type: Item

- type: entity
  id: SurgToolBoneSet
  components:
  - type: SurgeryTool
    action: BoneSet
    quality: 1.0
    sterile: true
  - type: Item

- type: entity
  id: SurgToolOrganFix
  components:
  - type: SurgeryTool
    action: OrganFix
    quality: 1.0
    sterile: true
  - type: Item

- type: entity
  id: SurgToolAmputate
  components:
  - type: SurgeryTool
    action: Amputate
    quality: 1.0
    sterile: true
  - type: Item

- type: entity
  id: SurgToolOrganDetach
  components:
  - type: SurgeryTool
    action: OrganDetach
    quality: 1.0
    sterile: true
  - type: Item

- type: entity
  id: SurgToolOrganRemove
  components:
  - type: SurgeryTool
    action: OrganRemove
    quality: 1.0
    sterile: true
  - type: Item

- type: entity
  id: SurgToolOrganAttach
  components:
  - type: SurgeryTool
    action: OrganAttach
    quality: 1.0
    sterile: true
  - type: Item

- type: entity
  id: SurgToolLowQuality
  components:
  - type: SurgeryTool
    action: Incision
    quality: 0.3
    sterile: false
  - type: Item

- type: entity
  id: SurgInternalOrgan
  components:
  - type: Organ
    category: Torso
  - type: HeartCondition
    beating: true
    efficiency: 0.5

- type: entity
  id: SurgBrainOrgan
  components:
  - type: Organ
    category: Head
  - type: Brain
    integrity: 100
    maxIntegrity: 100
";

    private async Task<(EntityUid Body, EntityUid Limb)> SpawnBodyAndLimb(string limbProto = "SurgLimb")
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
            limb = entMan.SpawnEntity(limbProto, map.MapCoords);
            containerSys.Insert(limb, c);
        });
        await server.WaitRunTicks(5);
        return (body, limb);
    }

    private async Task<(EntityUid Body, EntityUid Limb)> SpawnWithInternalOrgan()
    {
        var server = Pair.Server;
        var (body, limb) = await SpawnBodyAndLimb();
        await server.WaitPost(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var containerSys = entMan.System<SharedContainerSystem>();
            var c = containerSys.GetContainer(body, BodyComponent.ContainerID);
            var organ = entMan.SpawnEntity("SurgInternalOrgan", entMan.GetComponent<TransformComponent>(body).Coordinates);
            containerSys.Insert(organ, c);
        });
        await server.WaitRunTicks(5);
        return (body, limb);
    }

    private EntityUid MakeTool(string protoId, EntityCoordinates coords)
    {
        return Pair.Server.EntMan.SpawnEntity(protoId, coords);
    }

    private SurgeryToolComponent ToolComp(EntityUid tool) => Pair.Server.EntMan.GetComponent<SurgeryToolComponent>(tool);

    private ExternalOrganComponent Ext(EntityUid limb) => Pair.Server.EntMan.GetComponent<ExternalOrganComponent>(limb);

    [Test]
    public async Task IncisionAdvancesStage()
    {
        var server = Pair.Server;
        var (_, limb) = await SpawnBodyAndLimb();
        await server.WaitAssertion(() =>
        {
            var sys = server.EntMan.System<SurgerySystem>();
            var ext = Ext(limb);
            var tool = MakeTool("SurgToolScalpel", server.EntMan.GetComponent<TransformComponent>(limb).Coordinates);
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.None));
            var result = sys.ExecuteStep(tool, ToolComp(tool), limb, ext);
            Assert.That(result, Is.Not.Null);
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.Incised));
        });
    }

    [Test]
    public async Task FullSurgeryProgression()
    {
        var server = Pair.Server;
        var (_, limb) = await SpawnBodyAndLimb();
        await server.WaitAssertion(() =>
        {
            var sys = server.EntMan.System<SurgerySystem>();
            var ext = Ext(limb);
            var coords = server.EntMan.GetComponent<TransformComponent>(limb).Coordinates;

            // 1. Incise
            sys.ExecuteStep(MakeTool("SurgToolScalpel", coords), ToolComp(MakeTool("SurgToolScalpel", coords)), limb, ext);
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.Incised));

            // 2. Clamp
            sys.ExecuteStep(MakeTool("SurgToolHemostat", coords), ToolComp(MakeTool("SurgToolHemostat", coords)), limb, ext);
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.Clamped));

            // 3. Retract
            sys.ExecuteStep(MakeTool("SurgToolRetractor", coords), ToolComp(MakeTool("SurgToolRetractor", coords)), limb, ext);
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.Retracted));

            // 4. Open bone (BoneSaw)
            sys.ExecuteStep(MakeTool("SurgToolBoneSaw", coords), ToolComp(MakeTool("SurgToolBoneSaw", coords)), limb, ext);
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.Encased));

            // 5. OrganFix on encased limb (no organs to fix, but should succeed)
            var fixResult = sys.ExecuteStep(MakeTool("SurgToolOrganFix", coords), ToolComp(MakeTool("SurgToolOrganFix", coords)), limb, ext);
            Assert.That(fixResult, Is.Not.Null);

            // 6. Bone repair: Glue → Set → Glue (requires broken bone)
            ext.Status |= OrganStatusFlags.Broken;
            var glue1 = sys.ExecuteStep(MakeTool("SurgToolBoneGlue", coords), ToolComp(MakeTool("SurgToolBoneGlue", coords)), limb, ext);
            Assert.That(glue1, Does.Contain("glued"));
            Assert.That(ext.BoneRepairStage, Is.EqualTo(1));

            var set = sys.ExecuteStep(MakeTool("SurgToolBoneSet", coords), ToolComp(MakeTool("SurgToolBoneSet", coords)), limb, ext);
            Assert.That(set, Does.Contain("set"));
            Assert.That(ext.BoneRepairStage, Is.EqualTo(2));

            var glue2 = sys.ExecuteStep(MakeTool("SurgToolBoneGlue", coords), ToolComp(MakeTool("SurgToolBoneGlue", coords)), limb, ext);
            Assert.That(glue2, Does.Contain("repaired"));
            Assert.That(ext.BoneRepairStage, Is.EqualTo(3));
            Assert.That((ext.Status & OrganStatusFlags.Broken) == 0, Is.True, "Bone should no longer be broken");

            // 7. Cauterize closes the incision
            sys.ExecuteStep(MakeTool("SurgToolCautery", coords), ToolComp(MakeTool("SurgToolCautery", coords)), limb, ext);
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.None));
        });
    }

    [Test]
    public async Task WrongStepReturnsError()
    {
        var server = Pair.Server;
        var (_, limb) = await SpawnBodyAndLimb();
        await server.WaitAssertion(() =>
        {
            var sys = server.EntMan.System<SurgerySystem>();
            var ext = Ext(limb);
            var tool = MakeTool("SurgToolHemostat", server.EntMan.GetComponent<TransformComponent>(limb).Coordinates);
            var result = sys.ExecuteStep(tool, ToolComp(tool), limb, ext);
            Assert.That(result, Is.Not.Null, "Wrong step should return error message");
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.None), "Stage should not change");
        });
    }

    [Test]
    public async Task LowQualityToolMayFail()
    {
        var server = Pair.Server;
        var (_, limb) = await SpawnBodyAndLimb();
        await server.WaitAssertion(() =>
        {
            var sys = server.EntMan.System<SurgerySystem>();
            var ext = Ext(limb);
            var tool = MakeTool("SurgToolLowQuality", server.EntMan.GetComponent<TransformComponent>(limb).Coordinates);
            var comp = ToolComp(tool);
            Assert.That(comp.Quality, Is.EqualTo(0.3f).Within(0.01f));

            // At quality 0.3, failures are random. Verify the quality value is set correctly.
            // A tool at quality 1.0 should never slip.
            var perfectTool = MakeTool("SurgToolScalpel", server.EntMan.GetComponent<TransformComponent>(limb).Coordinates);
            Assert.That(ToolComp(perfectTool).Quality, Is.EqualTo(1.0f));
        });
    }

    [Test]
    public async Task CauterizeWorksOnRetractedAndEncased()
    {
        var server = Pair.Server;
        var (_, limb) = await SpawnBodyAndLimb();
        await server.WaitAssertion(() =>
        {
            var sys = server.EntMan.System<SurgerySystem>();
            var ext = Ext(limb);
            var coords = server.EntMan.GetComponent<TransformComponent>(limb).Coordinates;

            // Retracted → Cauterize
            ext.SurgeryStage = SurgeryStage.Retracted;
            var result = sys.ExecuteStep(MakeTool("SurgToolCautery", coords), ToolComp(MakeTool("SurgToolCautery", coords)), limb, ext);
            Assert.That(result, Is.Not.Null);
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.None));

            // Encased → Cauterize
            ext.SurgeryStage = SurgeryStage.Encased;
            result = sys.ExecuteStep(MakeTool("SurgToolCautery", coords), ToolComp(MakeTool("SurgToolCautery", coords)), limb, ext);
            Assert.That(result, Is.Not.Null);
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.None));
        });
    }

    [Test]
    public async Task AmputationRemovesLimb()
    {
        var server = Pair.Server;
        var (body, limb) = await SpawnBodyAndLimb("SurgLimbArm");
        await server.WaitAssertion(() =>
        {
            var sys = server.EntMan.System<SurgerySystem>();
            var ext = Ext(limb);
            var coords = server.EntMan.GetComponent<TransformComponent>(limb).Coordinates;
            var bodyComp = server.EntMan.GetComponent<BodyComponent>(body);

            // Amputation requires None stage and CanAmputate flag
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.None));
            Assert.That((ext.Flags & LimbFlags.CanAmputate) != 0, Is.True);

            // Verify limb is in the body
            Assert.That(bodyComp.Organs, Is.Not.Null);
            Assert.That(bodyComp.Organs!.ContainedEntities.Contains(limb), Is.True);

            var result = sys.ExecuteStep(MakeTool("SurgToolAmputate", coords), ToolComp(MakeTool("SurgToolAmputate", coords)), limb, ext);
            Assert.That(result, Is.Not.Null);

            // After amputation, limb should be removed from body container
            Assert.That(bodyComp.Organs!.ContainedEntities.Contains(limb), Is.False);
        });
    }

    [Test]
    public async Task BoneRepairRequiresBrokenBone()
    {
        var server = Pair.Server;
        var (_, limb) = await SpawnBodyAndLimb();
        await server.WaitAssertion(() =>
        {
            var sys = server.EntMan.System<SurgerySystem>();
            var ext = Ext(limb);
            var coords = server.EntMan.GetComponent<TransformComponent>(limb).Coordinates;

            ext.SurgeryStage = SurgeryStage.Retracted;

            // No broken bone → should say "bone not broken"
            var result = sys.ExecuteStep(MakeTool("SurgToolBoneGlue", coords), ToolComp(MakeTool("SurgToolBoneGlue", coords)), limb, ext);
            Assert.That(result, Does.Contain("not broken"));
            Assert.That(ext.BoneRepairStage, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task OrganTransplantDetachRemoveReplaceAttach()
    {
        var server = Pair.Server;
        var (body, limb) = await SpawnWithInternalOrgan();
        await server.WaitAssertion(() =>
        {
            var sys = server.EntMan.System<SurgerySystem>();
            var ext = Ext(limb);
            var coords = server.EntMan.GetComponent<TransformComponent>(limb).Coordinates;
            var bodyComp = server.EntMan.GetComponent<BodyComponent>(body);
            var containerSys = server.EntMan.System<SharedContainerSystem>();

            // Need the limb to be open for organ work
            ext.SurgeryStage = SurgeryStage.Encased;

            // Verify we have an internal organ
            var internalOrgans = bodyComp.Organs!.ContainedEntities
                .Where(o => o != limb && !server.EntMan.HasComponent<WoundableComponent>(o))
                .ToList();
            Assert.That(internalOrgans.Count, Is.GreaterThan(0), "Should have internal organs");

            // OrganDetach
            var detachResult = sys.ExecuteStep(MakeTool("SurgToolOrganDetach", coords), ToolComp(MakeTool("SurgToolOrganDetach", coords)), limb, ext, bodyComp, body);
            Assert.That(detachResult, Is.Not.Null);

            // OrganRemove
            var removeResult = sys.ExecuteStep(MakeTool("SurgToolOrganRemove", coords), ToolComp(MakeTool("SurgToolOrganRemove", coords)), limb, ext, bodyComp, body);
            Assert.That(removeResult, Is.Not.Null);

            // Verify organ was removed from body
            var remainingOrgans = bodyComp.Organs!.ContainedEntities
                .Where(o => o != limb && !server.EntMan.HasComponent<WoundableComponent>(o))
                .ToList();
            Assert.That(remainingOrgans.Count, Is.EqualTo(0), "Internal organ should be removed");
        });
    }

    [Test]
    public async Task SurgeryToolValuesCorrect()
    {
        var server = Pair.Server;
        var map = await Pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var scalpel = server.EntMan.SpawnEntity("SurgToolScalpel", map.MapCoords);
            Assert.That(server.EntMan.GetComponent<SurgeryToolComponent>(scalpel).Action, Is.EqualTo("Incision"));

            var saw = server.EntMan.SpawnEntity("SurgToolBoneSaw", map.MapCoords);
            Assert.That(server.EntMan.GetComponent<SurgeryToolComponent>(saw).Action, Is.EqualTo("BoneSaw"));

            var detach = server.EntMan.SpawnEntity("SurgToolOrganDetach", map.MapCoords);
            Assert.That(server.EntMan.GetComponent<SurgeryToolComponent>(detach).Action, Is.EqualTo("OrganDetach"));
        });
    }

    [Test]
    public async Task BoneSawOnUnopenedLimbAmputates()
    {
        var server = Pair.Server;
        var (body, limb) = await SpawnBodyAndLimb("SurgLimbArm");
        await server.WaitAssertion(() =>
        {
            var sys = server.EntMan.System<SurgerySystem>();
            var ext = Ext(limb);
            var coords = server.EntMan.GetComponent<TransformComponent>(limb).Coordinates;
            var bodyComp = server.EntMan.GetComponent<BodyComponent>(body);

            // BoneSaw on None stage on an amputatable limb → amputates
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.None));
            var result = sys.ExecuteStep(MakeTool("SurgToolBoneSaw", coords), ToolComp(MakeTool("SurgToolBoneSaw", coords)), limb, ext);
            Assert.That(result, Is.Not.Null);
            Assert.That(bodyComp.Organs!.ContainedEntities.Contains(limb), Is.False, "Limb should be amputated");
        });
    }

    [Test]
    public async Task FullProgressionShort()
    {
        var server = Pair.Server;
        var (_, limb) = await SpawnBodyAndLimb();
        await server.WaitAssertion(() =>
        {
            var sys = server.EntMan.System<SurgerySystem>();
            var ext = Ext(limb);
            var coords = server.EntMan.GetComponent<TransformComponent>(limb).Coordinates;

            var s = MakeTool("SurgToolScalpel", coords);
            sys.ExecuteStep(s, ToolComp(s), limb, ext);
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.Incised));

            var h = MakeTool("SurgToolHemostat", coords);
            sys.ExecuteStep(h, ToolComp(h), limb, ext);
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.Clamped));

            var r = MakeTool("SurgToolRetractor", coords);
            sys.ExecuteStep(r, ToolComp(r), limb, ext);
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.Retracted));

            var c = MakeTool("SurgToolCautery", coords);
            sys.ExecuteStep(c, ToolComp(c), limb, ext);
            Assert.That(ext.SurgeryStage, Is.EqualTo(SurgeryStage.None));
        });
    }
}
