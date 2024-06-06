namespace Deliveroo;

internal enum Stage
{
    TargetPersonnelOfficer,
    OpenGcSupply,
    SelectExpertDeliveryTab,
    SelectItemToTurnIn,
    TurnInSelected,
    FinalizeTurnIn,
    CloseGcSupplySelectString,
    CloseGcSupplySelectStringThenStop,
    CloseGcSupplyWindowThenStop,

    TargetQuartermaster,
    SelectRewardTier,
    SelectRewardSubCategory,
    SelectReward,
    ConfirmReward,
    CloseGcExchange,

    RequestStop,
    Stopped,

    SingleFinalizeTurnIn,
}
