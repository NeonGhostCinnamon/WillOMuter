using Dalamud.Configuration;
using System;

namespace WillOMuter;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

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

    /// <summary>Dark (default) or light theme, like Will-o-Patchwisp.</summary>
    public bool DarkMode { get; set; } = true;

    // Persisted so a crash while muted doesn't leave your ambient volume at 0.
    public bool IsMuted { get; set; } = false;
    public uint SavedVolume { get; set; } = 100;
}
