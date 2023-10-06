namespace Deliveroo;

internal enum Stage
{
    TargetPersonnelOfficer,
    OpenGcSupply,
    SelectExpertDeliveryTab,
    SelectItemToTurnIn,
    TurnInSelected,
    FinalizeTurnIn1,
    FinalizeTurnIn2,
    FinalizeTurnIn3,
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
