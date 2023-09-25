using System.IO;
using Dalamud.Plugin;
using Newtonsoft.Json;

namespace Deliveroo;

internal sealed class CharacterConfiguration
{
    public ulong LocalContentId { get; set; }
    public string? CachedPlayerName { get; set; }
    public string? CachedWorldName { get; set; }

    public bool DisableForCharacter { get; set; } = false;
    public bool UseHideArmouryChestItemsFilter { get; set; } = false;

    public static string ResolveFilePath(DalamudPluginInterface pluginInterface, ulong localContentId)
        => Path.Join(pluginInterface.GetPluginConfigDirectory(), $"char.{localContentId:X}.json");

    public static CharacterConfiguration? Load(DalamudPluginInterface pluginInterface, ulong localContentId)
    {
        string path = ResolveFilePath(pluginInterface, localContentId);
        if (!File.Exists(path))
            return null;

        return JsonConvert.DeserializeObject<CharacterConfiguration>(File.ReadAllText(path));
    }

    public void Save(DalamudPluginInterface pluginInterface)
    {
        File.WriteAllText(ResolveFilePath(pluginInterface, LocalContentId), JsonConvert.SerializeObject(this, Formatting.Indented));
    }

    public void Delete(DalamudPluginInterface pluginInterface) =>
        File.Delete(ResolveFilePath(pluginInterface, LocalContentId));
}
