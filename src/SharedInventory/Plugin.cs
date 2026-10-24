using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;

namespace SharedInventory;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "querencia.cms2027.sharedinventory";
    public const string Name = "Shared Inventory";
    public const string Version = "0.2.0";

    internal static ManualLogSource Logger;

    public override void Load()
    {
        Logger = Log;

        ClassInjector.RegisterTypeInIl2Cpp<SharedInventoryDriver>();
        AddComponent<SharedInventoryDriver>();

        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

        Logger.LogInfo($"{Name} {Version} loaded. Host's inventory is shared with every player.");
    }
}
