using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Deliveroo.External;

internal sealed class AllaganToolsIpc
{
    private readonly IPluginLog _pluginLog;

    private static readonly uint[] RetainerInventoryTypes = new[]
        {
            InventoryType.RetainerPage1,
            InventoryType.RetainerPage2,
            InventoryType.RetainerPage3,
            InventoryType.RetainerPage4,
            InventoryType.RetainerPage5,
            InventoryType.RetainerPage6,
            InventoryType.RetainerPage7,
        }
        .Select(x => (uint)x).ToArray();

    private readonly ICallGateSubscriber<uint, bool, uint[], uint> _itemCountOwned;

    public AllaganToolsIpc(DalamudPluginInterface pluginInterface, IPluginLog pluginLog)
    {
        _pluginLog = pluginLog;
        _itemCountOwned = pluginInterface.GetIpcSubscriber<uint, bool, uint[], uint>("AllaganTools.ItemCountOwned");
    }

    public uint GetRetainerItemCount(uint itemId)
    {
        try
        {
            uint itemCount = _itemCountOwned.InvokeFunc(itemId, true, RetainerInventoryTypes);
            _pluginLog.Verbose($"Found {itemCount} items in retainer inventories for itemId {itemId}");
            return itemCount;
        }
        catch (IpcError)
        {
            _pluginLog.Warning("Could not query allagantools for retainer inventory counts");
            return 0;
        }
    }
}
