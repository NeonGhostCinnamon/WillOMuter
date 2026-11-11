using Dalamud.Game.Config;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Lumina.Excel.Sheets;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    [PluginService] private static IDtrBar DtrBar { get; set; } = null!;
    [PluginService] private static IDataManager Data { get; set; } = null!;

    private IDtrBarEntry? dtr;
    private string lastDtrKey = string.Empty;

    /// <summary>
    /// The colours the server info bar entry can have: the game's default, then the game colours closest to
    /// pink, purple, blue, green, gold and red (the bar can only use the game's own colour list).
    /// </summary>
    public static (ushort Row, Vector4 Rgb)[] DtrColors { get; private set; } = Array.Empty<(ushort, Vector4)>();

    private readonly Configuration config;
    private readonly WindowSystem windows = new("WillOMuter");
    private readonly MainWindow configWindow;

    public Configuration Configuration => config;

    /// <summary>Nunito Black at heading size (the website's font), so the title is sharp.</summary>
    public IFontHandle TitleFont { get; }

    private DateTime lastCheck = DateTime.MinValue;
    private DateTime belowSince = DateTime.MinValue;

    // The player turned the ambient sounds back up while muted: leave them alone until this crowd is gone.
    private bool playerOverride;

    /// <summary>Latest count, shown in the config window.</summary>
    public static int LastCount { get; private set; }

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // v2: the dark/light switch became a theme choice (Dark, Light or Dalamud).
        if (config.Version < 2)
        {
            config.ThemeChoice = config.DarkMode ? ThemeMode.Dark : ThemeMode.Light;
            config.Version = 2;
            PluginInterface.SavePluginConfig(config);
        }

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

        LoadDtrColors();
        try
        {
            dtr = DtrBar.Get("Will-o-Muter");
            dtr.OnClick = OnDtrClick;
            UpdateDtr();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Couldn't add Will-o-Muter to the server info bar.");
        }

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
        UpdateDtr();

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
            if (playerOverride && LastCount < unmuteBelow)
                playerOverride = false;
            if (!playerOverride && LastCount >= config.MuteAtPlayers) Mute();
            return;
        }

        // Muted, but the player set a volume in the game's settings: keep theirs instead of the saved one.
        if (GameConfig.TryGet(SystemConfigOption.SoundEnv, out uint playerVolume) && playerVolume != 0)
        {
            config.SavedVolume = playerVolume;
            config.IsMuted = false;
            belowSince = DateTime.MinValue;
            playerOverride = true;
            Save();
            Log.Debug($"Ambient volume changed to {playerVolume} while muted; not muting again until the crowd is gone.");
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

        // Ambient sounds are already at 0 (turned down by the player): nothing to mute, and nothing to
        // "restore" later. Muting here would bring them back at the old saved volume once the crowd leaves.
        if (current == 0)
            return;

        config.SavedVolume = current;

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

    public void Save()
    {
        PluginInterface.SavePluginConfig(config);
        UpdateDtr();
    }

    // ---------------------------------------------------------------- server info bar

    private static readonly Vector4[] ColorTargets =
    {
        new(1.00f, 0.49f, 0.71f, 1f), // pink
        new(0.69f, 0.50f, 1.00f, 1f), // purple
        new(0.45f, 0.75f, 1.00f, 1f), // blue
        new(0.47f, 0.86f, 0.59f, 1f), // green
        new(0.95f, 0.77f, 0.42f, 1f), // gold
        new(1.00f, 0.44f, 0.44f, 1f), // red
    };

    private static void LoadDtrColors()
    {
        var list = new List<(ushort, Vector4)> { (0, new Vector4(0.91f, 0.90f, 0.87f, 1f)) };
        try
        {
            var rows = Data.GetExcelSheet<UIColor>()
                .Where(r => r.Dark != 0 && r.RowId <= ushort.MaxValue)
                .Select(r => ((ushort)r.RowId, new Vector4(((r.Dark >> 24) & 0xFF) / 255f, ((r.Dark >> 16) & 0xFF) / 255f, ((r.Dark >> 8) & 0xFF) / 255f, 1f)))
                .ToList();
            foreach (var target in ColorTargets)
            {
                var best = rows.OrderBy(r => Vector3.DistanceSquared(new Vector3(r.Item2.X, r.Item2.Y, r.Item2.Z), new Vector3(target.X, target.Y, target.Z))).FirstOrDefault();
                if (best.Item1 != 0)
                    list.Add(best);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Couldn't read the game's colours.");
        }
        DtrColors = list.ToArray();
    }

    /// <summary>The entry's text as it looks right now (also used for the preview in the window).</summary>
    public (string Text, int Color) DtrLook()
    {
        var muted = config.IsMuted;
        var text = muted ? config.DtrMutingText : config.DtrIdleText;
        if (string.IsNullOrWhiteSpace(text))
            text = muted ? Configuration.DefaultMutingText : Configuration.DefaultIdleText;
        if (config.DtrShowCount && config.Enabled)
            text += $" · {LastCount}";
        if (!config.Enabled)
            text += " (off)";
        return (text, muted ? config.DtrMutingColor : config.DtrIdleColor);
    }

    private void UpdateDtr()
    {
        if (dtr == null)
            return;

        var shown = config.DtrShown && (config.DtrAlways || config.IsMuted);
        var (text, color) = DtrLook();
        var key = $"{shown}|{text}|{color}|{config.Enabled}|{config.IsMuted}|{config.DtrClickToggles}|{LastCount}|{config.MuteAtPlayers}|{config.UnmuteMargin}";
        if (key == lastDtrKey)
            return;
        lastDtrKey = key;

        dtr.Shown = shown;
        if (!shown)
            return;

        var row = color > 0 && color < DtrColors.Length ? DtrColors[color].Row : (ushort)0;
        dtr.Text = row != 0
            ? new SeStringBuilder().AddUiForeground(text, row).Build()
            : new SeStringBuilder().AddText(text).Build();

        var unmuteBelow = Math.Max(1, config.MuteAtPlayers - config.UnmuteMargin);
        var state = !config.Enabled ? "turned off" : config.IsMuted ? "ambient sounds muted" : "not muting";
        var click = config.DtrClickToggles
            ? $"Click: turn Will-o-Muter {(config.Enabled ? "off" : "on")} · Right-click: settings"
            : "Click: settings";
        dtr.Tooltip = new SeStringBuilder()
            .AddText($"Will-o-Muter: {state}\n{LastCount} players nearby (mutes at {config.MuteAtPlayers}, unmutes below {unmuteBelow})\n{click}")
            .Build();
    }

    private void OnDtrClick(DtrInteractionEvent e)
    {
        if (e.ClickType == MouseClickType.Left && config.DtrClickToggles)
        {
            config.Enabled = !config.Enabled;
            Save();
        }
        else
        {
            configWindow.IsOpen = true;
        }
    }

    public void Dispose()
    {
        Framework.Update -= OnUpdate;
        CommandManager.RemoveHandler(Command);
        dtr?.Remove();

        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi -= configWindow.Toggle;
        windows.RemoveAllWindows();
        TitleFont.Dispose();

        // Never leave the user muted when the plugin goes away.
        if (config.IsMuted) Restore();
    }
}
