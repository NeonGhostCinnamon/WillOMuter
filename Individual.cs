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
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.System.Scheduler.Base;

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

    /// <summary>"Emotes": their emotes (voice and sound effects like clapping), battle shouts and other voices.</summary>
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

    // Sounds of animations and effects (skills, emote sound effects like clapping, mounting…). The game's own
    // function got merged into its caller, so the caller is intercepted and the character is read from its data.
    // Search pattern and memory positions as published by Penumbra (Penumbra.GameData Signatures and Penumbra's
    // VolatileOffsets); the positions can move with a game patch, which is why everything is checked first.
    private const string AnimationSoundSignature = "4C 8B DC 56 48 81 EC ?? ?? ?? ?? F3 0F 10 89";
    private const int PlayTimeOffset = 0x254;
    private const int IntermediateOffset = 0x1F8;
    private const int FlagsOffset = 0x4A8;
    private const int ListenerOffset = 0x270;
    private const int FlagsBit = 13;
    private const int CasterVirtualFunction = 1;
    private delegate nint AnimationSoundDelegate(nint self, nint unused, float timeOffset);
    private readonly Hook<AnimationSoundDelegate>? skillHook;

    // Footsteps (search pattern as published by Penumbra.GameData).
    private const string FootstepSignature = "E9 ?? ?? ?? ?? 49 8B CE E8 ?? ?? ?? ?? 8B F8";
    private delegate void FootstepDelegate(GameObject* gameObject, int id, int unk);
    private readonly Hook<FootstepDelegate>? footstepHook;

    // Timeline parts of emotes (the sound effects of /clap, /water…). Called when a character's action timeline
    // moves on; skipping it for a muted character keeps that step's sounds from starting.
    // Search pattern as published by Penumbra.GameData (ScheduleClipUpdate); the timeline sits at 0x38.
    private const string ClipUpdateSignature = "40 53 55 56 57 48 81 EC ?? ?? ?? ?? 48 8B F9";
    private const int ClipTimelineOffset = 0x38;
    private delegate void ClipUpdateDelegate(nint clipScheduler);
    private readonly Hook<ClipUpdateDelegate>? clipHook;

    // Addresses of the characters (and their mounts, minions and accessories) whose sounds are muted right now.
    // Rebuilt a few times a second on the game's thread; the sound function runs on that thread too.
    private HashSet<nint> mutedObjects = new();     // voices
    private HashSet<nint> mutedFootsteps = new();
    private HashSet<nint> mutedSkills = new();
    private HashSet<nint> mutedSkillDraw = new();   // the same characters' draw objects (another way to recognise them)
    private Dictionary<nint, nint> emoteDrawOwner = new(); // draw object -> character, for people with "Emotes" muted

    private nint localPlayer;
    private DateTime lastScan = DateTime.MinValue;

    /// <summary>Whether muting voices works (false when the game function couldn't be found, e.g. right after a patch).</summary>
    public bool VoiceMutingWorks => voiceHook is { IsEnabled: true };
    public bool FootstepMutingWorks => footstepHook is { IsEnabled: true };
    public bool SkillMutingWorks => skillHook is { IsEnabled: true };
    public bool EmoteEffectMutingWorks => clipHook is { IsEnabled: true };

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

        // Each one on its own: if a game patch broke one, only that kind of sound stops being muted.
        try
        {
            footstepHook = interop.HookFromSignature<FootstepDelegate>(FootstepSignature, FootstepDetour);
            footstepHook.Enable();
        }
        catch (Exception ex)
        {
            footstepHook?.Dispose();
            footstepHook = null;
            log.Warning(ex, "Couldn't set up muting of footsteps; that part is turned off.");
        }

        try
        {
            skillHook = interop.HookFromSignature<AnimationSoundDelegate>(AnimationSoundSignature, AnimationSoundDetour);
            skillHook.Enable();
        }
        catch (Exception ex)
        {
            skillHook?.Dispose();
            skillHook = null;
            log.Warning(ex, "Couldn't set up muting of skill and emote sound effects; that part is turned off.");
        }

        try
        {
            clipHook = interop.HookFromSignature<ClipUpdateDelegate>(ClipUpdateSignature, ClipUpdateDetour);
            clipHook.Enable();
        }
        catch (Exception ex)
        {
            clipHook?.Dispose();
            clipHook = null;
            log.Warning(ex, "Couldn't set up muting of emote sound effects; that part is turned off.");
        }
    }

    public void Dispose()
    {
        chat.ChatMessage -= OnChat;
        contextMenu.OnMenuOpened -= OnMenuOpened;
        voiceHook?.Dispose();
        footstepHook?.Dispose();
        skillHook?.Dispose();
        clipHook?.Dispose();
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

    /// <summary>On the game's thread: a few times a second, work out whose voices, footsteps and skill sounds are muted.</summary>
    public void Update()
    {
        localPlayer = objects.LocalPlayer?.Address ?? 0;
        var listOn = config.AllEnabled && config.IndividualEnabled && config.MutedPeople.Count > 0;
        var rulesOn = config.AllEnabled && config.RaceRulesEnabled
                      && config.RaceRules.Any(r => r.Enabled && (r.MuteVoices || r.MuteFootsteps || r.MuteSkills));
        if (!listOn && !rulesOn)
        {
            if (mutedObjects.Count > 0) mutedObjects = new HashSet<nint>();
            if (mutedFootsteps.Count > 0) mutedFootsteps = new HashSet<nint>();
            if (mutedSkills.Count > 0) mutedSkills = new HashSet<nint>();
            if (mutedSkillDraw.Count > 0) mutedSkillDraw = new HashSet<nint>();
            if (emoteDrawOwner.Count > 0) emoteDrawOwner = new Dictionary<nint, nint>();
            return;
        }

        var now = DateTime.UtcNow;
        if ((now - lastScan).TotalMilliseconds < 250)
            return;
        lastScan = now;

        var local = objects.LocalPlayer;
        var myTag = local?.CompanyTag.TextValue ?? string.Empty;
        var voices = new HashSet<nint>();
        var footsteps = new HashSet<nint>();
        var skills = new HashSet<nint>();
        var skillDraw = new HashSet<nint>();
        var emoteDraw = new Dictionary<nint, nint>();
        foreach (var obj in objects)
        {
            if (obj is not IPlayerCharacter pc || pc.Address == IntPtr.Zero)
                continue;
            var name = pc.Name.TextValue;
            var world = pc.HomeWorld.RowId;
            var c = (Character*)pc.Address;

            bool v = false, f = false, k = false;
            if (listOn && Find(name, world) is { Enabled: true } p)
            {
                v = p.MuteVoices;
                f = p.MuteFootsteps;
                k = p.MuteSkills;
            }

            // Race and gender rules: never for yourself, WhiteList people, and (when chosen) party, friends and FC.
            if (rulesOn && pc.Address != local?.Address && FindWhite(name, world) == null)
            {
                var flags = pc.StatusFlags;
                var skip = (config.RaceSkipParty && (flags.HasFlag(StatusFlags.PartyMember) || flags.HasFlag(StatusFlags.AllianceMember)))
                           || (config.RaceSkipFriends && (flags.HasFlag(StatusFlags.Friend)
                               || (myTag.Length > 0 && pc.CompanyTag.TextValue == myTag)));
                if (!skip)
                {
                    var race = c->DrawData.CustomizeData.Data[0];
                    var gender = c->DrawData.CustomizeData.Data[1];
                    foreach (var r in config.RaceRules)
                    {
                        if (!r.Enabled || (r.Race != 0 && r.Race != race) || (r.Gender >= 0 && r.Gender != gender))
                            continue;
                        v |= r.MuteVoices;
                        f |= r.MuteFootsteps;
                        k |= r.MuteSkills;
                    }
                }
            }

            // The person, plus their mount, minion and fashion accessory (separate objects with their own sounds).
            if (v)
            {
                AddWithParts(voices, c);
                if (c->GameObject.DrawObject != null)
                    emoteDraw[(nint)c->GameObject.DrawObject] = (nint)c;
            }
            if (f) AddWithParts(footsteps, c);
            if (k)
            {
                AddWithParts(skills, c);
                if (c->GameObject.DrawObject != null)
                    skillDraw.Add((nint)c->GameObject.DrawObject);
                if (c->Mount.MountObject != null && c->Mount.MountObject->GameObject.DrawObject != null)
                    skillDraw.Add((nint)c->Mount.MountObject->GameObject.DrawObject);
            }
        }
        mutedObjects = voices;
        mutedFootsteps = footsteps;
        mutedSkills = skills;
        mutedSkillDraw = skillDraw;
        emoteDrawOwner = emoteDraw;
    }

    private static void AddWithParts(HashSet<nint> set, Character* c)
    {
        set.Add((nint)c);
        if (c->Mount.MountObject != null) set.Add((nint)c->Mount.MountObject);
        if (c->CompanionData.CompanionObject != null) set.Add((nint)c->CompanionData.CompanionObject);
        if (c->OrnamentData.OrnamentObject != null) set.Add((nint)c->OrnamentData.OrnamentObject);
    }

    /// <summary>Emotes tab: is this player doing an emote that's muted for everyone?</summary>
    private bool EmoteMuted(Character* owner)
        => config.AllEnabled && config.EmotesEnabled && config.MutedEmotes.Count > 0
           && owner->GameObject.ObjectKind == FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind.Pc
           && (config.EmotesMuteSelf || (nint)owner != localPlayer)
           && owner->EmoteController.EmoteId != 0
           && config.MutedEmotes.Contains(owner->EmoteController.EmoteId);

    private void ClipUpdateDetour(nint clipScheduler)
    {
        try
        {
            if (clipScheduler != 0 && (mutedObjects.Count > 0 || (config.EmotesEnabled && config.MutedEmotes.Count > 0)))
            {
                var owner = ClipOwner(clipScheduler);
                if (owner != null)
                {
                    var emote = owner->EmoteController.EmoteId;
                    // Only while they're doing an emote, so their skills, mount and other effects stay as they are.
                    var mute = emote != 0 && (mutedObjects.Contains((nint)owner) || EmoteMuted(owner));
                    if (mute)
                    {
                        SkippedSounds++;
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "Checking an emote's timeline failed; letting it play.");
        }
        clipHook!.Original(clipScheduler);
    }

    /// <summary>The player a timeline step belongs to, or null when that isn't a player character.</summary>
    private static Character* ClipOwner(nint clipScheduler)
    {
        var timeline = *(SchedulerTimeline**)(clipScheduler + ClipTimelineOffset);
        if (timeline == null)
            return null;
        var index = timeline->GetOwningGameObjectIndex();
        if (index < 0 || index >= 200)
            return null;
        var obj = GameObjectManager.Instance()->Objects.IndexSorted[index].Value;
        if (obj == null || !obj->IsCharacter()
            || obj->ObjectKind != FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind.Pc)
            return null;
        return (Character*)obj;
    }

    private void FootstepDetour(GameObject* gameObject, int id, int unk)
    {
        try
        {
            if (gameObject != null && mutedFootsteps.Count > 0 && mutedFootsteps.Contains((nint)gameObject))
            {
                SkippedSounds++;
                return; // no footstep
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "Checking a footstep failed; letting it play.");
        }
        footstepHook!.Original(gameObject, id, unk);
    }

    private nint AnimationSoundDetour(nint self, nint unused, float timeOffset)
    {
        try
        {
            if (self != 0)
            {
                var (ownerPtr, draw) = AnimationSoundOwner(self);
                var mute = false;
                if (ownerPtr != 0)
                {
                    var owner = (GameObject*)ownerPtr;
                    var doingEmote = owner->IsCharacter() && ((Character*)owner)->EmoteController.EmoteId != 0;
                    mute = mutedSkills.Contains(ownerPtr)
                           // "Emotes" for that person: their emote's sound effects (like clapping) too.
                           || (doingEmote && mutedObjects.Contains(ownerPtr))
                           || (owner->IsCharacter() && EmoteMuted((Character*)owner));
                }
                else if (draw != 0)
                {
                    // Only the model is known: the character it belongs to (from the last check) decides.
                    mute = mutedSkillDraw.Contains(draw)
                           || (emoteDrawOwner.TryGetValue(draw, out var ch) && ((Character*)ch)->EmoteController.EmoteId != 0);
                }
                if (mute)
                {
                    SkippedSounds++;
                    return 0; // the game itself returns early like this when there's nothing to play
                }
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "Checking a skill or emote sound failed; letting it play.");
        }
        return skillHook!.Original(self, unused, timeOffset);
    }

    /// <summary>
    /// Who an animation or effect sound belongs to: the character (when the sound's listener can tell), or else the
    /// draw object the listener belongs to. Every step is checked like the game does; anything unclear plays normally.
    /// </summary>
    private static (nint Owner, nint Draw) AnimationSoundOwner(nint self)
    {
        if (*(float*)(self + PlayTimeOffset) < 0)
            return (0, 0);
        var intermediate = *(nint*)(self + IntermediateOffset);
        if (intermediate == 0 || ((*(ushort*)(intermediate + FlagsOffset) >> FlagsBit) & 1) == 0)
            return (0, 0);
        var listener = *(nint*)(intermediate + ListenerOffset);
        if (listener == 0)
            return (0, 0);

        var vtable = *(delegate* unmanaged<nint, GameObject*>**)listener;
        var caster = vtable[CasterVirtualFunction](listener);
        if (caster != null)
            return ((nint)caster, 0);

        // Effect listeners keep the draw object right after their virtual table; for other kinds this is just a
        // number that won't be in the muted sets.
        return (0, ((nint*)listener)[1]);
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
                if (EmoteMuted(owner))
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

    private static ushort? pinkColor;

    /// <summary>The game colour closest to the Will-o pink (#FF7EB6), for the boxed "O" in front of the menu entries.</summary>
    private static ushort PinkColor()
    {
        if (pinkColor is { } cached)
            return cached;
        ushort best = 0;
        var bestDist = double.MaxValue;
        try
        {
            foreach (var row in Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.UIColor>())
            {
                var c = row.Dark; // 0xRRGGBBAA
                if (c == 0 || row.RowId > ushort.MaxValue)
                    continue;
                double r = (c >> 24) & 0xFF, g = (c >> 16) & 0xFF, b = (c >> 8) & 0xFF;
                var d = (r - 255) * (r - 255) + (g - 126) * (g - 126) + (b - 182) * (b - 182);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = (ushort)row.RowId;
                }
            }
        }
        catch
        {
            // Colour list unreadable: the game's default colour is fine.
        }
        pinkColor = best;
        return best;
    }

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
        if (Player(name, worldId) is not { } player)
            return;

        foreach (var (label, click) in MenuEntries(player.Name, player.WorldId, player.WorldName))
        {
            args.AddMenuItem(new MenuItem
            {
                Name = label,
                PrefixChar = 'O',
                PrefixColor = PinkColor(),
                OnClicked = _ => click(),
            });
        }
    }

    /// <summary>
    /// The right-click entries for a player ("MuteList in Muter", "WhiteList in Muter", or their "Remove from"
    /// versions), shared by the game's menus and Chat 2's.
    /// </summary>
    public List<(string Label, Action Click)> MenuEntries(string name, uint worldId, string worldName)
    {
        var muted = Find(name, worldId);
        var white = FindWhite(name, worldId);
        return new List<(string, Action)>
        {
            (muted == null ? "MuteList in Muter" : "Remove from MuteList in Muter",
                () => { if (muted == null) Add(name, worldId, worldName); else Remove(muted); }),
            (white == null ? "WhiteList in Muter" : "Remove from WhiteList in Muter",
                () => { if (white == null) AddWhite(name, worldId, worldName); else RemoveWhite(white); }),
        };
    }

    /// <summary>A real player's name and home server, or null for anything else.</summary>
    public static (string Name, uint WorldId, string WorldName)? Player(string? name, uint worldId)
    {
        var world = Worlds().FirstOrDefault(w => w.Id == worldId);
        if (string.IsNullOrWhiteSpace(name) || world.Id == 0 || name.Trim().Split(' ').Length != 2)
            return null;
        return (name.Trim(), worldId, world.Name);
    }
}
