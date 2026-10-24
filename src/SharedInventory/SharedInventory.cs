using System;
using System.Collections.Generic;
using CMS.Items;
using CMS.Multiplayer.Containers;
using CMS.Multiplayer.Gameplay.Player.Inventory;
using CMS.Multiplayer.Gameplay.Player.Sync;
using CMS.Multiplayer.Session;
using CMS.Utils;
using Fusion;
using Il2CppList = Il2CppSystem.Collections.Generic.List<CMS.Items.ItemStack>;
using Object = UnityEngine.Object;

namespace SharedInventory;

/// <summary>
/// How it works:
/// - The game gives every player a private inventory. Clients send each change
///   (add / remove / update stack) to the host, which keeps a copy per player
///   in <see cref="SessionPlayerData.Inventory"/>.
/// - With this mod the host's own inventory is the shared one ("master").
///   Whenever a client's change arrives, the host copies that client's list into
///   the master; whenever the master changes, the host re-sends it to everyone
///   using the game's own "inventory full sync" packets.
/// - Clients apply a re-sync in place (see <see cref="Patches"/>), so the game's
///   UI keeps working and nothing is echoed back to the host.
/// </summary>
internal static class SharedInventory
{
    /// <summary>Master changed and must be pushed to the clients.</summary>
    internal static bool Dirty;

    /// <summary>True while the mod itself is writing into an inventory list.</summary>
    internal static bool Applying;

    private static PlayersDataStore store;
    private static PlayerInventorySyncServer server;
    private static PlayerInventorySyncClient hostClient;

    internal static NetworkRunner Runner => server != null ? server.Runner : null;

    internal static bool IsHost
    {
        get
        {
            var runner = Runner;
            return runner != null && runner.IsServer;
        }
    }

    private static bool Resolve()
    {
        if (server == null)
            server = Object.FindObjectOfType<PlayerInventorySyncServer>();
        if (store == null)
            store = Object.FindObjectOfType<PlayersDataStore>();
        if (hostClient == null)
        {
            foreach (var client in Object.FindObjectsOfType<PlayerInventorySyncClient>())
            {
                if (client.HasInputAuthority)
                {
                    hostClient = client;
                    break;
                }
            }
        }

        return server != null && store != null && hostClient != null && IsHost;
    }

    /// <summary>The host's local inventory list (the shared inventory).</summary>
    private static ObservableList<ItemStack> Master
    {
        get
        {
            var collection = hostClient != null ? hostClient.localItemsCollection : null;
            return collection != null ? collection.Items : null;
        }
    }

    internal static bool IsLocalHostClient(PlayerInventorySyncClient client)
    {
        return IsHost && client.HasInputAuthority;
    }

    /// <summary>Player ids (save keys) whose own inventory was already merged into the master.</summary>
    private static readonly HashSet<string> merged = new();

    /// <summary>Last broadcast state of the master, to notice the host's own changes.</summary>
    private static long lastSignature;

    /// <summary>Host: a client sent an inventory change packet (already applied to its shadow list).</summary>
    internal static void OnInventoryPacket(PlayerRef sender)
    {
        if (!Resolve())
            return;

        if (sender == Runner.LocalPlayer)
        {
            // Host's own change: master already has it.
            Dirty = true;
            return;
        }

        var data = store.GetPlayerData(sender);
        var master = Master;
        if (data == null || data.Inventory == null || master == null)
            return;

        if (MergeIfNew(PlayerId(sender), data))
            return;

        // The sender's shadow list = the shared inventory as it saw it + its change.
        WithSyncPaused(hostClient, () => CopyInto(master, data.Inventory));
        Dirty = true;
    }

    /// <summary>Host: a player is joining; add their items to the master and send them the master.</summary>
    internal static void OnInitialSyncRequest(PlayerRef player)
    {
        if (!Resolve() || player == Runner.LocalPlayer)
            return;

        var data = store.GetPlayerData(player);
        var master = Master;
        if (data == null || master == null)
            return;

        MergeIfNew(PlayerId(player), data);
        data.Inventory = Clone(master);
    }

    /// <summary>
    /// Host, every frame (throttled by the driver): merge saved inventories not merged yet and
    /// notice the host's own inventory changes (the game raises no event for those on the host).
    /// </summary>
    internal static void Poll()
    {
        if (!Resolve())
            return;

        var master = Master;
        if (master == null)
            return;

        var hostId = PlayerId(Runner.LocalPlayer);
        if (hostId == null)
            return;

        foreach (var entry in store.sessionPlayersData)
        {
            if (entry.Key != hostId)
                MergeIfNew(entry.Key, entry.Value);
        }

        if (Signature(master) != lastSignature)
            Dirty = true;
    }

    /// <summary>Host: push the master to every remote player.</summary>
    internal static void Broadcast()
    {
        if (!Resolve())
            return;

        var master = Master;
        if (master == null)
            return;

        // Every saved inventory (also players who are offline) mirrors the master,
        // so nobody brings back items that were used while they were away.
        foreach (var entry in store.sessionPlayersData)
        {
            if (entry.Value != null)
                entry.Value.Inventory = Clone(master);
        }

        var runner = Runner;
        foreach (var entry in store.playersIdLookup)
        {
            var player = entry.Key;
            var data = store.GetPlayerData(player);
            if (data == null)
                continue;

            // Keep every shadow equal to the master so the next index-based
            // packet from that player lines up with what it sees.
            data.Inventory = Clone(master);

            if (player != runner.LocalPlayer)
                server.HandleInitialSync(player, ref data);
        }

        // The host never sends its own dirty flags anywhere, so clear them here:
        // the next change sets them again and shows up in the signature.
        for (var i = 0; i < master.Count; i++)
        {
            var stack = master[i];
            if (stack == null)
                continue;
            stack.IsDirty = false;
            var items = stack.Items;
            if (items == null)
                continue;
            for (var j = 0; j < items.Count; j++)
            {
                if (items[j] != null)
                    items[j].IsDirty = false;
            }
        }

        lastSignature = Signature(master);
    }

    /// <summary>Add the stacks of a player's own (saved) inventory that the master does not have yet.</summary>
    private static bool MergeIfNew(string playerId, SessionPlayerData data)
    {
        if (playerId == null || data == null || merged.Contains(playerId))
            return false;

        merged.Add(playerId);
        var source = data.Inventory;
        var master = Master;
        if (source == null || master == null || source.Count == 0)
            return true;

        var added = 0;
        WithSyncPaused(hostClient, () =>
        {
            for (var i = 0; i < source.Count; i++)
            {
                var stack = source[i];
                if (stack == null || Contains(master, stack))
                    continue;
                master.Add(Copy(stack));
                added++;
            }
        });

        Plugin.Logger.LogInfo($"Merged {added} stack(s) from player {playerId} into the shared inventory.");
        Dirty = true;
        return true;
    }

    private static bool Contains(ObservableList<ItemStack> list, ItemStack stack)
    {
        for (var i = 0; i < list.Count; i++)
        {
            var other = list[i];
            if (other != null && other.UID == stack.UID && other.ItemID == stack.ItemID)
                return true;
        }

        return false;
    }

    private static string PlayerId(PlayerRef player)
    {
        return store.playersIdLookup.TryGetValue(player, out var id) ? id : null;
    }

    /// <summary>Cheap fingerprint of the list: stacks, their sizes and the items in them.</summary>
    private static long Signature(ObservableList<ItemStack> list)
    {
        unchecked
        {
            long hash = list.Count;
            for (var i = 0; i < list.Count; i++)
            {
                var stack = list[i];
                if (stack == null)
                    continue;
                hash = hash * 31 + (long)stack.UID;
                hash = hash * 31 + stack.ItemsCount;
                var items = stack.Items;
                if (items == null)
                    continue;
                for (var j = 0; j < items.Count; j++)
                {
                    var item = items[j];
                    if (item != null)
                        hash = hash * 31 + (long)item.UID + (item.IsDirty ? 1 : 0);
                }
            }

            return hash;
        }
    }

    /// <summary>
    /// Run <paramref name="action"/> while the given sync client ignores list events,
    /// so writes done by the mod are not sent back over the network.
    /// </summary>
    internal static void WithSyncPaused(PlayerInventorySyncClient client, Action action)
    {
        var wasDone = client.initialSyncDone;
        client.initialSyncDone = false;
        Applying = true;
        try
        {
            action();
        }
        finally
        {
            Applying = false;
            client.initialSyncDone = wasDone;
        }
    }

    /// <summary>Make <paramref name="target"/> equal to <paramref name="source"/>, firing list events for the UI.</summary>
    internal static void CopyInto(ObservableList<ItemStack> target, Il2CppList source)
    {
        var count = source.Count;
        var common = Math.Min(target.Count, count);
        for (var i = 0; i < common; i++)
            target[i] = Copy(source[i]);
        while (target.Count > count)
            target.RemoveAt(target.Count - 1);
        for (var i = target.Count; i < count; i++)
            target.Add(Copy(source[i]));
    }

    internal static void CopyInto(ObservableList<ItemStack> target, ObservableList<ItemStack> source)
    {
        var count = source.Count;
        var common = Math.Min(target.Count, count);
        for (var i = 0; i < common; i++)
            target[i] = source[i];
        while (target.Count > count)
            target.RemoveAt(target.Count - 1);
        for (var i = target.Count; i < count; i++)
            target.Add(source[i]);
    }

    private static Il2CppList Clone(ObservableList<ItemStack> source)
    {
        var list = new Il2CppList(source.Count);
        for (var i = 0; i < source.Count; i++)
            list.Add(Copy(source[i]));
        return list;
    }

    /// <summary>Deep copy through the game's own network representation.</summary>
    private static ItemStack Copy(ItemStack stack)
    {
        return new NetworkItemStack(stack).ToNative();
    }

    internal static void Reset()
    {
        store = null;
        server = null;
        hostClient = null;
        Dirty = false;
        merged.Clear();
        lastSignature = 0;
    }
}
