using System.Linq;
using Content.Shared.Administration.Logs;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Climbing.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.DragDrop;
using Content.Shared.Emag.Systems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.MedicalScanner;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared.Medical.Sleeper;

public abstract partial class SharedSleeperSystem : EntitySystem
{
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] protected IGameTiming Timing = default!;
    [Dependency] private ClimbSystem _climb = default!;
    [Dependency] private EmagSystem _emag = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] protected SharedAppearanceSystem Appearance = default!;
    [Dependency] private SharedBloodstreamSystem _bloodstream = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] protected SharedUserInterfaceSystem UI = default!;
    [Dependency] private StandingStateSystem _standingState = default!;

    [Dependency] private EntityQuery<BloodstreamComponent> _bloodstreamQuery = default!;
    [Dependency] private EntityQuery<ItemSlotsComponent> _itemSlotsQuery = default!;
    [Dependency] private EntityQuery<FitsInDispenserComponent> _dispenserQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SleeperComponent, CanDropTargetEvent>(OnSleeperCanDropOn);
        SubscribeLocalEvent<SleeperComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<SleeperComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<SleeperComponent, GetVerbsEvent<AlternativeVerb>>(AddAlternativeVerbs);
        SubscribeLocalEvent<SleeperComponent, GotEmaggedEvent>(OnEmagged);
        SubscribeLocalEvent<SleeperComponent, DragDropTargetEvent>(HandleDragDropOn);
        SubscribeLocalEvent<SleeperComponent, EntRemovedFromContainerMessage>(OnEjected);
        SubscribeLocalEvent<SleeperComponent, EntInsertedIntoContainerMessage>(OnBodyInserted);
        SubscribeLocalEvent<SleeperComponent, PowerChangedEvent>(OnPowerChanged);

        Subs.BuiEvents<SleeperComponent>(SleeperUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnBoundUiOpened);
            subs.Event<SleeperToggleFilterMessage>(OnToggleFilterMessage);
            subs.Event<SleeperTogglePumpMessage>(OnTogglePumpMessage);
            subs.Event<SleeperSetStasisMessage>(OnSetStasisMessage);
            subs.Event<SleeperInjectChemicalMessage>(OnInjectChemicalMessage);
            subs.Event<SleeperEjectPatientMessage>(OnEjectPatientMessage);
            subs.Event<SleeperEjectBeakerMessage>(OnEjectBeakerMessage);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = Timing.CurTime;
        var query = EntityQueryEnumerator<SleeperComponent>();

        while (query.MoveNext(out var uid, out var sleeper))
        {
            if (curTime < sleeper.NextInjectionTime)
                continue;

            sleeper.NextInjectionTime += sleeper.BeakerTransferTime;
            Dirty(uid, sleeper);
            UpdateInjection((uid, sleeper));
        }
    }

    private void UpdateInjection(Entity<SleeperComponent> entity)
    {
        var patient = entity.Comp.BodyContainer.ContainedEntity;

        if (patient == null)
            return;

        if (entity.Comp.Filtering)
            ProcessDialysis(entity, patient.Value);

        if (entity.Comp.Pump)
            ProcessPump(entity, patient.Value);

        if (entity.Comp.StasisSetting > 1)
        {
            var ev = new ApplyMetabolicMultiplierEvent(1f / entity.Comp.StasisSetting);
            RaiseLocalEvent(patient.Value, ref ev);
        }

        UpdateUi(entity);
    }

    private void ProcessDialysis(Entity<SleeperComponent> entity, EntityUid patient)
    {
        if (!_bloodstreamQuery.TryComp(patient, out var bloodstream))
            return;

        if (!_solutionContainer.ResolveSolution(patient, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out var _))
            return;

        if (!_solutionContainer.TryGetSolution(entity.Owner, "dialysisBuffer", out var dialysisSolutionEnt, out _) || dialysisSolutionEnt == null)
            return;

        var bloodEnt = bloodstream.BloodSolution;
        if (bloodEnt == null)
            return;

        var filterSpeed = entity.Comp.PumpSpeed;
        var bloodVol = _solutionContainer.SplitSolution(bloodEnt.Value, filterSpeed);
        _solutionContainer.TryAddSolution(dialysisSolutionEnt.Value, bloodVol);
    }

    private void ProcessPump(Entity<SleeperComponent> entity, EntityUid patient)
    {
        if (!_bloodstreamQuery.TryComp(patient, out var bloodstream))
            return;

        if (!_solutionContainer.TryGetSolution(entity.Owner, "stomachBuffer", out var pumpSolutionEnt, out _) || pumpSolutionEnt == null)
            return;

        if (!_solutionContainer.ResolveSolution(patient, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out var _))
            return;

        var bloodEnt = bloodstream.BloodSolution;
        if (bloodEnt == null)
            return;

        var pumpSpeed = entity.Comp.PumpSpeed;
        var removed = _solutionContainer.SplitSolution(bloodEnt.Value, pumpSpeed);
        _solutionContainer.TryAddSolution(pumpSolutionEnt.Value, removed);
    }

    private void HandleDragDropOn(Entity<SleeperComponent> ent, ref DragDropTargetEvent args)
    {
        if (ent.Comp.BodyContainer.ContainedEntity != null)
            return;

        InsertBody(ent.Owner, args.Dragged, ent.Comp);
        args.Handled = true;
    }

    private void OnSleeperCanDropOn(EntityUid uid, SleeperComponent component, ref CanDropTargetEvent args)
    {
        args.Handled = true;
        args.CanDrop |= HasComp<BodyComponent>(args.Dragged);
    }

    private void OnComponentInit(EntityUid uid, SleeperComponent sleeperComponent, ComponentInit args)
    {
        sleeperComponent.BodyContainer = _container.EnsureContainer<ContainerSlot>(uid, SleeperComponent.BodyContainerName);
    }

    private void OnPowerChanged(Entity<SleeperComponent> ent, ref PowerChangedEvent args)
    {
        if (Terminating(ent))
            return;

        UpdateAppearance(ent.Owner, ent.Comp);
    }

    private void OnExamined(Entity<SleeperComponent> entity, ref ExaminedEvent args)
    {
        var container = _itemSlots.GetItemOrNull(entity.Owner, SleeperComponent.BeakerSlotName);
        if (args.IsInDetailsRange && container != null && _solutionContainer.TryGetFitsInDispenser(container.Value, out _, out var containerSolution))
        {
            using (args.PushGroup(nameof(SleeperComponent)))
            {
                args.PushMarkup(Loc.GetString("sleeper-examine", ("beaker", Name(container.Value))));
                if (containerSolution.Volume == 0)
                    args.PushMarkup(Loc.GetString("sleeper-empty-beaker"));
            }
        }
    }

    private void OnEjected(Entity<SleeperComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == SleeperComponent.BodyContainerName)
            ClearInjectionBuffer(ent);
        UpdateUi(ent);
    }

    private void OnBodyInserted(Entity<SleeperComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == SleeperComponent.BodyContainerName)
        {
            UI.CloseUi(ent.Owner, SleeperUiKey.Key, args.Entity);
            ClearInjectionBuffer(ent);
        }
        UpdateUi(ent);
    }

    private void OnToggleFilterMessage(Entity<SleeperComponent> ent, ref SleeperToggleFilterMessage msg)
    {
        ent.Comp.Filtering = msg.Filtering;
        Dirty(ent.Owner, ent.Comp);
        UpdateUi(ent);
    }

    private void OnTogglePumpMessage(Entity<SleeperComponent> ent, ref SleeperTogglePumpMessage msg)
    {
        ent.Comp.Pump = msg.Pump;
        Dirty(ent.Owner, ent.Comp);
        UpdateUi(ent);
    }

    private void OnSetStasisMessage(Entity<SleeperComponent> ent, ref SleeperSetStasisMessage msg)
    {
        if (ent.Comp.StasisSettings.Contains(msg.StasisSetting))
        {
            ent.Comp.StasisSetting = msg.StasisSetting;
            Dirty(ent.Owner, ent.Comp);
            UpdateUi(ent);
        }
    }

    private void OnInjectChemicalMessage(Entity<SleeperComponent> ent, ref SleeperInjectChemicalMessage msg)
    {
        if (!ent.Comp.AvailableChemicals.Contains(msg.Chemical))
            return;

        var patient = ent.Comp.BodyContainer.ContainedEntity;
        if (patient == null)
            return;

        if (!_bloodstreamQuery.TryComp(patient.Value, out var bloodstream))
            return;

        if (!_solutionContainer.ResolveSolution(patient.Value, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out var bloodSolution))
            return;

        var currentAmount = bloodSolution.GetTotalPrototypeQuantity(msg.Chemical);
        if (currentAmount + msg.Amount > 20)
        {
            _popup.PopupClient(Loc.GetString("sleeper-too-many-chemicals"), ent.Owner, msg.Actor);
            return;
        }

        var solution = new Solution();
        solution.AddReagent(msg.Chemical, msg.Amount);
        _bloodstream.TryAddToBloodstream((patient.Value, bloodstream), solution);

        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(msg.Actor)} injected {msg.Amount}u {msg.Chemical} into {ToPrettyString(patient.Value)} via {ToPrettyString(ent.Owner)}");

        UpdateUi(ent);
    }

    private void OnEjectPatientMessage(Entity<SleeperComponent> ent, ref SleeperEjectPatientMessage msg)
    {
        if (ent.Comp.Locked)
        {
            _popup.PopupClient(Loc.GetString("sleeper-locked"), ent.Owner, msg.Actor);
            return;
        }

        var ejected = EjectBody(ent.Owner, ent.Comp);
        if (ejected != null)
            _adminLogger.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(ejected.Value)} ejected from {ToPrettyString(ent.Owner)} by {ToPrettyString(msg.Actor)}");
    }

    private void OnEjectBeakerMessage(Entity<SleeperComponent> ent, ref SleeperEjectBeakerMessage msg)
    {
        TryEjectBeaker(ent, msg.Actor);
    }

    private void AddAlternativeVerbs(EntityUid uid, SleeperComponent sleeperComponent, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (sleeperComponent.BodyContainer.ContainedEntity != null)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("sleeper-verb-eject"),
                Category = VerbCategory.Eject,
                Priority = 1,
                Act = () =>
                {
                    if (!sleeperComponent.Locked)
                        EjectBody(uid, sleeperComponent);
                    else
                        _popup.PopupClient(Loc.GetString("sleeper-locked"), uid, args.User);
                }
            });
        }
        else if (HasComp<BodyComponent>(args.User) && _mobState.IsAlive(args.User))
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Act = () => InsertBody(uid, args.User, sleeperComponent),
                Text = Loc.GetString("medical-scanner-verb-enter")
            });
        }
    }

    private void OnEmagged(EntityUid uid, SleeperComponent? sleeperComponent, ref GotEmaggedEvent args)
    {
        if (!Resolve(uid, ref sleeperComponent))
            return;

        if (!_emag.CompareFlag(args.Type, EmagType.Interaction))
            return;

        sleeperComponent.Locked = !sleeperComponent.Locked;
        Dirty(uid, sleeperComponent);
        args.Handled = true;
    }

    protected void UpdateAppearance(EntityUid uid, SleeperComponent? sleeper = null)
    {
        if (!Resolve(uid, ref sleeper))
            return;

        var hasOccupant = sleeper.BodyContainer?.ContainedEntity != null;

        if (TryComp<AppearanceComponent>(uid, out var appearance))
        {
            Appearance.SetData(uid, SleeperVisuals.ContainsEntity, hasOccupant, appearance);
        }
    }

    public bool InsertBody(EntityUid uid, EntityUid target, SleeperComponent sleeperComponent)
    {
        if (sleeperComponent.BodyContainer.ContainedEntity != null)
            return false;

        if (!HasComp<MobStateComponent>(target))
            return false;

        var xform = Transform(target);
        _container.Insert((target, xform), sleeperComponent.BodyContainer);
        _standingState.Stand(target, force: true);

        UpdateAppearance(uid, sleeperComponent);
        return true;
    }

    public EntityUid? EjectBody(EntityUid uid, SleeperComponent? sleeperComponent)
    {
        if (!Resolve(uid, ref sleeperComponent))
            return null;

        if (sleeperComponent.BodyContainer.ContainedEntity is not { Valid: true } contained)
            return null;

        _container.Remove(contained, sleeperComponent.BodyContainer);

        if (HasComp<KnockedDownComponent>(contained) || _mobState.IsIncapacitated(contained))
            _standingState.Down(contained);
        else
            _standingState.Stand(contained);

        _climb.ForciblySetClimbing(contained, uid);
        UpdateAppearance(uid, sleeperComponent);
        return contained;
    }

    public void TryEjectBeaker(Entity<SleeperComponent> sleeper, EntityUid? user)
    {
        if (_itemSlots.TryEject(sleeper.Owner, SleeperComponent.BeakerSlotName, user, out var beaker) && user != null)
            _hands.PickupOrDrop(user.Value, beaker.Value);
    }

    public void ClearInjectionBuffer(Entity<SleeperComponent> sleeper)
    {
        if (_solutionContainer.TryGetSolution(sleeper.Owner, SleeperComponent.InjectionBufferSolutionName, out var injectingSolution, out _))
            _solutionContainer.RemoveAllSolution(injectingSolution.Value);
    }

    protected (FixedPoint2? capacity, List<ReagentQuantity>? reagents) GetBeakerInfo(Entity<SleeperComponent> entity)
    {
        if (!_itemSlotsQuery.TryComp(entity, out var itemSlotsComponent))
            return (null, null);

        var beaker = _itemSlots.GetItemOrNull(entity.Owner, SleeperComponent.BeakerSlotName, itemSlotsComponent);

        if (beaker == null || !beaker.Value.Valid || !_dispenserQuery.TryComp(beaker, out var fitsInDispenser) || !_solutionContainer.TryGetFitsInDispenser((beaker.Value, fitsInDispenser), out var containerSolution, out _))
            return (null, null);

        var capacity = containerSolution.Value.Comp.Solution.MaxVolume;
        var reagents = containerSolution.Value.Comp.Solution.Contents
            .Select(reagent => new ReagentQuantity(reagent.Reagent, reagent.Quantity))
            .ToList();

        return (capacity, reagents);
    }

    protected List<ReagentQuantity>? GetInjectingReagents(Entity<SleeperComponent> entity)
    {
        if (!_solutionContainer.TryGetSolution(entity.Owner, SleeperComponent.InjectionBufferSolutionName, out var injectingSolution, out _))
            return null;

        return injectingSolution.Value.Comp.Solution.Contents
            .Select(reagent => new ReagentQuantity(reagent.Reagent, reagent.Quantity))
            .ToList();
    }

    private void OnBoundUiOpened(Entity<SleeperComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    protected abstract void UpdateUi(Entity<SleeperComponent> sleeper);
}
