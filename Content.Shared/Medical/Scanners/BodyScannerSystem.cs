using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Climbing.Systems;
using Content.Shared.DragDrop;
using Content.Shared.MedicalScanner;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Shared.Medical.Scanners;

public abstract partial class SharedBodyScannerSystem : EntitySystem
{
    [Dependency] private ClimbSystem _climb = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] protected SharedAppearanceSystem Appearance = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] protected SharedUserInterfaceSystem UI = default!;
    [Dependency] private StandingStateSystem _standingState = default!;
    [Dependency] protected IGameTiming Timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BodyScannerComponent, CanDropTargetEvent>(OnCanDropOn);
        SubscribeLocalEvent<BodyScannerComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<BodyScannerComponent, GetVerbsEvent<InteractionVerb>>(AddInsertOtherVerb);
        SubscribeLocalEvent<BodyScannerComponent, GetVerbsEvent<AlternativeVerb>>(AddAlternativeVerbs);
        SubscribeLocalEvent<BodyScannerComponent, DragDropTargetEvent>(HandleDragDropOn);
        SubscribeLocalEvent<BodyScannerComponent, EntRemovedFromContainerMessage>(OnEjected);
        SubscribeLocalEvent<BodyScannerComponent, EntInsertedIntoContainerMessage>(OnBodyInserted);

        Subs.BuiEvents<BodyScannerComponent>(BodyScannerUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnBoundUiOpened);
            subs.Event<BodyScannerScanMessage>(OnScanMessage);
            subs.Event<BodyScannerEjectMessage>(OnEjectMessage);
        });
    }

    private void OnCanDropOn(EntityUid uid, BodyScannerComponent component, ref CanDropTargetEvent args)
    {
        args.Handled = true;
        args.CanDrop |= HasComp<BodyComponent>(args.Dragged);
    }

    private void OnComponentInit(EntityUid uid, BodyScannerComponent scannerComponent, ComponentInit args)
    {
        scannerComponent.BodyContainer = _container.EnsureContainer<ContainerSlot>(uid, BodyScannerComponent.BodyContainerName);
    }

    private void AddInsertOtherVerb(EntityUid uid, BodyScannerComponent component, GetVerbsEvent<InteractionVerb> args)
    {
        if (args.Using == null || !args.CanAccess || !args.CanInteract || IsOccupied(component) || !HasComp<BodyComponent>(args.Using.Value))
            return;

        var name = "Unknown";
        if (TryComp(args.Using.Value, out MetaDataComponent? metadata))
            name = metadata.EntityName;

        args.Verbs.Add(new InteractionVerb
        {
            Act = () => InsertBody(uid, args.Using.Value, component),
            Category = VerbCategory.Insert,
            Text = name
        });
    }

    private void AddAlternativeVerbs(EntityUid uid, BodyScannerComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (IsOccupied(component))
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Act = () => EjectBody(uid, component),
                Category = VerbCategory.Eject,
                Priority = 1
            });
        }

        if (!IsOccupied(component) && HasComp<BodyComponent>(args.User))
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Act = () => InsertBody(uid, args.User, component),
                Text = Loc.GetString("medical-scanner-verb-enter")
            });
        }
    }

    private void HandleDragDropOn(Entity<BodyScannerComponent> ent, ref DragDropTargetEvent args)
    {
        if (ent.Comp.BodyContainer.ContainedEntity != null)
            return;

        InsertBody(ent.Owner, args.Dragged, ent.Comp);
        args.Handled = true;
    }

    private void OnEjected(Entity<BodyScannerComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == BodyScannerComponent.BodyContainerName)
            UpdateUi(ent);
    }

    private void OnBodyInserted(Entity<BodyScannerComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == BodyScannerComponent.BodyContainerName)
            UI.CloseUi(ent.Owner, BodyScannerUiKey.Key, args.Entity);

        UpdateUi(ent);
    }

    private void OnBoundUiOpened(Entity<BodyScannerComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnScanMessage(Entity<BodyScannerComponent> ent, ref BodyScannerScanMessage msg)
    {
        PerformScan(ent);
    }

    private void OnEjectMessage(Entity<BodyScannerComponent> ent, ref BodyScannerEjectMessage msg)
    {
        EjectBody(ent.Owner, ent.Comp);
    }

    private void UpdateAppearance(EntityUid uid, BodyScannerComponent? scanner = null)
    {
        if (!Resolve(uid, ref scanner))
            return;

        if (TryComp<AppearanceComponent>(uid, out var appearance))
            Appearance.SetData(uid, BodyScannerVisuals.Status, GetStatus(uid, scanner), appearance);
    }

    private BodyScannerStatus GetStatus(EntityUid uid, BodyScannerComponent scanner)
    {
        var body = scanner.BodyContainer.ContainedEntity;
        if (body == null)
            return BodyScannerStatus.Open;

        if (!TryComp<MobStateComponent>(body.Value, out var state))
            return BodyScannerStatus.Yellow;

        if (_mobState.IsAlive(body.Value, state))
            return BodyScannerStatus.Green;

        if (_mobState.IsCritical(body.Value, state))
            return BodyScannerStatus.Red;

        if (_mobState.IsDead(body.Value, state))
            return BodyScannerStatus.Death;

        return BodyScannerStatus.Yellow;
    }

    private static bool IsOccupied(BodyScannerComponent scannerComponent)
    {
        return scannerComponent.BodyContainer.ContainedEntity != null;
    }

    public bool InsertBody(EntityUid uid, EntityUid target, BodyScannerComponent scannerComponent)
    {
        if (scannerComponent.BodyContainer.ContainedEntity != null)
            return false;

        if (!HasComp<BodyComponent>(target))
            return false;

        var xform = Transform(target);
        _container.Insert((target, xform), scannerComponent.BodyContainer);
        _standingState.Stand(target, force: true);

        UpdateAppearance(uid, scannerComponent);
        return true;
    }

    public EntityUid? EjectBody(EntityUid uid, BodyScannerComponent? scannerComponent)
    {
        if (!Resolve(uid, ref scannerComponent))
            return null;

        if (scannerComponent.BodyContainer.ContainedEntity is not { Valid: true } contained)
            return null;

        _container.Remove(contained, scannerComponent.BodyContainer);

        if (HasComp<KnockedDownComponent>(contained) || _mobState.IsIncapacitated(contained))
            _standingState.Down(contained);
        else
            _standingState.Stand(contained);

        _climb.ForciblySetClimbing(contained, uid);
        UpdateAppearance(uid, scannerComponent);
        return contained;
    }

    protected abstract void PerformScan(Entity<BodyScannerComponent> entity);
    protected abstract void UpdateUi(Entity<BodyScannerComponent> scanner);
}
