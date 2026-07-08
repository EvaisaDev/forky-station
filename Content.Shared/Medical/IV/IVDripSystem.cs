using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.DragDrop;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Shared.Medical.IV;

public sealed partial class IVDripSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedBloodstreamSystem _bloodstream = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    [Dependency] private EntityQuery<BloodstreamComponent> _bloodstreamQuery = default!;
    [Dependency] private EntityQuery<FitsInDispenserComponent> _dispenserQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IVDripComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<IVDripComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<IVDripComponent, GetVerbsEvent<AlternativeVerb>>(AddAlternativeVerbs);
        SubscribeLocalEvent<IVDripComponent, DragDropTargetEvent>(OnDragDrop);
        SubscribeLocalEvent<IVDripComponent, CanDropTargetEvent>(OnCanDrop);
        SubscribeLocalEvent<IVDripComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<IVDripComponent, EntRemovedFromContainerMessage>(OnBeakerRemoved);
        SubscribeLocalEvent<IVDripComponent, EntInsertedIntoContainerMessage>(OnBeakerInserted);
        SubscribeLocalEvent<IVDripComponent, IVDripDoAfterEvent>(OnAttachDoAfter);
        SubscribeLocalEvent<IVDripComponent, IVDripDetachDoAfterEvent>(OnDetachDoAfter);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<IVDripComponent>();

        while (query.MoveNext(out var uid, out var iv))
        {
            if (!iv.Connected || iv.ConnectedPatient == null || curTime < iv.NextTransferTime)
                continue;

            iv.NextTransferTime = curTime + iv.TransferTime;
            Dirty(uid, iv);

            if (iv.TransferRate <= 0)
                continue;

            if (Deleted(iv.ConnectedPatient.Value) || !EntityManager.EntityExists(iv.ConnectedPatient.Value))
            {
                DisconnectDrip(uid, iv);
                continue;
            }

            if (!Transform(iv.ConnectedPatient.Value).Coordinates.TryDistance(EntityManager, Transform(uid).Coordinates, out var distance) || distance > 2f)
            {
                DisconnectDrip(uid, iv);
                continue;
            }

            ProcessTransfer(uid, iv);
        }
    }

    private void OnComponentInit(Entity<IVDripComponent> ent, ref ComponentInit args)
    {
        ent.Comp.NextTransferTime = _timing.CurTime + ent.Comp.TransferTime;
    }

    private void OnActivate(Entity<IVDripComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        var beaker = _itemSlots.GetItemOrNull(ent.Owner, IVDripComponent.BeakerSlotName);
        if (beaker != null)
        {
            _itemSlots.TryEject(ent.Owner, IVDripComponent.BeakerSlotName, args.User, out _);
            args.Handled = true;
            return;
        }

        if (ent.Comp.Connected)
        {
            DisconnectDrip(ent.Owner, ent.Comp);
            args.Handled = true;
        }
    }

    private void AddAlternativeVerbs(Entity<IVDripComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (ent.Comp.Connected)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Act = () => ToggleMode(ent.Owner, ent.Comp),
                Text = ent.Comp.Mode == IVDripMode.Inject
                    ? Loc.GetString("iv-drip-switch-draw")
                    : Loc.GetString("iv-drip-switch-inject")
            });
        }

        if (ent.Comp.Connected)
        {
            foreach (var rate in ent.Comp.AvailableTransferRates)
            {
                if (rate == ent.Comp.TransferRate)
                    continue;

                var capturedRate = rate;
                var capturedUser = args.User;
                args.Verbs.Add(new AlternativeVerb
                {
                    Act = () =>
                    {
                        ent.Comp.TransferRate = capturedRate;
                        Dirty(ent.Owner, ent.Comp);
                        _popup.PopupClient(Loc.GetString("iv-drip-rate-set", ("rate", capturedRate)), ent.Owner, capturedUser);
                    },
                    Text = Loc.GetString("iv-drip-set-rate", ("rate", rate))
                });
            }
        }
    }

    private void OnDragDrop(Entity<IVDripComponent> ent, ref DragDropTargetEvent args)
    {
        if (ent.Comp.Connected)
            return;

        if (!HasComp<BloodstreamComponent>(args.Dragged))
            return;

        var doAfterArgs = new DoAfterArgs(EntityManager, args.User, 3f, new IVDripDoAfterEvent(), ent, target: args.Dragged, used: ent)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = true,
        };
        _doAfter.TryStartDoAfter(doAfterArgs);
        args.Handled = true;
    }

    private void OnCanDrop(Entity<IVDripComponent> ent, ref CanDropTargetEvent args)
    {
        args.Handled = true;
        args.CanDrop |= HasComp<BloodstreamComponent>(args.Dragged) && !ent.Comp.Connected;
    }

    private void OnExamined(Entity<IVDripComponent> entity, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (entity.Comp.Connected && entity.Comp.ConnectedPatient != null)
        {
            args.PushMarkup(Loc.GetString("iv-drip-examine-connected", ("patient", Name(entity.Comp.ConnectedPatient.Value))));
            args.PushMarkup(Loc.GetString("iv-drip-examine-mode", ("mode", entity.Comp.Mode == IVDripMode.Inject ? "inject" : "draw")));
            args.PushMarkup(Loc.GetString("iv-drip-examine-rate", ("rate", entity.Comp.TransferRate)));
        }
        else
        {
            args.PushMarkup(Loc.GetString("iv-drip-examine-not-connected"));
        }

        var beaker = _itemSlots.GetItemOrNull(entity.Owner, IVDripComponent.BeakerSlotName);
        if (beaker != null)
            args.PushMarkup(Loc.GetString("iv-drip-examine-has-beaker"));
        else
            args.PushMarkup(Loc.GetString("iv-drip-examine-no-beaker"));
    }

    private void OnBeakerRemoved(Entity<IVDripComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == IVDripComponent.BeakerSlotName)
            UpdateAppearance(ent.Owner, ent.Comp);
    }

    private void OnBeakerInserted(Entity<IVDripComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == IVDripComponent.BeakerSlotName)
            UpdateAppearance(ent.Owner, ent.Comp);
    }

    private void OnAttachDoAfter(Entity<IVDripComponent> ent, ref IVDripDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Args.Target == null)
            return;

        if (ent.Comp.Connected)
            return;

        ConnectDrip(ent.Owner, args.Args.Target.Value, ent.Comp);
        args.Handled = true;
    }

    private void OnDetachDoAfter(Entity<IVDripComponent> ent, ref IVDripDetachDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        DisconnectDrip(ent.Owner, ent.Comp);
        args.Handled = true;
    }

    private void ProcessTransfer(EntityUid uid, IVDripComponent iv)
    {
        if (iv.ConnectedPatient == null)
            return;

        var patient = iv.ConnectedPatient.Value;

        if (!_bloodstreamQuery.TryComp(patient, out var bloodstream))
            return;

        var beaker = _itemSlots.GetItemOrNull(uid, IVDripComponent.BeakerSlotName);
        if (beaker == null || !beaker.Value.Valid)
            return;

        if (!_dispenserQuery.TryComp(beaker, out var fitsInDispenser))
            return;

        if (!_solutionContainer.TryGetFitsInDispenser((beaker.Value, fitsInDispenser), out var beakerSolution, out _))
            return;

        if (iv.Mode == IVDripMode.Inject)
        {
            var transferAmount = FixedPoint2.Min(iv.TransferRate, beakerSolution.Value.Comp.Solution.Volume);
            if (transferAmount <= 0)
                return;

            var solution = _solutionContainer.SplitSolution(beakerSolution.Value, transferAmount);
            _bloodstream.TryAddToBloodstream((patient, bloodstream), solution);
            UpdateAppearance(uid, iv);
        }
        else
        {
            if (!_solutionContainer.ResolveSolution(patient, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out var bloodSolution))
                return;

            var bloodEnt = bloodstream.BloodSolution;
            if (bloodEnt == null)
                return;

            var availableSpace = beakerSolution.Value.Comp.Solution.AvailableVolume;
            if (availableSpace <= 0)
                return;

            // Don't draw blood below safe threshold
            if (bloodSolution.Volume <= bloodstream.BloodReferenceSolution.Volume * 0.5f)
                return;

            var bloodVol = _solutionContainer.SplitSolution(bloodEnt.Value, iv.TransferRate);
            _solutionContainer.TryAddSolution(beakerSolution.Value, bloodVol);
            UpdateAppearance(uid, iv);
        }
    }

    public void ConnectDrip(EntityUid uid, EntityUid patient, IVDripComponent iv)
    {
        iv.ConnectedPatient = patient;
        iv.Connected = true;
        iv.NextTransferTime = _timing.CurTime + iv.TransferTime;
        Dirty(uid, iv);
        UpdateAppearance(uid, iv);

        _popup.PopupClient(Loc.GetString("iv-drip-connected", ("patient", patient)), uid, uid);
    }

    public void DisconnectDrip(EntityUid uid, IVDripComponent iv)
    {
        if (!iv.Connected || iv.ConnectedPatient == null)
            return;

        var patient = iv.ConnectedPatient.Value;
        iv.ConnectedPatient = null;
        iv.Connected = false;
        Dirty(uid, iv);
        UpdateAppearance(uid, iv);

        _popup.PopupClient(Loc.GetString("iv-drip-disconnected", ("patient", patient)), uid, uid);
    }

    public void ToggleMode(EntityUid uid, IVDripComponent iv)
    {
        iv.Mode = iv.Mode == IVDripMode.Inject ? IVDripMode.Draw : IVDripMode.Inject;
        Dirty(uid, iv);

        var modeName = iv.Mode == IVDripMode.Inject ? "inject" : "draw";
        _popup.PopupClient(Loc.GetString("iv-drip-mode-changed", ("mode", modeName)), uid, uid);
    }

    private void UpdateAppearance(EntityUid uid, IVDripComponent? iv = null)
    {
        if (!Resolve(uid, ref iv))
            return;

        if (!TryComp<AppearanceComponent>(uid, out var appearance))
            return;

        var hasBeaker = _itemSlots.GetItemOrNull(uid, IVDripComponent.BeakerSlotName) != null;
        _appearance.SetData(uid, IVDripVisuals.Connected, iv.Connected, appearance);
        _appearance.SetData(uid, IVDripVisuals.HasBeaker, hasBeaker, appearance);
    }
}
