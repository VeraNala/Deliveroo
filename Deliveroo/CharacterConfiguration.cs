using System.Collections.Generic;
using System.IO;
using Dalamud.Plugin;
using Newtonsoft.Json;

namespace Deliveroo;

internal sealed class CharacterConfiguration
{
    public ulong LocalContentId { get; set; }
    public string? CachedPlayerName { get; set; }
    public string? CachedWorldName { get; set; }

    public bool DisableForCharacter { get; set; }
    public bool UseHideArmouryChestItemsFilter { get; set; }
    public bool IgnoreMinimumSealsToKeep { get; set; }
    public bool OverrideItemsToPurchase { get; set; }
    public List<Configuration.PurchasePriority> ItemsToPurchase { get; set; } = new();

    public static string ResolveFilePath(IDalamudPluginInterface pluginInterface, ulong localContentId)
        => Path.Join(pluginInterface.GetPluginConfigDirectory(), $"char.{localContentId:X}.json");

    public static CharacterConfiguration? Load(IDalamudPluginInterface pluginInterface, ulong localContentId)
    {
        string path = ResolveFilePath(pluginInterface, localContentId);
        if (!File.Exists(path))
            return null;

        return JsonConvert.DeserializeObject<CharacterConfiguration>(File.ReadAllText(path));
    }

    public void Save(IDalamudPluginInterface pluginInterface)
    {
        File.WriteAllText(ResolveFilePath(pluginInterface, LocalContentId), JsonConvert.SerializeObject(this, Formatting.Indented));
    }

    public void Delete(IDalamudPluginInterface pluginInterface) =>
        File.Delete(ResolveFilePath(pluginInterface, LocalContentId));
}
