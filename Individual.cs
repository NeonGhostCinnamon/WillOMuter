using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace WillOMuter;

/// <summary>What happens to one person's sounds.</summary>
public enum PersonSounds
{
    /// <summary>Their sounds don't play.</summary>
    Muted,

    /// <summary>Not used anymore (an early test); treated like <see cref="Muted"/>.</summary>
    LikeOthers,

    /// <summary>Their sounds are left alone (only the other options apply).</summary>
    Normal,
}

[Serializable]
public class MutedPerson
{
    public string Name { get; set; } = string.Empty;
    public uint WorldId { get; set; }
    public string WorldName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public PersonSounds Sounds { get; set; } = PersonSounds.Muted;

    /// <summary>Mute their emote voices, battle shouts and other character sounds.</summary>
    public bool MuteVoices { get; set; } = true;

    /// <summary>Mute their footsteps (not working yet: coming in a later test build).</summary>
    public bool MuteFootsteps { get; set; } = true;

    /// <summary>Mute their skill, action and animation sounds (not working yet: coming in a later test build).</summary>
    public bool MuteSkills { get; set; } = true;

    /// <summary>Also hide their emote messages in chat ("Kai waves to you.").</summary>
    public bool HideEmoteMessages { get; set; }

    public string Key => $"{Name}@{WorldId}";

    /// <summary>"[Lich] Mocha Parfait".</summary>
    public string Display => $"[{WorldName}] {Name}";
}

/// <summary>Someone on the WhiteList: never muted by the race and gender rules.</summary>
[Serializable]
public class ListedPerson
{
    public string Name { get; set; } = string.Empty;
    public uint WorldId { get; set; }
    public string WorldName { get; set; } = string.Empty;
    public string Display => $"[{WorldName}] {Name}";
}

/// <summary>Mute everyone of a race and/or gender.</summary>
[Serializable]
public class RaceRule
{
    public bool Enabled { get; set; } = true;

    /// <summary>Race row id (1 Hyur … 8 Viera), 0 = any race.</summary>
    public byte Race { get; set; } = 3;

    /// <summary>0 = male, 1 = female, -1 = any gender.</summary>
    public int Gender { get; set; } = -1;

    public bool MuteVoices { get; set; } = true;
    public bool MuteFootsteps { get; set; } = true;
    public bool MuteSkills { get; set; } = true;
}

/// <summary>
/// Mutes the sounds of specific people. The game asks a character's effect container to load each of its
/// character sounds (emote voices, battle shouts and the like) through one function; Will-o-Muter steps in front of
/// it and skips the sound when the character belongs to someone on the list. The function comes from the
/// FFXIVClientStructs library that ships with Dalamud, so it's kept up to date with game patches.
/// Footsteps and ability/animation sounds use other functions and aren't covered yet.
/// </summary>
public sealed unsafe class Individual : IDisposable
{
    private delegate nint LoadCharacterSoundDelegate(
        VfxContainer* self, int soundNumber, int unk2, nint gameObject, ulong autoRelease, int weaponDataIndex, int unk6, ulong unk7);

    private readonly Configuration config;
    private readonly Action save;
    private readonly IObjectTable objects;
    private readonly IChatGui chat;
    private readonly IContextMenu contextMenu;
    private readonly IPluginLog log;
    private readonly Hook<LoadCharacterSoundDelegate>? voiceHook;

    // Addresses of the characters (and their mounts, minions and accessories) whose sounds are muted right now.
    // Rebuilt a few times a second on the game's thread; the sound function runs on that thread too.
    private HashSet<nint> mutedObjects = new();
    private nint localPlayer;
    private DateTime lastScan = DateTime.MinValue;

    /// <summary>Whether muting voices works (false when the game function couldn't be found, e.g. right after a patch).</summary>
    public bool VoiceMutingWorks => voiceHook is { IsEnabled: true };

    /// <summary>How many sounds were skipped since the plugin started (shown in the test build).</summary>
    public int SkippedSounds { get; private set; }

    public Individual(Configuration config, Action save, IObjectTable objects, IChatGui chat, IContextMenu contextMenu,
        IGameInteropProvider interop, IPluginLog log)
    {
        this.config = config;
        this.save = save;
        this.objects = objects;
        this.chat = chat;
        this.contextMenu = contextMenu;
        this.log = log;
        chat.ChatMessage += OnChat;
        contextMenu.OnMenuOpened += OnMenuOpened;

        // Earlier test builds: "Not muted" became "Emotes and voices" switched off.
        foreach (var p in config.MutedPeople.Where(p => p.Sounds == PersonSounds.Normal))
        {
            p.MuteVoices = false;
            p.Sounds = PersonSounds.Muted;
        }

        // If the function can't be found (a game patch moved it and Dalamud isn't updated yet), muting is simply off.
        try
        {
            voiceHook = interop.HookFromAddress<LoadCharacterSoundDelegate>(
                (nint)VfxContainer.MemberFunctionPointers.LoadCharacterSound, LoadCharacterSoundDetour);
            voiceHook.Enable();
        }
        catch (Exception ex)
        {
            voiceHook?.Dispose();
            voiceHook = null;
            log.Error(ex, "Couldn't set up muting of individual players' voices; that part is turned off.");
        }
    }

    public void Dispose()
    {
        chat.ChatMessage -= OnChat;
        contextMenu.OnMenuOpened -= OnMenuOpened;
        voiceHook?.Dispose();
    }

    public MutedPerson? Find(string name, uint worldId)
        => config.MutedPeople.FirstOrDefault(p => p.WorldId == worldId && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public ListedPerson? FindWhite(string name, uint worldId)
        => config.WhiteList.FirstOrDefault(p => p.WorldId == worldId && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Add to the MuteList (false when they're already on it).</summary>
    public bool Add(string name, uint worldId, string worldName)
    {
        if (string.IsNullOrWhiteSpace(name) || Find(name, worldId) != null)
            return false;
        config.MutedPeople.Add(new MutedPerson { Name = name.Trim(), WorldId = worldId, WorldName = worldName });
        save();
        lastScan = DateTime.MinValue;
        return true;
    }

    public void Remove(MutedPerson p)
    {
        config.MutedPeople.Remove(p);
        save();
        lastScan = DateTime.MinValue;
    }

    /// <summary>Add to the WhiteList (false when they're already on it).</summary>
    public bool AddWhite(string name, uint worldId, string worldName)
    {
        if (string.IsNullOrWhiteSpace(name) || FindWhite(name, worldId) != null)
            return false;
        config.WhiteList.Add(new ListedPerson { Name = name.Trim(), WorldId = worldId, WorldName = worldName });
        save();
        lastScan = DateTime.MinValue;
        return true;
    }

    public void RemoveWhite(ListedPerson p)
    {
        config.WhiteList.Remove(p);
        save();
        lastScan = DateTime.MinValue;
    }

    /// <summary>Settings changed (rules, switches): look again right away.</summary>
    public void Refresh() => lastScan = DateTime.MinValue;

    // ---------------------------------------------------------------- worlds, export and import

    private static List<(uint Id, string Name)>? worlds;

    /// <summary>Every public world (server), by name.</summary>
    public static List<(uint Id, string Name)> Worlds()
        => worlds ??= Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.World>()
            .Where(w => w.IsPublic && !string.IsNullOrWhiteSpace(w.Name.ExtractText()))
            .Select(w => (w.RowId, w.Name.ExtractText()))
            .OrderBy(w => w.Item2)
            .ToList();

    /// <summary>One person per line: "[Server] First Last".</summary>
    public static string Export(IEnumerable<(string Name, string WorldName)> people)
        => string.Join("\n", people.Select(p => $"[{p.WorldName}] {p.Name}"));

    private static readonly System.Text.RegularExpressions.Regex LineRegex =
        new(@"^\s*\[\s*([^\]]+?)\s*\]\s*([A-Za-z'\-]+)\s+([A-Za-z'\-]+)\s*$");

    /// <summary>Reads "[Server] First Last" lines. Returns the people found and, per skipped line, why.</summary>
    public static (List<(string Name, uint WorldId, string WorldName)> People, List<string> Skipped) Parse(string text)
    {
        var people = new List<(string, uint, string)>();
        var skipped = new List<string>();
        var lineNo = 0;
        foreach (var raw in text.Replace("\r", string.Empty).Split('\n'))
        {
            lineNo++;
            if (raw.Trim().Length == 0)
                continue;
            var m = LineRegex.Match(raw);
            if (!m.Success)
            {
                skipped.Add(raw.TrimStart().StartsWith('[') ? $"line {lineNo}: not \"[Server] First Last\"" : $"line {lineNo}: no server");
                continue;
            }
            var world = Worlds().FirstOrDefault(w => w.Name.Equals(m.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
            if (world.Id == 0)
            {
                skipped.Add($"line {lineNo}: unknown server \"{m.Groups[1].Value}\"");
                continue;
            }
            var name = $"{Cap(m.Groups[2].Value)} {Cap(m.Groups[3].Value)}";
            if (!people.Any(p => p.Item2 == world.Id && p.Item1.Equals(name, StringComparison.OrdinalIgnoreCase)))
                people.Add((name, world.Id, world.Name));
        }
        return (people, skipped);
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>On the game's thread: a few times a second, work out whose voices are muted.</summary>
    public void Update()
    {
        localPlayer = objects.LocalPlayer?.Address ?? 0;
        var listOn = config.AllEnabled && config.IndividualEnabled && config.MutedPeople.Count > 0;
        var rulesOn = config.AllEnabled && config.RaceRulesEnabled && config.RaceRules.Any(r => r.Enabled && r.MuteVoices);
        if (!listOn && !rulesOn)
        {
            if (mutedObjects.Count > 0) mutedObjects = new HashSet<nint>();
            return;
        }

        var now = DateTime.UtcNow;
        if ((now - lastScan).TotalMilliseconds < 250)
            return;
        lastScan = now;

        var local = objects.LocalPlayer;
        var myTag = local?.CompanyTag.TextValue ?? string.Empty;
        var muted = new HashSet<nint>();
        foreach (var obj in objects)
        {
            if (obj is not IPlayerCharacter pc || pc.Address == IntPtr.Zero)
                continue;
            var name = pc.Name.TextValue;
            var world = pc.HomeWorld.RowId;

            var voices = false;
            if (listOn && Find(name, world) is { Enabled: true, MuteVoices: true })
                voices = true;

            // Race and gender rules: never for yourself, WhiteList people, and (when chosen) party, friends and FC.
            if (!voices && rulesOn && pc.Address != local?.Address && FindWhite(name, world) == null)
            {
                var flags = pc.StatusFlags;
                var skip = (config.RaceSkipParty && (flags.HasFlag(StatusFlags.PartyMember) || flags.HasFlag(StatusFlags.AllianceMember)))
                           || (config.RaceSkipFriends && (flags.HasFlag(StatusFlags.Friend)
                               || (myTag.Length > 0 && pc.CompanyTag.TextValue == myTag)));
                if (!skip)
                {
                    var c0 = (Character*)pc.Address;
                    var race = c0->DrawData.CustomizeData.Data[0];
                    var gender = c0->DrawData.CustomizeData.Data[1];
                    voices = config.RaceRules.Any(r => r.Enabled && r.MuteVoices
                                                        && (r.Race == 0 || r.Race == race)
                                                        && (r.Gender < 0 || r.Gender == gender));
                }
            }
            if (!voices)
                continue;

            // The person, plus their mount, minion and fashion accessory (separate objects with their own sounds).
            var c = (Character*)pc.Address;
            muted.Add(pc.Address);
            if (c->Mount.MountObject != null) muted.Add((nint)c->Mount.MountObject);
            if (c->CompanionData.CompanionObject != null) muted.Add((nint)c->CompanionData.CompanionObject);
            if (c->OrnamentData.OrnamentObject != null) muted.Add((nint)c->OrnamentData.OrnamentObject);
        }
        mutedObjects = muted;
    }

    private nint LoadCharacterSoundDetour(
        VfxContainer* self, int soundNumber, int unk2, nint gameObject, ulong autoRelease, int weaponDataIndex, int unk6, ulong unk7)
    {
        try
        {
            if (self != null && self->OwnerObject != null)
            {
                var owner = self->OwnerObject;
                if (mutedObjects.Count > 0 && mutedObjects.Contains((nint)owner))
                {
                    SkippedSounds++;
                    return 0; // don't load the sound: nothing plays
                }

                // Emotes muted for everyone: the emote this player is doing right now.
                if (config.AllEnabled && config.EmotesEnabled && config.MutedEmotes.Count > 0
                    && owner->GameObject.ObjectKind == FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind.Pc
                    && (config.EmotesMuteSelf || (nint)owner != localPlayer)
                    && owner->EmoteController.EmoteId != 0
                    && config.MutedEmotes.Contains(owner->EmoteController.EmoteId))
                {
                    SkippedSounds++;
                    return 0;
                }
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "Checking a character sound failed; letting it play.");
        }

        return voiceHook!.Original(self, soundNumber, unk2, gameObject, autoRelease, weaponDataIndex, unk6, unk7);
    }

    // ---------------------------------------------------------------- chat: hide emote messages

    private void OnChat(Dalamud.Game.Chat.IHandleableChatMessage msg)
    {
        if (msg.IsHandled || !config.AllEnabled || !config.IndividualEnabled || msg.LogKind is not (XivChatType.StandardEmote or XivChatType.CustomEmote))
            return;
        if (!config.MutedPeople.Any(p => p.Enabled && p.HideEmoteMessages))
            return;

        // The person is in the sender (custom /em) or, for standard emotes, in the message itself.
        var player = msg.Sender.Payloads.OfType<PlayerPayload>().FirstOrDefault()
                     ?? msg.Message.Payloads.OfType<PlayerPayload>().FirstOrDefault();
        if (player == null)
            return;
        var p = Find(player.PlayerName, player.World.RowId);
        if (p is { Enabled: true, HideEmoteMessages: true })
            msg.PreventOriginal();
    }

    // ---------------------------------------------------------------- right-click menu on players

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        // Right-click on a player anywhere: in the world, the target window, the party list, chat, and the social
        // windows (friends, Free Company, linkshells, player search…). Each fills in different details, so take the
        // name and home server from whichever is there.
        if (args.MenuType != ContextMenuType.Default || args.Target is not MenuTargetDefault t)
            return;

        string? name = null;
        uint worldId = 0;
        try
        {
            if (t.TargetObject is IPlayerCharacter pc)
            {
                name = pc.Name.TextValue;
                worldId = pc.HomeWorld.RowId;
            }
            else if (t.TargetObject != null)
            {
                return; // an NPC, a minion or anything else that isn't a player
            }

            if (string.IsNullOrEmpty(name))
                name = t.TargetName;
            if (worldId == 0)
                worldId = t.TargetHomeWorld.RowId;
            if ((string.IsNullOrEmpty(name) || worldId == 0) && t.TargetCharacter is { } cd)
            {
                if (string.IsNullOrEmpty(name)) name = cd.Name;
                if (worldId == 0) worldId = cd.HomeWorld.RowId;
            }
        }
        catch (Exception ex)
        {
            log.Debug(ex, $"Couldn't read the right-clicked player ({args.AddonName}).");
            return;
        }

        // Only real players: "First Last" on a public server.
        var world = Worlds().FirstOrDefault(w => w.Id == worldId);
        if (string.IsNullOrWhiteSpace(name) || world.Id == 0 || name.Trim().Split(' ').Length != 2)
            return;
        var worldName = world.Name;

        var muted = Find(name, worldId);
        args.AddMenuItem(new MenuItem
        {
            Name = muted == null ? "Add to MuteList (Will-o-Muter)" : "Remove from MuteList (Will-o-Muter)",
            UseDefaultPrefix = true,
            OnClicked = _ =>
            {
                if (muted == null) Add(name, worldId, worldName);
                else Remove(muted);
            },
        });
        var white = FindWhite(name, worldId);
        args.AddMenuItem(new MenuItem
        {
            Name = white == null ? "Add to WhiteList (Will-o-Muter)" : "Remove from WhiteList (Will-o-Muter)",
            UseDefaultPrefix = true,
            OnClicked = _ =>
            {
                if (white == null) AddWhite(name, worldId, worldName);
                else RemoveWhite(white);
            },
        });
    }
}
