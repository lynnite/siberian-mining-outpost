using Content.Shared.CCVar;
using Content.Shared.Station.Systems;
using Content.Shared.Train.Components;
using Robust.Shared.Configuration;

namespace Content.Shared.Train;

public abstract partial class SharedTrainMoneySystem : EntitySystem
{
    [Dependency] protected IConfigurationManager Cfg = default!;
    [Dependency] protected StationSystem Station = default!;

    public StationTrainMoneyComponent EnsureMoney(EntityUid station)
    {
        var comp = EnsureComp<StationTrainMoneyComponent>(station);

        if (!comp.Initialised)
        {
            comp.Balance = Cfg.GetCVar(CCVars.TrainStartingMoney);
            comp.Initialised = true;
            Dirty(station, comp);
        }

        return comp;
    }

    public bool TryGetStationMoney(EntityUid entity, out EntityUid station, out StationTrainMoneyComponent money)
    {
        station = EntityUid.Invalid;
        money = default!;

        var owner = Station.GetOwningStation(entity);
        if (owner is not { } stationUid)
            return false;

        station = stationUid;
        money = EnsureMoney(stationUid);
        return true;
    }

    public int GetBalance(EntityUid station)
    {
        return EnsureMoney(station).Balance;
    }

    protected bool InternalTryWithdraw(EntityUid station, int amount)
    {
        if (amount < 0)
            return false;

        var money = EnsureMoney(station);

        if (money.Balance < amount)
            return false;

        money.Balance -= amount;
        Dirty(station, money);
        RaiseBalanceUpdated(station, money.Balance);
        return true;
    }

    protected void InternalDeposit(EntityUid station, int amount)
    {
        if (amount <= 0)
            return;

        var money = EnsureMoney(station);
        money.Balance += amount;
        Dirty(station, money);
        RaiseBalanceUpdated(station, money.Balance);
    }

    protected void RaiseBalanceUpdated(EntityUid station, int balance)
    {
        var ev = new TrainMoneyUpdatedEvent(station, balance);
        RaiseLocalEvent(station, ref ev, true);
    }
}

[ByRefEvent]
public readonly record struct TrainMoneyUpdatedEvent(EntityUid Station, int Balance);


