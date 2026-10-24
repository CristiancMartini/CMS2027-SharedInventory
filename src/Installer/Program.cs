using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Installer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private static readonly string[] GameFolderNames =
    {
        "Car Mechanic Simulator 2027 Demo",
        "Car Mechanic Simulator 2027",
    };

    private const string PluginFolder = @"BepInEx\plugins\SharedInventory";
    private const string PluginFile = "SharedInventory.dll";
    private const string UpdaterFolder = @"BepInEx\patchers\SharedInventory";
    private const string UpdaterFile = "SharedInventory.Updater.dll";

    private static readonly string[] BepInExRootFiles = { "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt" };

    private readonly TextBox pathBox;
    private readonly Label statusLabel;

    public MainForm()
    {
        Text = "CMS 2027 - Inventário Compartilhado";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 230);
        Font = new Font("Segoe UI", 9.5f);

        var title = new Label
        {
            Text = "Inventário compartilhado no multiplayer",
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(16, 14),
        };

        var info = new Label
        {
            Text = "Todos os jogadores precisam instalar. O inventário do host vira o inventário de todos.",
            AutoSize = true,
            Location = new Point(18, 46),
        };

        var pathLabel = new Label { Text = "Pasta do jogo:", AutoSize = true, Location = new Point(18, 80) };
        pathBox = new TextBox { Location = new Point(20, 100), Width = 430 };
        var browse = new Button { Text = "Procurar...", Location = new Point(458, 98), Width = 84 };
        browse.Click += (_, _) => Browse();

        var install = new Button { Text = "Instalar", Location = new Point(20, 140), Size = new Size(160, 36), Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
        install.Click += (_, _) => Run(Install);

        var uninstall = new Button { Text = "Desinstalar", Location = new Point(190, 140), Size = new Size(120, 36) };
        uninstall.Click += (_, _) => Run(Uninstall);

        statusLabel = new Label { AutoSize = false, Location = new Point(18, 186), Size = new Size(524, 36) };

        Controls.AddRange(new Control[] { title, info, pathLabel, pathBox, browse, install, uninstall, statusLabel });

        pathBox.Text = FindGameFolder() ?? "";
        SetStatus(pathBox.Text.Length > 0 ? "Jogo encontrado. Clique em Instalar." : "Jogo não encontrado. Clique em Procurar e escolha a pasta do jogo.");
        AcceptButton = install;
    }

    private void SetStatus(string text) => statusLabel.Text = text;

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog { Description = "Escolha a pasta do Car Mechanic Simulator 2027" };
        if (Directory.Exists(pathBox.Text))
            dialog.SelectedPath = pathBox.Text;
        if (dialog.ShowDialog(this) == DialogResult.OK)
            pathBox.Text = dialog.SelectedPath;
    }

    private void Run(Action<string> action)
    {
        var gameDir = pathBox.Text.Trim();
        if (FindGameExe(gameDir) == null)
        {
            MessageBox.Show(this, "Essa pasta não tem o jogo. Escolha a pasta onde está o \"Car Mechanic Simulator 2027 ... .exe\".", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (IsGameRunning())
        {
            MessageBox.Show(this, "Feche o jogo antes de continuar.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            UseWaitCursor = true;
            action(gameDir);
        }
        catch (Exception e)
        {
            MessageBox.Show(this, "Deu erro:\n\n" + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Erro: " + e.Message);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void Install(string gameDir)
    {
        SetStatus("Instalando...");
        Refresh();

        using (var zipStream = Resource("BepInEx.zip"))
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Read))
        {
            foreach (var entry in zip.Entries)
            {
                var target = Path.GetFullPath(Path.Combine(gameDir, entry.FullName));
                if (!target.StartsWith(Path.GetFullPath(gameDir), StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, true);
            }
        }

        // Hide BepInEx's console window on fresh installs (BepInEx fills in the other settings).
        var config = Path.Combine(gameDir, @"BepInEx\config\BepInEx.cfg");
        if (!File.Exists(config))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(config)!);
            File.WriteAllText(config, "[Logging.Console]\r\nEnabled = false\r\n");
        }

        WriteResource(PluginFile, Path.Combine(gameDir, PluginFolder));
        WriteResource(UpdaterFile, Path.Combine(gameDir, UpdaterFolder));

        SetStatus("Instalado! Na primeira vez o jogo demora alguns minutos pra abrir. É normal.");
        MessageBox.Show(this,
            "Pronto, instalado!\n\n" +
            "A PRIMEIRA vez que você abrir o jogo ele vai demorar alguns minutos (pode parecer travado). É normal, só esperar.\n\n" +
            "Lembre: todos os jogadores precisam instalar.",
            Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void Uninstall(string gameDir)
    {
        foreach (var folder in new[] { PluginFolder, UpdaterFolder })
        {
            var dir = Path.Combine(gameDir, folder);
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }

        var removeLoader = MessageBox.Show(this,
            "Mod removido.\n\nRemover também o BepInEx (o carregador de mods)? Se você usa outros mods, clique em Não.",
            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (removeLoader == DialogResult.Yes)
        {
            foreach (var name in BepInExRootFiles)
            {
                var path = Path.Combine(gameDir, name);
                if (File.Exists(path))
                    File.Delete(path);
            }

            var bepInEx = Path.Combine(gameDir, "BepInEx");
            if (Directory.Exists(bepInEx))
                Directory.Delete(bepInEx, true);
        }

        SetStatus("Desinstalado.");
    }

    private static void WriteResource(string name, string dir)
    {
        Directory.CreateDirectory(dir);
        using var resource = Resource(name);
        using var file = File.Create(Path.Combine(dir, name));
        resource.CopyTo(file);
    }

    private static Stream Resource(string name)
    {
        return Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
               ?? throw new InvalidOperationException("Arquivo interno ausente: " + name);
    }

    private static bool IsGameRunning()
    {
        return Process.GetProcesses().Any(p =>
        {
            try { return p.ProcessName.StartsWith("Car Mechanic Simulator 2027", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        });
    }

    private static string FindGameExe(string dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return null;
        return Directory.GetFiles(dir, "Car Mechanic Simulator 2027*.exe").FirstOrDefault();
    }

    private static string FindGameFolder()
    {
        foreach (var library in SteamLibraries())
        {
            foreach (var name in GameFolderNames)
            {
                var dir = Path.Combine(library, "steamapps", "common", name);
                if (FindGameExe(dir) != null)
                    return dir;
            }
        }

        return null;
    }

    private static IEnumerable<string> SteamLibraries()
    {
        var roots = new List<string>();
        foreach (var (hive, key, value) in new[]
                 {
                     (Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                     (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                     (Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
                 })
        {
            try
            {
                using var k = hive.OpenSubKey(key);
                if (k?.GetValue(value) is string path && Directory.Exists(path))
                    roots.Add(Path.GetFullPath(path.Replace('/', '\\')));
            }
            catch
            {
                // ignore unreadable keys
            }
        }

        var libraries = new List<string>(roots);
        foreach (var root in roots)
        {
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
                continue;

            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }

        return libraries.Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
