using Content.Server.Popups;
using Content.Shared.CCVar;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Station.Components;
using Content.Shared.Train;
using Content.Shared.Train.Components;
using Robust.Shared.Configuration;

namespace Content.Server.Train;

public sealed partial class TrainMoneySystem : SharedTrainMoneySystem
{
    [Dependency] private PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StationTrainMoneyComponent, MapInitEvent>(OnMoneyMapInit);
        SubscribeLocalEvent<TrainMoneyCounterComponent, UseInHandEvent>(OnCounterUseInHand);
        SubscribeLocalEvent<TrainMoneyCounterComponent, ActivateInWorldEvent>(OnCounterActivate);

        Cfg.OnValueChanged(CCVars.TrainStartingMoney, OnStartingMoneyChanged);
    }

    private void OnMoneyMapInit(Entity<StationTrainMoneyComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Initialised)
            return;

        ent.Comp.Balance = Cfg.GetCVar(CCVars.TrainStartingMoney);
        ent.Comp.Initialised = true;
        Dirty(ent);
        RaiseBalanceUpdated(ent.Owner, ent.Comp.Balance);
    }

    private void OnStartingMoneyChanged(int value)
    {
    }

    private void OnCounterUseInHand(Entity<TrainMoneyCounterComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        ShowBalance(ent, args.User);
        args.Handled = true;
    }

    private void OnCounterActivate(Entity<TrainMoneyCounterComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        ShowBalance(ent, args.User);
        args.Handled = true;
    }

    private void ShowBalance(Entity<TrainMoneyCounterComponent> ent, EntityUid user)
    {
        EntityUid? station = ent.Comp.Station;

        if (station == null)
            station = Station.GetOwningStation(ent.Owner);

        if (station == null)
            return;

        var balance = GetBalance(station.Value);
        _popup.PopupEntity(Loc.GetString("train-money-counter-balance", ("balance", balance)), ent.Owner, user);
    }

    public bool TrySpend(EntityUid station, int amount)
    {
        return InternalTryWithdraw(station, amount);
    }

    public void Add(EntityUid station, int amount)
    {
        InternalDeposit(station, amount);
    }
}
