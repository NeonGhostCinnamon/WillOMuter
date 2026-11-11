using Dalamud.Configuration;
using System;

namespace WillOMuter;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 3;

    /// <summary>Whether Will-o-Muter is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Mute when at least this many other players are within <see cref="Radius"/>.</summary>
    public int MuteAtPlayers { get; set; } = 10;

    /// <summary>Unmute once the count drops below MuteAtPlayers minus this (prevents flapping).</summary>
    public int UnmuteMargin { get; set; } = 5;

    /// <summary>Only count players within this many yalms. 0 = no limit.</summary>
    public float Radius { get; set; } = 50f;

    /// <summary>Seconds the area must stay below the unmute level before volume is restored.</summary>
    public float UnmuteDelaySeconds { get; set; } = 3f;

    /// <summary>Old dark/light switch, only read once to move existing setups to <see cref="ThemeChoice"/>.</summary>
    public bool DarkMode { get; set; } = true;

    /// <summary>Dark (default), Light or Dalamud, like Will-o-Patchwisp.</summary>
    public ThemeMode ThemeChoice { get; set; } = ThemeMode.Dark;

    // ---- server info bar (the entry at the top right of the screen; Umbra can show it too)

    /// <summary>Show Will-o-Muter in the server info bar.</summary>
    public bool DtrShown { get; set; } = true;

    /// <summary>Always show it (with the "not muting" text), not only while muting.</summary>
    public bool DtrAlways { get; set; } = false;

    public string DtrMutingText { get; set; } = DefaultMutingText;
    public string DtrIdleText { get; set; } = DefaultIdleText;
    public const string DefaultMutingText = "Muting crowd";
    /// <summary>Blank: when not muting, the entry shows just the number of players.</summary>
    public const string DefaultIdleText = "";

    /// <summary>Show the text at all (off: only the number of players).</summary>
    public bool DtrMutingTextOn { get; set; } = true;
    public bool DtrIdleTextOn { get; set; } = true;

    /// <summary>Text colour as 0xRRGGBB; null = the game's default colour.</summary>
    public uint? DtrMutingRgb { get; set; } = DefaultMutingRgb;
    public uint? DtrIdleRgb { get; set; }
    public const uint DefaultMutingRgb = 0xFF7EB6;

    /// <summary>No longer used: the bar only says whether Will-o-Muter is muting (kept so old settings still load).</summary>
    public bool DtrShowCount { get; set; } = true;

    /// <summary>Old colour choice (before version 3): index into the old colour list; 0 = the game's default colour.</summary>
    public int DtrMutingColor { get; set; } = 1;
    public int DtrIdleColor { get; set; } = 0;

    /// <summary>Left-click turns Will-o-Muter on/off (otherwise it opens the settings). Right-click always opens them.</summary>
    public bool DtrClickToggles { get; set; } = true;

    // Persisted so a crash while muted doesn't leave your ambient volume at 0.
    public bool IsMuted { get; set; } = false;
    public uint SavedVolume { get; set; } = 100;
}

/// <summary>The window's look: the website's dark or light colours, or Dalamud's own style.</summary>
public enum ThemeMode
{
    Dark,
    Light,
    Dalamud,
}
