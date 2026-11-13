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
    /// <summary>"WillOMuter", or "WillOMuterTest" for a test build that runs next to the normal one.</summary>
    public static readonly string Id = typeof(Plugin).Assembly.GetName().Name ?? "WillOMuter";
    public static bool IsTestBuild => Id != "WillOMuter";
    private static readonly string Command = IsTestBuild ? "/willomutertest" : "/willomuter";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    [PluginService] private static IObjectTable ObjectTable { get; set; } = null!;
    [PluginService] private static IGameConfig GameConfig { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;
    [PluginService] private static IDtrBar DtrBar { get; set; } = null!;
    [PluginService] internal static IDataManager Data { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] private static IChatGui Chat { get; set; } = null!;
    [PluginService] private static IContextMenu ContextMenu { get; set; } = null!;
    [PluginService] internal static ITargetManager Targets { get; private set; } = null!;
    [PluginService] private static IGameInteropProvider Interop { get; set; } = null!;
    [PluginService] internal static ITextureProvider Textures { get; private set; } = null!;

    /// <summary>Muting the sounds of specific people.</summary>
    public Individual Individual { get; }
    private readonly ChatTwoMenu chatTwoMenu;

    private IDtrBarEntry? dtr;
    private string lastDtrKey = string.Empty;

    /// <summary>What an old colour choice (before version 3) becomes: the matching quick colour, or the game's default.</summary>
    private static uint? OldColor(int index) => index > 0 && index <= QuickColors.Length ? QuickColors[index - 1] : null;

    private readonly Configuration config;
    private readonly WindowSystem windows = new(Id);
    private readonly MainWindow configWindow;

    public Configuration Configuration => config;

    /// <summary>Nunito Black at heading size (the website's font), so the title is sharp.</summary>
    public IFontHandle TitleFont { get; }

    /// <summary>The Discord logo for the footer button (null when the file is missing).</summary>
    public Dalamud.Interface.Textures.ISharedImmediateTexture? DiscordLogo { get; private set; }

    private void LoadDiscordLogo(ITextureProvider textures)
    {
        try
        {
            DiscordLogo = textures.GetFromFile(Path.Combine(PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty, "images", "discord.png"));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Couldn't load the Discord logo; the footer button shows without it.");
        }
    }


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

        LoadDiscordLogo(Textures);
        var fontPath = Path.Combine(PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty, "Nunito-Black.ttf");
        TitleFont = PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            if (File.Exists(fontPath))
                tk.AddFontFromFile(fontPath, new SafeFontConfig { SizePx = 30 });
            else
                tk.AddDalamudDefaultFont(30);
        }));

        // v3: the server info bar's colour became any RGB colour (it was a choice from a short list).
        if (config.Version < 3)
        {
            config.DtrMutingRgb = OldColor(config.DtrMutingColor);
            config.DtrIdleRgb = OldColor(config.DtrIdleColor);
            config.Version = 3;
            PluginInterface.SavePluginConfig(config);
        }

        // v4: "friends and Free Company members" became two choices; keep both as they were.
        if (config.Version < 4)
        {
            config.RaceSkipFreeCompany = config.RaceSkipFriends;
            config.Version = 4;
            PluginInterface.SavePluginConfig(config);
        }
        try
        {
            dtr = DtrBar.Get(IsTestBuild ? "Will-o-Muter (Test)" : "Will-o-Muter");
            dtr.OnClick = OnDtrClick;
            UpdateDtr();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Couldn't add Will-o-Muter to the server info bar.");
        }

        Individual = new Individual(config, Save, ObjectTable, Chat, ContextMenu, Interop, Log);
        chatTwoMenu = new ChatTwoMenu(PluginInterface, Individual, Log);
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
        Individual.Update();

        // Check twice a second, not every frame.
        var now = DateTime.UtcNow;
        if ((now - lastCheck).TotalMilliseconds < 500) return;
        lastCheck = now;
        UpdateDtr();

        if (!config.Enabled || !config.AllEnabled)
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

    /// <summary>Quick colours in the colour picker (and what the old colour choices become).</summary>
    public static readonly uint[] QuickColors =
    {
        0xFF7EB6, // pink
        0xB080FF, // purple
        0x73BFFF, // blue
        0x78DB96, // green
        0xF2C46B, // gold
        0xFF7070, // red
    };

    /// <summary>The game's default text colour in the bar (for the preview and the "default" dot).</summary>
    public static readonly Vector4 GameDefaultColor = new(0.91f, 0.90f, 0.87f, 1f);

    public static Vector4 ToVector(uint? rgb) => rgb is { } c
        ? new Vector4(((c >> 16) & 0xFF) / 255f, ((c >> 8) & 0xFF) / 255f, (c & 0xFF) / 255f, 1f)
        : GameDefaultColor;

    public static uint FromVector(Vector4 v)
        => ((uint)Math.Round(Math.Clamp(v.X, 0, 1) * 255) << 16) | ((uint)Math.Round(Math.Clamp(v.Y, 0, 1) * 255) << 8) | (uint)Math.Round(Math.Clamp(v.Z, 0, 1) * 255);

    /// <summary>The entry's text and colour for the muting or the not-muting state ("" = nothing to show).</summary>
    public (string Text, uint? Rgb) DtrLook(bool muted)
    {
        var on = muted ? config.DtrMutingTextOn : config.DtrIdleTextOn;
        var label = on ? (muted ? config.DtrMutingText : config.DtrIdleText).Trim() : string.Empty;
        var text = label;
        if (!config.Enabled)
            text = text.Length > 0 ? $"{text} (off)" : "Crowd muting (off)";
        return (text, muted ? config.DtrMutingRgb : config.DtrIdleRgb);
    }

    private void UpdateDtr()
    {
        if (dtr == null)
            return;

        var (text, rgb) = DtrLook(config.IsMuted);
        var shown = config.AllEnabled && config.DtrShown && (config.DtrAlways || config.IsMuted) && text.Length > 0;
        var key = $"{shown}|{text}|{rgb}|{config.Enabled}|{config.IsMuted}|{config.DtrClickToggles}|{LastCount}|{config.MuteAtPlayers}|{config.UnmuteMargin}";
        if (key == lastDtrKey)
            return;
        lastDtrKey = key;

        dtr.Shown = shown;
        if (!shown)
            return;

        dtr.Text = Colored(text, rgb);

        var unmuteBelow = Math.Max(1, config.MuteAtPlayers - config.UnmuteMargin);
        var state = !config.Enabled ? "crowd muting is off" : config.IsMuted ? "ambient sounds muted" : "not muting";
        var click = config.DtrClickToggles
            ? $"Click: turn crowd muting {(config.Enabled ? "off" : "on")} · Right-click: settings"
            : "Click: settings";
        dtr.Tooltip = new SeStringBuilder()
            .AddText($"Will-o-Muter: {state}\n{LastCount} players nearby (mutes at {config.MuteAtPlayers}, unmutes below {unmuteBelow})\n{click}")
            .Build();
    }

    /// <summary>Text in any colour (the game's own colour list only has a few dozen; this takes any RGB).</summary>
    private static unsafe SeString Colored(string text, uint? rgb)
    {
        if (rgb == null)
            return new SeStringBuilder().AddText(text).Build();
        var b = new Lumina.Text.SeStringBuilder();
        b.PushColorRgba(ToVector(rgb));
        b.Append(text);
        b.PopColor();
        var bytes = b.ToArray();
        fixed (byte* p = bytes)
            return SeString.Parse(p, bytes.Length);
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
        chatTwoMenu.Dispose();
        Individual.Dispose();
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
