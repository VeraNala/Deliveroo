namespace Deliveroo;

internal enum Stage
{
    TargetPersonnelOfficer,
    OpenGcSupply,
    SelectExpertDeliveryTab,
    SelectItemToTurnIn,
    TurnInSelected,
    FinalizeTurnIn,
    CloseGcSupply,
    CloseGcSupplyThenStop,

    TargetQuartermaster,
    SelectRewardTier,
    SelectRewardSubCategory,
    SelectReward,
    ConfirmReward,
    CloseGcExchange,

    RequestStop,
    Stopped,
}
