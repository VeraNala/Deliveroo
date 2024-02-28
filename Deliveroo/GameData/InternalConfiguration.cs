using System.Collections.Generic;
using System.Linq;

namespace Deliveroo.GameData;

internal static class InternalConfiguration
{
    public static readonly IReadOnlyList<uint> QuickVentureExclusiveItems = new List<uint>
        {
            // Weapons
            1662, // Thormoen's Pride
            1799, // Sibold's Reach
            1870, // Gerbald's Redspike
            1731, // Symon's Honeyclaws
            1940, // Alesone's Songbow
            2132, // Aubriest's Whisper
            2043, // Chiran Zabran's Tempest

            2271, // Thormoen's Purpose
            2290, // Aubriest's Allegory

            // Armor
            2752, // Peregrine Helm
            2820, // Red Onion Helm
            2823, // Veteran's Pot Helm
            2822, // Explorer's Bandana
            2821, // Explorer's Calot
            3170, // Explorer's Tabard
            3168, // Veteran's Acton
            3167, // Explorer's Tunic
            3171, // Mage's Halfrobe
            3676, // Spiked Armguards
            3644, // Mage's Halfgloves
            3418, // Thormoen's Subligar
            3420, // Explorer's Breeches
            3419, // Mage's Chausses
            3864, // Explorer's Sabatons
            3865, // Explorer's Moccasins
            3863, // Mage's Pattens

            // Accessories
            4256, // Stonewall Earrings
            4249, // Explorer's Earrings
            4250, // Mage's Earrings
            4257, // Blessed Earrings
            4359, // Stonewall Choker
            4357, // Explorer's Choker
            4358, // Mage's Choker
            4498, // Stonewall Ring
            4483, // Explorer's Ring
            4484, // Mage's Ring
            4499, // Blessed Ring
        }
        .ToList()
        .AsReadOnly();
}
