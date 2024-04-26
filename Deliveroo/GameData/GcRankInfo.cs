using System;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace Deliveroo.GameData
{
    internal sealed class GcRankInfo
    {
        public required string NameTwinAddersMale { private get; init; }
        public required string NameTwinAddersFemale { private get; init; }
        public required string NameMaelstromMale { private get; init; }
        public required string NameMaelstromFemale { private get; init; }
        public required string NameImmortalFlamesMale { private get; init; }
        public required string NameImmortalFlamesFemale { private get; init; }

        public required uint MaxSeals { get; init; }
        public required uint RequiredSeals { get; init; }
        public required byte RequiredHuntingLog { get; init; }

        public string GetName(GrandCompany grandCompany, bool female)
        {
            return (grandCompany, female) switch
            {
                (GrandCompany.TwinAdder, false) => NameTwinAddersMale,
                (GrandCompany.TwinAdder, true) => NameTwinAddersFemale,
                (GrandCompany.Maelstrom, false) => NameMaelstromMale,
                (GrandCompany.Maelstrom, true) => NameMaelstromFemale,
                (GrandCompany.ImmortalFlames, false) => NameImmortalFlamesMale,
                (GrandCompany.ImmortalFlames, true) => NameImmortalFlamesFemale,
                _ => throw new ArgumentOutOfRangeException(nameof(grandCompany) + "," + nameof(female)),
            };
        }
    }
}
