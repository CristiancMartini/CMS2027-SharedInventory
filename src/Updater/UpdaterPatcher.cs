using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using BepInEx;
using BepInEx.Preloader.Core.Patching;

namespace SharedInventory.Updater;

/// <summary>
/// Runs in the BepInEx preloader, before any plugin is loaded: checks the latest
/// GitHub release and, if it is newer, replaces the plugin dll so this launch already
/// uses it. Any failure (offline, GitHub down, ...) just keeps the installed version.
/// </summary>
[PatcherPluginInfo("querencia.cms2027.sharedinventory.updater", "Shared Inventory Updater", "1.0.0")]
public class UpdaterPatcher : BasePatcher
{
    private const string Repo = "CristiancMartini/CMS2027-SharedInventory";
    private const string AssetName = "SharedInventory.dll";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public override void Initialize()
    {
        try
        {
            Update();
        }
        catch (Exception e)
        {
            Log.LogWarning($"Update check failed, keeping installed version: {e.Message}");
        }
    }

    private void Update()
    {
        var pluginPath = Path.Combine(Paths.PluginPath, "SharedInventory", AssetName);
        var installed = File.Exists(pluginPath) ? AssemblyName.GetAssemblyName(pluginPath).Version : new Version(0, 0);

        using var http = new HttpClient { Timeout = Timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CMS2027-SharedInventory-Updater");

        // /releases (not /releases/latest) so pre-releases count too.
        var json = http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases?per_page=10").GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(json);

        Version best = null;
        string url = null;
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean())
                continue;
            if (!Version.TryParse(release.GetProperty("tag_name").GetString()?.TrimStart('v', 'V'), out var version))
                continue;
            if (best != null && version <= best)
                continue;

            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() == AssetName)
                {
                    best = version;
                    url = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }
        }

        if (best == null || Normalize(best) <= Normalize(installed))
        {
            Log.LogInfo($"Shared Inventory is up to date ({installed}).");
            return;
        }

        if (url == null || !url.StartsWith($"https://github.com/{Repo}/releases/download/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected download URL: " + url);

        Log.LogMessage($"Updating Shared Inventory {installed} -> {best}...");
        var bytes = http.GetByteArrayAsync(url).GetAwaiter().GetResult();

        Directory.CreateDirectory(Path.GetDirectoryName(pluginPath)!);
        var temp = pluginPath + ".new";
        File.WriteAllBytes(temp, bytes);
        File.Copy(temp, pluginPath, true);
        File.Delete(temp);

        Log.LogMessage($"Shared Inventory updated to {best}.");
    }

    private static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
