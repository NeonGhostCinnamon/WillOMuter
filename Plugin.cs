using Dalamud.Game.Config;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.IO;
using System.Numerics;
using WillOMuter.Windows;

namespace WillOMuter;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/willomuter";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    [PluginService] private static IObjectTable ObjectTable { get; set; } = null!;
    [PluginService] private static IGameConfig GameConfig { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;

    private readonly Configuration config;
    private readonly WindowSystem windows = new("WillOMuter");
    private readonly MainWindow configWindow;

    public Configuration Configuration => config;

    /// <summary>Nunito Black at heading size (the website's font), so the title is sharp.</summary>
    public IFontHandle TitleFont { get; }

    private DateTime lastCheck = DateTime.MinValue;
    private DateTime belowSince = DateTime.MinValue;

    /// <summary>Latest count, shown in the config window.</summary>
    public static int LastCount { get; private set; }

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // Recover from a crash/force-quit that happened while muted.
        if (config.IsMuted)
        {
            Restore();
        }

        var fontPath = Path.Combine(PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty, "Nunito-Black.ttf");
        TitleFont = PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            if (File.Exists(fontPath))
                tk.AddFontFromFile(fontPath, new SafeFontConfig { SizePx = 30 });
            else
                tk.AddDalamudDefaultFont(30);
        }));

        configWindow = new MainWindow(this);
        windows.AddWindow(configWindow);

        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi += configWindow.Toggle;

        CommandManager.AddHandler(Command, new CommandInfo((_, _) => configWindow.Toggle())
        {
            HelpMessage = "Open the Will-o-Muter settings.",
        });

        Framework.Update += OnUpdate;
    }

    private void OnUpdate(IFramework framework)
    {
        // Check twice a second, not every frame.
        var now = DateTime.UtcNow;
        if ((now - lastCheck).TotalMilliseconds < 500) return;
        lastCheck = now;

        if (!config.Enabled)
        {
            if (config.IsMuted) Restore();
            return;
        }

        var local = ObjectTable.LocalPlayer;
        if (local == null) return;

        LastCount = CountNearbyPlayers(local);

        var unmuteBelow = Math.Max(1, config.MuteAtPlayers - config.UnmuteMargin);

        if (!config.IsMuted)
        {
            if (LastCount >= config.MuteAtPlayers) Mute();
            return;
        }

        // Currently muted: restore only after the crowd has stayed small for a while.
        if (LastCount < unmuteBelow)
        {
            if (belowSince == DateTime.MinValue) belowSince = now;
            if ((now - belowSince).TotalSeconds >= config.UnmuteDelaySeconds) Restore();
        }
        else
        {
            belowSince = DateTime.MinValue;
        }
    }

    private int CountNearbyPlayers(IPlayerCharacter local)
    {
        var count = 0;
        foreach (var obj in ObjectTable)
        {
            if (obj is not IPlayerCharacter pc) continue;
            if (pc.GameObjectId == local.GameObjectId) continue;
            if (config.Radius > 0 && Vector3.Distance(pc.Position, local.Position) > config.Radius) continue;
            count++;
        }
        return count;
    }

    private void Mute()
    {
        if (!GameConfig.TryGet(SystemConfigOption.SoundEnv, out uint current))
        {
            Log.Warning("Could not read the ambient volume option; not muting.");
            return;
        }

        // Don't overwrite a good saved value with 0 if something else already muted it.
        if (current > 0) config.SavedVolume = current;

        GameConfig.Set(SystemConfigOption.SoundEnv, 0u);
        config.IsMuted = true;
        belowSince = DateTime.MinValue;
        Save();
        Log.Debug($"Muted ambient (crowd of {LastCount}); saved volume {config.SavedVolume}.");
    }

    private void Restore()
    {
        GameConfig.Set(SystemConfigOption.SoundEnv, config.SavedVolume);
        config.IsMuted = false;
        belowSince = DateTime.MinValue;
        Save();
        Log.Debug($"Restored ambient volume to {config.SavedVolume}.");
    }

    public void Save() => PluginInterface.SavePluginConfig(config);

    public void Dispose()
    {
        Framework.Update -= OnUpdate;
        CommandManager.RemoveHandler(Command);

        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi -= configWindow.Toggle;
        windows.RemoveAllWindows();
        TitleFont.Dispose();

        // Never leave the user muted when the plugin goes away.
        if (config.IsMuted) Restore();
    }
}
