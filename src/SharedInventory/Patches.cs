using System;
using CMS.Items;
using CMS.Multiplayer.Gameplay.Player.Inventory;
using CMS.Multiplayer.Gameplay.Player.Sync;
using CMS.Multiplayer.Packets.Player;
using CMS.Utils;
using Fusion;
using HarmonyLib;

namespace SharedInventory;

[HarmonyPatch]
internal static class Patches
{
    // ---------------- Host side ----------------

    /// <summary>Every packet a player sends to the host passes through here (after the game handled it).</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayersDataStore), nameof(PlayersDataStore.HandleReceivedPackets))]
    private static void AfterHostReceivedPacket(PlayerRef player, PacketType packetType)
    {
        if (packetType != PacketType.InventoryStackAdd &&
            packetType != PacketType.InventoryStackRemove &&
            packetType != PacketType.InventoryStackUpdate)
            return;

        Guard(() => SharedInventory.OnInventoryPacket(player));
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayersDataStore), nameof(PlayersDataStore.HandleInitialSyncRequest))]
    private static void BeforeInitialSyncRequest(PlayerRef player)
    {
        Guard(() => SharedInventory.OnInitialSyncRequest(player));
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayersDataStore), nameof(PlayersDataStore.Despawned))]
    private static void AfterSessionEnded()
    {
        SharedInventory.Reset();
    }

    // Host's own inventory changes (the game reports them through these list callbacks).

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerInventorySyncClient), nameof(PlayerInventorySyncClient.OnItemAdded))]
    private static void AfterItemAdded(PlayerInventorySyncClient __instance) => MarkHostDirty(__instance);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerInventorySyncClient), nameof(PlayerInventorySyncClient.OnItemRemoved))]
    private static void AfterItemRemoved(PlayerInventorySyncClient __instance) => MarkHostDirty(__instance);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerInventorySyncClient), nameof(PlayerInventorySyncClient.OnItemChanged))]
    private static void AfterItemChanged(PlayerInventorySyncClient __instance) => MarkHostDirty(__instance);

    /// <summary>Changes inside a stack (condition, item count) only show up as dirty flags.</summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerInventorySyncClient), nameof(PlayerInventorySyncClient.SendItemsChanges))]
    private static void BeforeSendItemsChanges(PlayerInventorySyncClient __instance)
    {
        if (SharedInventory.Applying || !SharedInventory.IsLocalHostClient(__instance))
            return;

        var items = __instance.localItemsCollection?.Items;
        if (items == null)
            return;

        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].CheckIsDirty())
            {
                SharedInventory.Dirty = true;
                return;
            }
        }
    }

    private static void MarkHostDirty(PlayerInventorySyncClient client)
    {
        if (!SharedInventory.Applying && SharedInventory.IsLocalHostClient(client))
            SharedInventory.Dirty = true;
    }

    // ---------------- Client side ----------------
    //
    // The game's full sync replaces the client's list with a new one and is meant to
    // run once, on join. For the re-syncs this mod sends, we let the game fill its new
    // list quietly and then copy the result into the original list, so the UI (which
    // listens to the original list) updates and no change is echoed to the host.

    private static PlayerInventorySyncClient resyncClient;
    private static ObservableList<ItemStack> resyncOriginalList;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerInventorySyncClient), nameof(PlayerInventorySyncClient.HandleFullSyncStartPacket))]
    private static void BeforeFullSyncStart(PlayerInventorySyncClient __instance)
    {
        if (!__instance.initialSyncDone || SharedInventory.IsHost)
            return;

        var collection = __instance.localItemsCollection;
        if (collection == null)
            return;

        // An earlier re-sync still in progress keeps its original list.
        if (resyncClient == null || resyncClient.Pointer != __instance.Pointer)
        {
            resyncClient = __instance;
            resyncOriginalList = collection.Items;
        }

        // Makes the game ignore the list events while it fills the new list.
        __instance.initialSyncDone = false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerInventorySyncClient), nameof(PlayerInventorySyncClient.HandleFullSyncEnd))]
    private static void AfterFullSyncEnd(PlayerInventorySyncClient __instance)
    {
        if (resyncClient == null || resyncClient.Pointer != __instance.Pointer)
            return;

        var original = resyncOriginalList;
        resyncClient = null;
        resyncOriginalList = null;

        Guard(() =>
        {
            var collection = __instance.localItemsCollection;
            var received = collection.Items;
            if (original == null || received == null || original.Pointer == received.Pointer)
                return;

            // Detach the game's handlers from the temporary list and put the original back.
            __instance.UnregisterItemsCollectionEvents();
            collection.Items = original;

            SharedInventory.WithSyncPaused(__instance, () => SharedInventory.CopyInto(original, received));
            __instance.initialSyncDone = true;
        });
    }

    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e);
        }
    }
}
