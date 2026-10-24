using System;
using UnityEngine;

namespace SharedInventory;

/// <summary>
/// Runs on every machine; only does work on the host. Coalesces inventory changes
/// and pushes the shared inventory to clients at most every <see cref="BroadcastInterval"/>.
/// </summary>
public class SharedInventoryDriver : MonoBehaviour
{
    private const float BroadcastInterval = 0.25f;
    private const float PollInterval = 0.25f;

    private float nextBroadcast;
    private float nextPoll;

    public SharedInventoryDriver(IntPtr ptr) : base(ptr) { }

    private void Update()
    {
        if (Time.unscaledTime >= nextPoll)
        {
            nextPoll = Time.unscaledTime + PollInterval;
            try
            {
                SharedInventory.Poll();
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"Poll failed: {e}");
            }
        }

        if (!SharedInventory.Dirty || Time.unscaledTime < nextBroadcast)
            return;

        nextBroadcast = Time.unscaledTime + BroadcastInterval;
        SharedInventory.Dirty = false;

        try
        {
            SharedInventory.Broadcast();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"Broadcast failed: {e}");
        }
    }
}
