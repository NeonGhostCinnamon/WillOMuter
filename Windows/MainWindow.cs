using System.Collections.Generic;
using System.Linq;
using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;

namespace WillOMuter.Windows;

/// <summary>
/// Will-o-Muter's window, in the same style as Will-o-Patchwisp: no title bar (drag the header),
/// round header buttons with a pink outline, light/dark theme, glass cards and a compact minimized bar.
/// </summary>
public sealed class MainWindow : Window
{
    private const string GitHubUrl = "https://github.com/NeonGhostCinnamon/WillOMuter";

    private readonly Plugin plugin;
    private Configuration Config => plugin.Configuration;

    private static float S(float v) => v * ImGuiHelpers.GlobalScale;

    private static readonly WindowSizeConstraints NormalConstraints = new()
    {
        MinimumSize = new Vector2(320, 300),
        MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
    };

    // dragging / minimizing (same approach as Will-o-Patchwisp)
    private bool dragging;
    private Vector2 dragOffset;
    private bool minimized;
    private Vector2 restoreSize;
    private Vector2? sizeOnce;
    private bool resetSizeCondition;
    private bool pushedMiniStyle;
    private Vector2 miniSize = new(260, 34);

    public MainWindow(Plugin plugin)
        : base($"Will-o-Muter##{Plugin.Id}Main", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove)
    {
        this.plugin = plugin;
        Size = new Vector2(440, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = NormalConstraints;
        RespectCloseHotkey = false;
    }

    // ---------------------------------------------------------------- window frame

    public override void PreDraw()
    {
        Theme.Mode = Config.ThemeChoice;

        if (dragging && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            dragging = false;
        if (dragging)
        {
            Position = ImGui.GetIO().MousePos - dragOffset;
            PositionCondition = ImGuiCond.Always;
        }
        else
        {
            Position = null;
        }

        if (resetSizeCondition)
        {
            SizeCondition = ImGuiCond.FirstUseEver;
            resetSizeCondition = false;
        }
        if (sizeOnce is { } once)
        {
            Size = once;
            SizeCondition = ImGuiCond.Always;
            sizeOnce = null;
            resetSizeCondition = !minimized;
        }

        Theme.Push();

        if (minimized)
        {
            var padding = new Vector2(S(10), S(6));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, padding);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, (ImGui.GetFrameHeight() + padding.Y * 2) / 2f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, new Vector2(1, 1));
            pushedMiniStyle = true;
        }
    }

    public override void PostDraw()
    {
        if (pushedMiniStyle)
        {
            ImGui.PopStyleVar(3);
            pushedMiniStyle = false;
        }
        Theme.Pop();
    }

    private void SetMinimized(bool value)
    {
        if (minimized == value)
            return;

        var scale = ImGuiHelpers.GlobalScale;
        if (value)
        {
            restoreSize = ImGui.GetWindowSize();
            minimized = true;
            Flags |= ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
            SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(1, 1), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
            sizeOnce = miniSize / scale;
        }
        else
        {
            minimized = false;
            Flags &= ~(ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            SizeConstraints = NormalConstraints;
            sizeOnce = restoreSize / scale;
        }
    }

    public override void Draw()
    {
        if (minimized)
        {
            DrawMiniBar();
            Size = miniSize / ImGuiHelpers.GlobalScale;
            SizeCondition = ImGuiCond.Always;
            return;
        }

        DrawHeader();
        Theme.AccentBar();
        ImGuiHelpers.ScaledDummy(4f);

        DrawQuickAccess();
        using (var bar = ImRaii.TabBar("##tabs"))
        {
            if (bar.Success)
            {
                var peopleFlags = selectPeopleTab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
                selectPeopleTab = false;
                using (var tab = ImRaii.TabItem("Crowd"))
                {
                    if (tab.Success)
                    {
                        ImGuiHelpers.ScaledDummy(2f);
                        OffNote();
                        DrawAmbientCard();
                        DrawRacesCard();
                    }
                }
                using (var tab = ImRaii.TabItem("People", peopleFlags))
                {
                    if (tab.Success)
                    {
                        ImGuiHelpers.ScaledDummy(2f);
                        OffNote();
                        DrawPeopleCard();
                    }
                }
                using (var tab = ImRaii.TabItem("Emotes"))
                {
                    if (tab.Success)
                    {
                        ImGuiHelpers.ScaledDummy(2f);
                        OffNote();
                        DrawEmotesCard();
                    }
                }
                using (var tab = ImRaii.TabItem("Settings"))
                {
                    if (tab.Success)
                    {
                        ImGuiHelpers.ScaledDummy(2f);
                        DrawServerBarCard();
                        DrawImportExportCard();
                    }
                }
            }
        }
        DrawFooter();
        fileDialog.Draw();
    }

    // ---------------------------------------------------------------- header

    private void DrawHeader()
    {
        var d = S(28);
        var gap = S(6);
        var buttonsW = d * 3 + gap * 2;

        var top = ImGui.GetCursorPos();
        var topScreen = ImGui.GetCursorScreenPos();
        Theme.Heading(plugin.TitleFont, buttonsW + S(8), ("Will-", false), ("o", true), ("-Muter", false));
        var headingH = ImGui.GetItemRectSize().Y;
        var after = ImGui.GetCursorPos();

        var y = top.Y + Math.Max(0, (headingH - d) / 2f);
        var right = ImGui.GetWindowContentRegionMax().X;

        ImGui.SetCursorPos(new Vector2(right - d * 2 - gap, y));
        if (Theme.IconCircle("##min", FontAwesomeIcon.Minus, Theme.Text, d))
            SetMinimized(true);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Minimize");

        ImGui.SetCursorPos(new Vector2(right - buttonsW, y));
        var mode = Config.ThemeChoice;
        if (Theme.ThemeButton("##theme", ref mode, d))
        {
            Config.ThemeChoice = mode;
            plugin.Save();
        }

        ImGui.SetCursorPos(new Vector2(right - d, y));
        if (Theme.IconCircle("##close", FontAwesomeIcon.Times, Theme.Text, d))
            IsOpen = false;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Close");

        // header = title bar: drag to move, double-click to minimize
        var winPos = ImGui.GetWindowPos();
        var headerMax = new Vector2(winPos.X + right - buttonsW - gap, topScreen.Y + Math.Max(headingH, d));
        var overHeader = ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered() && ImGui.IsMouseHoveringRect(winPos, headerMax);
        if (overHeader)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                SetMinimized(true);
                dragging = false;
            }
            else if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                dragging = true;
                dragOffset = ImGui.GetIO().MousePos - winPos;
            }
        }

        ImGui.SetCursorPos(new Vector2(after.X, Math.Max(after.Y, top.Y + d)));
        ImGui.Dummy(Vector2.Zero);
    }

    // ---------------------------------------------------------------- minimized bar

    private void DrawMiniBar()
    {
        var style = ImGui.GetStyle();
        var d = ImGui.GetFrameHeight();
        var start = ImGui.GetCursorScreenPos();
        var winPos = ImGui.GetWindowPos();

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.Text, "Will-");
        ImGui.SameLine(0, 0);
        Theme.DanglingText("o", Theme.BrandPink);
        ImGui.SameLine(0, 0);
        ImGui.TextColored(Theme.Text, "-Muter");

        // status pill
        var (label, on) = StatusText();
        ImGui.SameLine(0, S(10));
        var pillY = start.Y + (d - (ImGui.GetTextLineHeight() + S(2))) / 2f;
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, pillY));
        Theme.Pill(label, on ? Theme.Pink : new Vector4(0.55f, 0.55f, 0.62f, 1f));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"{Plugin.LastCount} players nearby");

        ImGui.SameLine(0, S(10));
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, start.Y));
        if (Theme.IconCircle("##expand", FontAwesomeIcon.ChevronDown, Theme.Text, d))
            SetMinimized(false);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Expand");
        ImGui.SameLine(0, S(4));
        if (Theme.IconCircle("##close", FontAwesomeIcon.Times, Theme.Text, d))
            IsOpen = false;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Close");

        var end = ImGui.GetItemRectMax();
        miniSize = new Vector2(end.X - winPos.X, end.Y - winPos.Y) + style.WindowPadding;

        if (ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered())
        {
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                SetMinimized(false);
                dragging = false;
            }
            else if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                dragging = true;
                dragOffset = ImGui.GetIO().MousePos - winPos;
            }
        }
    }

    private (string Label, bool On) StatusText()
        => !Config.Enabled || !Config.AllEnabled ? ("Off", false) : Config.IsMuted ? ("Muted", true) : ("Unmuted", false);

    // ---------------------------------------------------------------- quick access

    private bool selectPeopleTab;
    private bool showWhiteList;
    private bool colorDirty;
    private readonly HashSet<string> openPeople = new(); // MuteList people whose options are folded out

    private void ToggleOpen(string key)
    {
        if (!openPeople.Remove(key))
            openPeople.Add(key);
    }

    /// <summary>Above the tabs: mute your target, jump to the MuteList or WhiteList, and whether it's muting now.</summary>
    private void DrawQuickAccess()
    {
        var ind = plugin.Individual;
        Theme.Card("quick", () =>
        {
            var spacing = ImGui.GetStyle().ItemSpacing.X;
            var d = ImGui.GetFrameHeight();
            var all = Config.AllEnabled;
            var power = all
                ? Theme.IconCircle("##all", FontAwesomeIcon.PowerOff, Theme.Text, d)
                : Theme.IconCircle("##all", FontAwesomeIcon.PowerOff, Theme.OnPink, d, filled: true);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(all ? "Turn everything in Will-o-Muter off\n(crowd, people, races, emotes and the server info bar)." : "Will-o-Muter is off. Click to turn everything back on.");
            if (power)
            {
                Config.AllEnabled = !all;
                plugin.Save();
                ind.Refresh();
            }
            ImGui.SameLine();

            var w = (Theme.CardInnerRight - ImGui.GetCursorScreenPos().X - spacing * 2) / 3f;
            var target = Plugin.Targets.Target as Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter;
            var onList = target != null ? ind.Find(target.Name.TextValue, target.HomeWorld.RowId) : null;
            using (ImRaii.Disabled(target == null))
            {
                if (Theme.Button(onList != null ? "Unmute target" : "Mute target", new Vector2(w, 0)))
                {
                    if (onList != null) ind.Remove(onList);
                    else ind.Add(target!.Name.TextValue, target.HomeWorld.RowId, target.HomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty);
                }
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(target == null ? "Target a player first." : onList != null ? $"Take {target.Name.TextValue} off the MuteList." : $"Add {target.Name.TextValue} to the MuteList.");
            ImGui.SameLine();
            if (OutlineButton($"MuteList ({Config.MutedPeople.Count})", w))
            {
                selectPeopleTab = true;
                showWhiteList = false;
            }
            ImGui.SameLine();
            if (OutlineButton($"WhiteList ({Config.WhiteList.Count})", w))
            {
                selectPeopleTab = true;
                showWhiteList = true;
            }
        });
    }

    /// <summary>Card heading in the rounded title font, pink (Crowd, Emotes).</summary>
    private void CardTitle(string text)
    {
        using (plugin.TitleFont.Available ? plugin.TitleFont.Push() : null)
        {
            ImGui.SetWindowFontScale(0.6f);
            ImGui.TextColored(Theme.AccentText, text);
            ImGui.SetWindowFontScale(1f);
        }
    }

    /// <summary>Shown at the top of every tab while everything is turned off.</summary>
    private void OffNote()
    {
        if (Config.AllEnabled)
            return;
        Theme.Wrapped("Will-o-Muter is off. Turn it back on with the power button at the top.", Theme.Amber);
        ImGuiHelpers.ScaledDummy(2f);
    }

    /// <summary>Rounded button with a pink outline (not filled).</summary>
    private static bool OutlineButton(string label, float width)
    {
        var h = ImGui.GetFrameHeight();
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##" + label, new Vector2(width, h));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var round = Theme.UseDalamud ? Theme.R(0) : h / 2f;
        if (hovered)
            dl.AddRectFilled(pos, pos + new Vector2(width, h), ImGui.GetColorU32(Theme.GlassHover), round);
        dl.AddRect(pos, pos + new Vector2(width, h), ImGui.GetColorU32(Theme.Pink), round, ImDrawFlags.None, S(1.4f));
        var ts = ImGui.CalcTextSize(label);
        dl.AddText(pos + new Vector2((width - ts.X) / 2f, (h - ts.Y) / 2f), ImGui.GetColorU32(Theme.Text), label);
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return clicked;
    }

    // ---------------------------------------------------------------- crowd tab: ambient sounds

    /// <summary>One card: on/off and status, the nearby count, and the mute conditions.</summary>
    private void DrawAmbientCard()
    {
        Theme.Card("ambient", () =>
        {
            var left = Theme.CardInnerLeft;
            var right = Theme.CardInnerRight;
            var top = ImGui.GetCursorScreenPos().Y;

            // Title, with the crowd on/off button and the status badge on the right.
            CardTitle("Crowd");
            var titleH = ImGui.GetItemRectSize().Y;
            var (label, on) = StatusText();
            var icon = !Config.Enabled ? FontAwesomeIcon.PowerOff : on ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeUp;
            var h = ImGui.GetFrameHeight() + S(4);
            var gap = S(8);
            var bw = h + gap + Theme.StatusBadgeWidth(icon, label);
            var after = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(new Vector2(right - bw, top + Math.Min(0, (titleH - h) / 2f)));
            DrawPowerButton(h);
            ImGui.SameLine(0, gap);
            Theme.StatusBadge(icon, label, on, h);
            ImGui.SetCursorScreenPos(new Vector2(left, Math.Max(after.Y, top + h + S(2))));

            // "1 / 10" and a short line next to it.
            var count = Plugin.LastCount;
            var rowTop = ImGui.GetCursorScreenPos().Y;
            using (plugin.TitleFont.Available ? plugin.TitleFont.Push() : null)
            {
                ImGui.SetWindowFontScale(0.75f);
                ImGui.TextColored(count >= Config.MuteAtPlayers ? Theme.AccentText : Theme.Text, count.ToString());
                ImGui.SameLine(0, S(5));
                ImGui.TextColored(Theme.AccentText, "/");
                ImGui.SameLine(0, S(5));
                ImGui.TextColored(Theme.Muted, Config.MuteAtPlayers.ToString());
                ImGui.SetWindowFontScale(1f);
            }
            var numBottom = ImGui.GetItemRectMax().Y;
            ImGui.SameLine(0, S(10));
            var where = Config.Radius > 0 ? $"within {Config.Radius:0} yalms" : "around you";
            var line = $"nearby players {where} · ambient sounds mute at {Config.MuteAtPlayers}";
            ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, rowTop + Math.Max(0, (numBottom - rowTop - ImGui.GetTextLineHeight()) / 2f)));
            ImGui.BeginGroup();
            using (ImRaii.TextWrapPos(ImGui.GetCursorPosX() + (right - ImGui.GetCursorScreenPos().X)))
                ImGui.TextColored(Theme.Muted, line);
            ImGui.EndGroup();
            ImGui.SetCursorScreenPos(new Vector2(left, Math.Max(numBottom, ImGui.GetItemRectMax().Y) + S(4)));

            // The four settings, half width, two by two.
            var colGap = S(14);
            var half = (Theme.CardInnerWidth - colGap) / 2f;
            var rowStart = ImGui.GetCursorScreenPos();

            ImGui.BeginGroup();
            var mute = Config.MuteAtPlayers;
            if (IntSlider("Mute when at least", "##mute", ref mute, 1, 100, "%d players", half,
                    "Mute the ambient sounds once this many players are around you."))
                Config.MuteAtPlayers = mute;
            var radius = Config.Radius;
            if (FloatSlider("Radius", "##radius", ref radius, 5, 100, 1f, "%.0f yalms", half,
                    "Only count players within this distance."))
                Config.Radius = radius;
            ImGui.EndGroup();
            var leftBottom = ImGui.GetItemRectMax().Y;

            ImGui.SetCursorScreenPos(new Vector2(left + half + colGap, rowStart.Y));
            ImGui.BeginGroup();
            var margin = Config.UnmuteMargin;
            if (IntSlider("Unmute margin", "##margin", ref margin, 0, 50, "%d players", half,
                    $"Unmute again once the crowd drops below {Math.Max(1, Config.MuteAtPlayers - margin)} players. Prevents flickering on and off."))
                Config.UnmuteMargin = margin;
            var delay = Config.UnmuteDelaySeconds;
            if (FloatSlider("Unmute delay", "##delay", ref delay, 0, 30, 0.5f, "%.1f seconds", half,
                    "How long it has to stay quiet before your volume comes back."))
                Config.UnmuteDelaySeconds = delay;
            ImGui.EndGroup();

            ImGui.SetCursorScreenPos(new Vector2(left, Math.Max(leftBottom, ImGui.GetItemRectMax().Y)));
            ImGui.Dummy(Vector2.Zero);
        });
    }

    // ---------------------------------------------------------------- crowd tab: races and genders

    private static readonly (byte Id, string Name)[] Races =
    {
        (0, "Any race"), (1, "Hyur"), (2, "Elezen"), (3, "Lalafell"), (4, "Miqo'te"),
        (5, "Roegadyn"), (6, "Au Ra"), (7, "Hrothgar"), (8, "Viera"),
    };

    private static string GenderName(int g) => g switch { 0 => "Male", 1 => "Female", _ => "Any gender" };

    private void DrawRacesCard()
    {
        Theme.Card("races", () =>
        {
            var right = Theme.CardInnerRight;
            ImGui.TextColored(Theme.Text, "Races and genders");
            var top = ImGui.GetItemRectMin().Y;
            var after = ImGui.GetCursorScreenPos();
            var sw = ImGui.GetFrameHeight() * 0.8f * 1.9f;
            ImGui.SetCursorScreenPos(new Vector2(right - sw, top));
            if (Switch("##raceson", Config.RaceRulesEnabled))
            {
                Config.RaceRulesEnabled = !Config.RaceRulesEnabled;
                plugin.Save();
                plugin.Individual.Refresh();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Config.RaceRulesEnabled ? "On. Click to turn all race and gender rules off." : "Off. Click to turn the rules on.");
            ImGui.SetCursorScreenPos(after);
            Theme.BrandText("Mute the sounds of everyone of a race or gender. People on your WhiteList are never muted, and neither are you.", Theme.Muted, right);

            RaceRule? remove = null;
            for (var i = 0; i < Config.RaceRules.Count; i++)
            {
                var r = Config.RaceRules[i];
                using var id = ImRaii.PushId(i);
                Divider();
                ImGui.AlignTextToFramePadding();
                if (Switch("##on", r.Enabled))
                {
                    r.Enabled = !r.Enabled;
                    plugin.Save();
                    plugin.Individual.Refresh();
                }
                ImGui.SameLine(0, S(8));
                ImGui.SetNextItemWidth(S(110));
                using (var combo = ImRaii.Combo("##race", Races.FirstOrDefault(x => x.Id == r.Race).Name ?? "Any race"))
                {
                    if (combo.Success)
                        foreach (var (rid, rname) in Races)
                            if (ImGui.Selectable(rname, r.Race == rid)) { r.Race = rid; plugin.Save(); plugin.Individual.Refresh(); }
                }
                ImGui.SameLine();
                ImGui.SetNextItemWidth(S(110));
                using (var combo = ImRaii.Combo("##gender", GenderName(r.Gender)))
                {
                    if (combo.Success)
                        foreach (var g in new[] { -1, 0, 1 })
                            if (ImGui.Selectable(GenderName(g), r.Gender == g)) { r.Gender = g; plugin.Save(); plugin.Individual.Refresh(); }
                }
                ImGui.SameLine();
                ImGui.SetCursorScreenPos(new Vector2(Math.Max(ImGui.GetCursorScreenPos().X, right - ImGui.CalcTextSize("Remove").X), ImGui.GetCursorScreenPos().Y));
                if (TextButton("Remove"))
                    remove = r;

                SoundChoices(r.MuteVoices, v => { r.MuteVoices = v; plugin.Individual.Refresh(); }, r.MuteFootsteps, v => r.MuteFootsteps = v, r.MuteSkills, v => r.MuteSkills = v);
            }
            if (remove != null)
            {
                Config.RaceRules.Remove(remove);
                plugin.Save();
                plugin.Individual.Refresh();
            }

            ImGuiHelpers.ScaledDummy(2f);
            if (TextButton("+ Add a rule"))
            {
                Config.RaceRules.Add(new RaceRule());
                Config.RaceRulesEnabled = true;
                plugin.Save();
                plugin.Individual.Refresh();
            }

            ImGuiHelpers.ScaledDummy(2f);
            var party = Config.RaceSkipParty;
            if (ImGui.Checkbox("Don't apply to my party members", ref party))
            {
                Config.RaceSkipParty = party;
                plugin.Save();
                plugin.Individual.Refresh();
            }
            var friends = Config.RaceSkipFriends;
            if (ImGui.Checkbox("Don't apply to friends and Free Company members", ref friends))
            {
                Config.RaceSkipFriends = friends;
                plugin.Save();
                plugin.Individual.Refresh();
            }
        });
    }

    /// <summary>The three sound checkboxes; footsteps and skills are greyed out until they work.</summary>
    private void SoundChoices(bool voices, Action<bool> setVoices, bool footsteps, Action<bool> setFootsteps, bool skills, Action<bool> setSkills)
    {
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetFrameHeight() * 1.6f);
        var v = voices;
        if (ImGui.Checkbox("Emotes", ref v))
        {
            setVoices(v);
            plugin.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Their emotes (the voice and sound effects like clapping),\nplus battle shouts and other voices.");
        ImGui.SameLine();
        var f = footsteps;
        if (ImGui.Checkbox("Footsteps", ref f))
        {
            setFootsteps(f);
            plugin.Save();
            plugin.Individual.Refresh();
        }
        ImGui.SameLine();
        var k = skills;
        if (ImGui.Checkbox("Skills and actions", ref k))
        {
            setSkills(k);
            plugin.Save();
            plugin.Individual.Refresh();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Sounds of skills, spells and effects, mounting and other animations.");
    }

    private static void Divider()
    {
        ImGuiHelpers.ScaledDummy(1f);
        var y = ImGui.GetCursorScreenPos().Y;
        ImGui.GetWindowDrawList().AddLine(new Vector2(Theme.CardInnerLeft, y), new Vector2(Theme.CardInnerRight, y), ImGui.GetColorU32(Theme.Border with { W = Theme.Border.W * 0.5f }));
        ImGuiHelpers.ScaledDummy(2f);
    }

    /// <summary>Pink text that works like a link.</summary>
    private static bool TextButton(string text)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.AccentText, text);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
    }

    /// <summary>Round power button next to the status badge: turns Will-o-Muter off, or (filled pink) back on.</summary>
    private void DrawPowerButton(float diameter)
    {
        var enabled = Config.Enabled;
        var clicked = enabled
            ? Theme.IconCircle("##power", FontAwesomeIcon.PowerOff, Theme.Text, diameter)
            : Theme.IconCircle("##power", FontAwesomeIcon.PowerOff, Theme.OnPink, diameter, filled: true);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(enabled ? "Turn crowd muting off (the ambient sounds stay as they are)" : "Turn crowd muting back on");
        if (clicked)
        {
            Config.Enabled = !enabled;
            plugin.Save();
        }
    }

    private bool IntSlider(string label, string id, ref int value, int min, int max, string format, float width, string tip)
    {
        ImGui.TextColored(Theme.TextSoft, label);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tip);
        ImGui.SetNextItemWidth(width);
        var changed = ImGui.SliderInt(id, ref value, min, max, format);
        if (changed)
            value = Math.Clamp(value, min, max); // a typed (Ctrl+click) value can be outside the slider
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tip + "\nCtrl+click to type a value.");
        if (ImGui.IsItemDeactivatedAfterEdit())
            plugin.Save();
        ImGuiHelpers.ScaledDummy(2f);
        return changed;
    }

    private bool FloatSlider(string label, string id, ref float value, float min, float max, float step, string format, float width, string tip)
    {
        ImGui.TextColored(Theme.TextSoft, label);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tip);
        ImGui.SetNextItemWidth(width);
        var changed = ImGui.SliderFloat(id, ref value, min, max, format);
        if (changed)
            value = Math.Clamp(MathF.Round(value / step) * step, min, max);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tip + "\nCtrl+click to type a value.");
        if (ImGui.IsItemDeactivatedAfterEdit())
            plugin.Save();
        ImGuiHelpers.ScaledDummy(2f);
        return changed;
    }

    // ---------------------------------------------------------------- server info bar card

    private void DrawServerBarCard()
    {
        Theme.Card("serverbar", () =>
        {
            ImGui.TextColored(Theme.Text, "Server info bar");
            Theme.BrandText("An entry at the top right of the screen (Umbra can show it too).", Theme.Muted, Theme.CardInnerRight);
            ImGuiHelpers.ScaledDummy(2f);

            var shown = Config.DtrShown;
            if (ImGui.Checkbox("Show Will-o-Muter in the server info bar", ref shown))
            {
                Config.DtrShown = shown;
                plugin.Save();
            }
            if (!Config.DtrShown)
                return;

            var w = Theme.CardInnerWidth;
            Label("Show it");
            ImGui.SetNextItemWidth(Math.Min(w, S(260)));
            using (var combo = ImRaii.Combo("##dtrmode", Config.DtrAlways ? "Always" : "Only while muting a crowd"))
            {
                if (combo.Success)
                {
                    if (ImGui.Selectable("Only while muting a crowd", !Config.DtrAlways)) { Config.DtrAlways = false; plugin.Save(); }
                    if (ImGui.Selectable("Always", Config.DtrAlways)) { Config.DtrAlways = true; plugin.Save(); }
                }
            }

            Label("Text while muting");
            TextRow("muting", Config.DtrMutingTextOn, v => Config.DtrMutingTextOn = v,
                Config.DtrMutingRgb, v => Config.DtrMutingRgb = v,
                Config.DtrMutingText, Configuration.DefaultMutingText, v => Config.DtrMutingText = v, w, "Colour while muting");
            if (Config.DtrAlways)
            {
                Label("Text when not muting");
                TextRow("idle", Config.DtrIdleTextOn, v => Config.DtrIdleTextOn = v,
                    Config.DtrIdleRgb, v => Config.DtrIdleRgb = v,
                    Config.DtrIdleText, Configuration.DefaultIdleText, v => Config.DtrIdleText = v, w, "Colour when not muting");
            }

            // Preview: dark strips like the game's bar, one per state.
            Label("Preview");
            PreviewStrip(true, w);
            if (Config.DtrAlways)
            {
                PreviewStrip(false, w);
                Theme.Wrapped("While muting / when not muting.", Theme.Muted);
            }

            Label("Clicking it");
            ImGui.SetNextItemWidth(Math.Min(w, S(260)));
            using (var combo = ImRaii.Combo("##dtrclick", Config.DtrClickToggles ? "Turns Will-o-Muter on/off" : "Opens the settings"))
            {
                if (combo.Success)
                {
                    if (ImGui.Selectable("Turns Will-o-Muter on/off", Config.DtrClickToggles)) { Config.DtrClickToggles = true; plugin.Save(); }
                    if (ImGui.Selectable("Opens the settings", !Config.DtrClickToggles)) { Config.DtrClickToggles = false; plugin.Save(); }
                }
            }
            Theme.Wrapped("Right-click always opens the settings.", Theme.Muted);
        });
    }

    private void PreviewStrip(bool muted, float w)
    {
        var (text, rgb) = plugin.DtrLook(muted);
        var pos = ImGui.GetCursorScreenPos();
        var h = ImGui.GetTextLineHeight() + S(8);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(w, h), ImGui.GetColorU32(new Vector4(0.06f, 0.06f, 0.08f, 1f)), S(6));
        var clock = DateTime.Now.ToString("h:mm tt");
        var cw = ImGui.CalcTextSize(clock).X;
        var ty = pos.Y + S(4);
        dl.AddText(new Vector2(pos.X + w - S(10) - cw, ty), ImGui.GetColorU32(Plugin.GameDefaultColor), clock);
        if (text.Length > 0)
            dl.AddText(new Vector2(pos.X + w - S(10) - cw - S(16) - ImGui.CalcTextSize(text).X, ty), ImGui.GetColorU32(Plugin.ToVector(rgb)), text);
        else
            dl.AddText(new Vector2(pos.X + S(10), ty), ImGui.GetColorU32(Theme.Muted), "(nothing to show: the entry stays hidden)");
        ImGui.Dummy(new Vector2(w, h));
        ImGuiHelpers.ScaledDummy(1f);
    }

    private static void Label(string text)
    {
        ImGuiHelpers.ScaledDummy(2f);
        ImGui.TextColored(Theme.AccentText, text);
    }

    /// <summary>One text setting: [on/off switch] [colour button] [text box] Reset.</summary>
    private void TextRow(string id, bool on, Action<bool> setOn, uint? rgb, Action<uint?> setRgb,
        string value, string fallback, Action<string> set, float width, string colorTitle)
    {
        using var pid = ImRaii.PushId(id);
        ImGui.AlignTextToFramePadding();
        if (Switch("##on", on))
        {
            setOn(!on);
            plugin.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(on ? "Text on. Turn off to show nothing in this state." : "Text off: nothing is shown in this state. Turn on to show the text.");

        ImGui.SameLine(0, S(6));
        using (ImRaii.Disabled(!on))
        {
            if (ColorButton("##color", rgb))
            {
                pickerColor = Plugin.ToVector(rgb);
                ImGui.OpenPopup("##picker");
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(rgb == null ? "Colour: the game's default" : $"Colour: #{rgb:X6}");
            DrawPicker(colorTitle, rgb, setRgb);

            ImGui.SameLine(0, S(6));
            var resetW = ImGui.CalcTextSize("Reset").X;
            ImGui.SetNextItemWidth(Math.Max(S(80), Math.Min(width - (ImGui.GetCursorScreenPos().X - Theme.CardInnerLeft) - resetW - S(12), S(220))));
            var v = value;
            if (ImGui.InputTextWithHint("##text", fallback.Length > 0 ? fallback : "(blank: nothing is shown)", ref v, 40))
            {
                set(v);
                plugin.Save();
            }
            ImGui.SameLine(0, S(10));
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(value == fallback ? Theme.Muted : Theme.AccentText, "Reset");
            if (value != fallback && ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    set(fallback);
                    plugin.Save();
                }
            }
        }
        if (!on)
            Theme.Wrapped("Off: nothing is shown in this state.", Theme.Muted);
    }

    private Vector4 pickerColor;

    /// <summary>The colour picker popup: any colour, hex/RGB boxes, quick colours, "Game default colour" and Done.</summary>
    private void DrawPicker(string title, uint? rgb, Action<uint?> setRgb)
    {
        using var popup = ImRaii.Popup("##picker");
        if (!popup.Success)
            return;

        ImGui.TextColored(Theme.Muted, title);
        var c = new Vector3(pickerColor.X, pickerColor.Y, pickerColor.Z);
        ImGui.SetNextItemWidth(S(240));
        if (ImGui.ColorPicker3("##pick", ref c, ImGuiColorEditFlags.PickerHueBar | ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.DisplayHex | ImGuiColorEditFlags.DisplayRgb | ImGuiColorEditFlags.InputRgb))
        {
            pickerColor = new Vector4(c, 1f);
            setRgb(Plugin.FromVector(pickerColor));
            colorDirty = true;
        }
        // Write the settings file once the colour is let go, not on every frame of dragging.
        if (colorDirty && !ImGui.IsAnyItemActive())
        {
            colorDirty = false;
            plugin.Save();
        }

        ImGuiHelpers.ScaledDummy(2f);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.Muted, "Quick:");
        var d = ImGui.GetFrameHeight() * 0.8f;
        var dl = ImGui.GetWindowDrawList();
        for (var i = 0; i < Plugin.QuickColors.Length; i++)
        {
            ImGui.SameLine(0, S(6));
            var pos = ImGui.GetCursorScreenPos();
            var q = Plugin.QuickColors[i];
            if (ImGui.InvisibleButton($"##q{i}", new Vector2(d, d)))
            {
                pickerColor = Plugin.ToVector(q);
                setRgb(q);
                plugin.Save();
            }
            var hovered = ImGui.IsItemHovered();
            var center = pos + new Vector2(d / 2f);
            dl.AddCircleFilled(center, d / 2f - S(1), ImGui.GetColorU32(Plugin.ToVector(q)), 24);
            if (rgb == q)
                dl.AddCircle(center, d / 2f + S(2), ImGui.GetColorU32(Theme.Text), 24, S(1.8f));
            else if (hovered)
                dl.AddCircle(center, d / 2f + S(1), ImGui.GetColorU32(Theme.Muted), 24, S(1.2f));
            if (hovered)
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        ImGuiHelpers.ScaledDummy(2f);
        ImGui.TextColored(rgb == null ? Theme.Muted : Theme.AccentText, "Game default colour");
        if (rgb != null && ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                setRgb(null);
                pickerColor = Plugin.GameDefaultColor;
                plugin.Save();
            }
        }
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("Done").X - ImGui.GetStyle().FramePadding.X * 2));
        if (Theme.Button("Done"))
            ImGui.CloseCurrentPopup();
    }

    /// <summary>Small button showing the colour as a dot, with a little arrow.</summary>
    private static bool ColorButton(string id, uint? rgb)
    {
        var h = ImGui.GetFrameHeight();
        var w = h + S(14);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(w, h));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(w, h), ImGui.GetColorU32(hovered ? Theme.GlassHover : Theme.Field), Theme.R(10));
        dl.AddRect(pos, pos + new Vector2(w, h), ImGui.GetColorU32(hovered ? Theme.Pink : Theme.Border), Theme.R(10));
        var r = h * 0.3f;
        var c = pos + new Vector2(S(6) + r, h / 2f);
        dl.AddCircleFilled(c, r, ImGui.GetColorU32(Plugin.ToVector(rgb)), 24);
        dl.AddCircle(c, r, ImGui.GetColorU32(new Vector4(1, 1, 1, 0.35f)), 24, S(1));
        var ax = c.X + r + S(5);
        var ay = pos.Y + h / 2f;
        dl.AddTriangleFilled(new Vector2(ax, ay - S(2)), new Vector2(ax + S(6), ay - S(2)), new Vector2(ax + S(3), ay + S(2)), ImGui.GetColorU32(Theme.AccentText));
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return clicked;
    }

    /// <summary>On/off switch (pink when on).</summary>
    private static bool Switch(string id, bool on)
    {
        var h = ImGui.GetFrameHeight() * 0.8f;
        var w = h * 1.9f;
        var pos = ImGui.GetCursorScreenPos() + new Vector2(0, (ImGui.GetFrameHeight() - h) / 2f);
        ImGui.SetCursorScreenPos(new Vector2(pos.X, ImGui.GetCursorScreenPos().Y));
        var clicked = ImGui.InvisibleButton(id, new Vector2(w, ImGui.GetFrameHeight()));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(w, h), ImGui.GetColorU32(on ? Theme.Pink : hovered ? Theme.GlassHover : Theme.Field), h / 2f);
        dl.AddRect(pos, pos + new Vector2(w, h), ImGui.GetColorU32(on ? Theme.Outline : Theme.Border), h / 2f);
        var r = h / 2f - S(3);
        var cx = on ? pos.X + w - h / 2f : pos.X + h / 2f;
        dl.AddCircleFilled(new Vector2(cx, pos.Y + h / 2f), r, ImGui.GetColorU32(on ? Theme.OnPink : Theme.Text), 20);
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return clicked;
    }

    // ---------------------------------------------------------------- people tab

    private string addName = string.Empty;
    private int addWorld = -1;
    private string worldFilter = string.Empty;
    private string listSearch = string.Empty;

    private void DrawPeopleCard()
    {
        var ind = plugin.Individual;
        Theme.Card("people", () =>
        {
            var right = Theme.CardInnerRight;
            var w = Theme.CardInnerWidth;

            // MuteList / WhiteList switch
            if (Segment($"MuteList ({Config.MutedPeople.Count})", !showWhiteList)) showWhiteList = false;
            ImGui.SameLine(0, 0);
            if (Segment($"WhiteList ({Config.WhiteList.Count})", showWhiteList)) showWhiteList = true;
            if (!showWhiteList)
            {
                ImGui.SameLine();
                var sw = ImGui.GetFrameHeight() * 0.8f * 1.9f;
                var onText = Config.IndividualEnabled ? "On" : "Off";
                ImGui.SetCursorScreenPos(new Vector2(right - sw - S(6) - ImGui.CalcTextSize(onText).X, ImGui.GetCursorScreenPos().Y));
                if (Switch("##liston", Config.IndividualEnabled))
                {
                    Config.IndividualEnabled = !Config.IndividualEnabled;
                    plugin.Save();
                    ind.Refresh();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(Config.IndividualEnabled ? "The MuteList is on. Click to stop muting everyone on it." : "The MuteList is off. Click to turn it on.");
                ImGui.SameLine(0, S(6));
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(Theme.TextSoft, onText);
            }

            Theme.Wrapped(showWhiteList
                ? "People who are never muted by the race and gender rules (your MuteList still applies to them)."
                : "People whose sounds you never want to hear.", Theme.Muted);

            // ---- add someone
            Label("Add someone");
            var target = Plugin.Targets.Target as Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter;
            using (ImRaii.Disabled(target == null))
            {
                if (Theme.Button("Add my target"))
                    AddPerson(target!.Name.TextValue, target.HomeWorld.RowId, target.HomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty);
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(target == null ? "Target a player first." : target.Name.TextValue);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(Math.Min(S(140), w * 0.32f));
            ImGui.InputTextWithHint("##addname", "First Last", ref addName, 32);
            ImGui.SameLine();
            var list = Individual.Worlds();
            ImGui.SetNextItemWidth(Math.Min(S(110), w * 0.25f));
            using (var combo = ImRaii.Combo("##addworld", addWorld >= 0 && addWorld < list.Count ? list[addWorld].Name : "Server", ImGuiComboFlags.HeightLarge))
            {
                if (combo.Success)
                {
                    ImGui.SetNextItemWidth(-1);
                    ImGui.InputTextWithHint("##worldfilter", "Search...", ref worldFilter, 30);
                    for (var i = 0; i < list.Count; i++)
                    {
                        if (worldFilter.Length > 0 && !list[i].Name.Contains(worldFilter, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (ImGui.Selectable(list[i].Name, i == addWorld))
                            addWorld = i;
                    }
                }
            }
            ImGui.SameLine();
            using (ImRaii.Disabled(addName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length != 2 || addWorld < 0))
            {
                if (Theme.Button("Add"))
                {
                    AddPerson(addName.Trim(), list[addWorld].Id, list[addWorld].Name);
                    addName = string.Empty;
                }
            }
            Theme.Wrapped($"Or right-click someone in chat, the party list, the target window, the social windows or the world: \"{(showWhiteList ? "WhiteList" : "MuteList")} in Muter\" (with the pink O). In Chat 2 it's under \"Integrations\".", Theme.Muted);

            // ---- the list
            var count = showWhiteList ? Config.WhiteList.Count : Config.MutedPeople.Count;
            Label(showWhiteList ? "On the WhiteList" : "On the MuteList");
            if (count > 6)
            {
                ImGui.SetNextItemWidth(w);
                ImGui.InputTextWithHint("##search", "Search the list...", ref listSearch, 40);
            }
            if (count == 0)
                Theme.Wrapped("Nobody yet.", Theme.Muted);

            bool Matches(string display) => listSearch.Length == 0 || display.Contains(listSearch, StringComparison.OrdinalIgnoreCase);

            if (showWhiteList)
            {
                ListedPerson? removeWhite = null;
                foreach (var p in Config.WhiteList.Where(p => Matches(p.Display)))
                {
                    using var id = ImRaii.PushId($"{p.Name}@{p.WorldId}");
                    Divider();
                    PersonName(p.WorldName, p.Name, true);
                    ImGui.SameLine();
                    ImGui.SetCursorScreenPos(new Vector2(Math.Max(ImGui.GetCursorScreenPos().X, right - ImGui.CalcTextSize("Remove").X), ImGui.GetCursorScreenPos().Y));
                    if (TextButton("Remove"))
                        removeWhite = p;
                }
                if (removeWhite != null)
                    ind.RemoveWhite(removeWhite);
                return;
            }

            MutedPerson? remove = null;
            foreach (var p in Config.MutedPeople.Where(p => Matches(p.Display)))
            {
                using var id = ImRaii.PushId(p.Key);
                Divider();
                var open = openPeople.Contains(p.Key);

                // One line: arrow, on/off, name, and whether they're muted right now.
                ImGui.AlignTextToFramePadding();
                var rowX = ImGui.GetCursorPosX();
                using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
                {
                    if (TextButton((open ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight).ToIconString()))
                        ToggleOpen(p.Key);
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(open ? "Hide the options." : "Show the options (emotes, footsteps, skills…).");
                ImGui.SameLine(rowX + S(16)); // same spot whichever way the arrow points
                if (Switch("##on", p.Enabled))
                {
                    p.Enabled = !p.Enabled;
                    plugin.Save();
                    ind.Refresh();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(p.Enabled ? "On. Click to pause muting this person." : "Paused. Click to mute this person again.");
                ImGui.SameLine(0, S(8));
                PersonName(p.WorldName, p.Name, p.Enabled);
                if (ImGui.IsItemHovered())
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (ImGui.IsItemClicked())
                    ToggleOpen(p.Key);

                var muted = Config.AllEnabled && Config.IndividualEnabled && p.Enabled
                            && (p.MuteVoices || p.MuteFootsteps || p.MuteSkills);
                var tag = muted ? "Muted" : "Not muted";
                var tagWidth = ImGui.CalcTextSize(tag).X + S(14);
                ImGui.SameLine();
                ImGui.SetCursorScreenPos(new Vector2(Math.Max(ImGui.GetCursorScreenPos().X, right - tagWidth),
                    ImGui.GetCursorScreenPos().Y + (ImGui.GetFrameHeight() - ImGui.GetTextLineHeight() - S(2)) / 2f));
                Theme.Pill(tag, muted ? Theme.Pink : new Vector4(0.55f, 0.55f, 0.62f, 1f));

                if (!open)
                    continue;

                // The fold-out part with this person's options.
                SoundChoices(p.MuteVoices, v => { p.MuteVoices = v; ind.Refresh(); }, p.MuteFootsteps, v => p.MuteFootsteps = v, p.MuteSkills, v => p.MuteSkills = v);
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetFrameHeight() * 1.6f);
                var hide = p.HideEmoteMessages;
                if (ImGui.Checkbox("Hide their emote messages in chat", ref hide))
                {
                    p.HideEmoteMessages = hide;
                    plugin.Save();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Hides lines like \"Kai waves to you.\" in chat.");
                var removeText = "Remove from MuteList";
                ImGui.SetCursorScreenPos(new Vector2(right - ImGui.CalcTextSize(removeText).X, ImGui.GetCursorScreenPos().Y));
                if (TextButton(removeText))
                    remove = p;
            }
            if (remove != null)
                ind.Remove(remove);

            // A part that couldn't be set up (most likely after a game patch) says so here.
            ImGuiHelpers.ScaledDummy(3f);
            var broken = new List<string>();
            if (!ind.VoiceMutingWorks) broken.Add("emotes");
            if (!ind.FootstepMutingWorks) broken.Add("footsteps");
            if (!ind.SkillMutingWorks) broken.Add("skills and emote sound effects");
            if (!ind.EmoteEffectMutingWorks) broken.Add("emote sound effects");
            if (broken.Count > 0)
                Theme.Wrapped($"Muting {string.Join(", ", broken)} isn't working right now (most likely a game patch moved things; it comes back with an update).", Theme.Amber);

        });
    }

    private void AddPerson(string name, uint worldId, string worldName)
    {
        if (showWhiteList) plugin.Individual.AddWhite(name, worldId, worldName);
        else plugin.Individual.Add(name, worldId, worldName);
    }

    /// <summary>"[Lich] Mocha Parfait", the server in grey.</summary>
    private static void PersonName(string world, string name, bool active)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.Muted, $"[{world}]");
        ImGui.SameLine(0, S(5));
        ImGui.TextColored(active ? Theme.Text : Theme.Muted, name);
    }

    /// <summary>One half of the MuteList / WhiteList switch.</summary>
    private static bool Segment(string label, bool on)
    {
        var h = ImGui.GetFrameHeight();
        var w = ImGui.CalcTextSize(label).X + S(22);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##seg" + label, new Vector2(w, h));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(w, h), ImGui.GetColorU32(on ? Theme.Pink : hovered ? Theme.GlassHover : Theme.Field), Theme.R(10));
        dl.AddRect(pos, pos + new Vector2(w, h), ImGui.GetColorU32(on ? Theme.Outline : Theme.Border), Theme.R(10));
        var ts = ImGui.CalcTextSize(label);
        dl.AddText(pos + new Vector2((w - ts.X) / 2f, (h - ts.Y) / 2f), ImGui.GetColorU32(on ? Theme.OnPink : Theme.Text), label);
        if (hovered && !on)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return clicked && !on;
    }

    // ---------------------------------------------------------------- emotes tab

    private sealed record EmoteInfo(uint Id, string Name, string Command, uint Icon, uint Category);
    private static List<EmoteInfo>? emotes;
    private string emoteSearch = string.Empty;
    private bool emotesOnlyMuted;

    private static List<EmoteInfo> Emotes()
        => emotes ??= Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Emote>()
            .Where(e => e.TextCommand.RowId != 0 && !string.IsNullOrWhiteSpace(e.Name.ExtractText()))
            .Select(e => new EmoteInfo(e.RowId, e.Name.ExtractText(), e.TextCommand.ValueNullable?.Command.ExtractText() ?? string.Empty, e.Icon, e.EmoteCategory.RowId))
            .GroupBy(e => e.Name).Select(g => g.First())
            .OrderBy(e => e.Name)
            .ToList();

    private void DrawEmotesCard()
    {
        Theme.Card("emotes", () =>
        {
            var right = Theme.CardInnerRight;
            var w = Theme.CardInnerWidth;
            CardTitle("Emotes");
            var top = ImGui.GetItemRectMin().Y;
            var after = ImGui.GetCursorScreenPos();
            var sw = ImGui.GetFrameHeight() * 0.8f * 1.9f;
            ImGui.SetCursorScreenPos(new Vector2(right - sw, top));
            if (Switch("##emoteson", Config.EmotesEnabled))
            {
                Config.EmotesEnabled = !Config.EmotesEnabled;
                plugin.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Config.EmotesEnabled ? "On. Click to stop muting these emotes." : "Off. Click to mute the emotes you picked.");
            ImGui.SetCursorScreenPos(after);
            Theme.BrandText("Pick emotes to mute.", Theme.Muted, right);

            var self = Config.EmotesMuteSelf;
            if (ImGui.Checkbox("Also when I use them", ref self))
            {
                Config.EmotesMuteSelf = self;
                plugin.Save();
            }

            ImGuiHelpers.ScaledDummy(2f);
            ImGui.SetNextItemWidth(w - S(150));
            ImGui.InputTextWithHint("##emotesearch", "Search emotes (name or /command)...", ref emoteSearch, 40);
            ImGui.SameLine();
            ImGui.Checkbox($"Muted only ({Config.MutedEmotes.Count})", ref emotesOnlyMuted);

            var list = Emotes().Where(e =>
                (!emotesOnlyMuted || Config.MutedEmotes.Contains(e.Id))
                && (emoteSearch.Length == 0 || e.Name.Contains(emoteSearch, StringComparison.OrdinalIgnoreCase)
                    || e.Command.Contains(emoteSearch, StringComparison.OrdinalIgnoreCase))).ToList();

            var h = Math.Min(S(320), list.Count * (ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y) + S(8));
            using (var child = ImRaii.Child("##emotelist", new Vector2(w, Math.Max(h, ImGui.GetFrameHeight() * 2)), false))
            {
                if (child.Success)
                {
                    if (list.Count == 0)
                        Theme.Wrapped(emotesOnlyMuted ? "No emotes muted yet." : "No emote matches your search.", Theme.Muted);
                    var iconSize = ImGui.GetFrameHeight();
                    // Only the rows that can be seen are drawn (and only their icons loaded).
                    var rowH = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y;
                    var top0 = ImGui.GetCursorPosY();
                    var first = Math.Max(0, (int)((ImGui.GetScrollY() - top0) / rowH));
                    var last = Math.Min(list.Count, first + (int)(ImGui.GetWindowHeight() / rowH) + 2);
                    for (var i = first; i < last; i++)
                    {
                        var e = list[i];
                        ImGui.SetCursorPosY(top0 + i * rowH);
                        using var id = ImRaii.PushId((int)e.Id);
                        var on = Config.MutedEmotes.Contains(e.Id);
                        if (ImGui.Checkbox("##m", ref on))
                        {
                            if (on) Config.MutedEmotes.Add(e.Id);
                            else Config.MutedEmotes.Remove(e.Id);
                            plugin.Save();
                        }
                        ImGui.SameLine();
                        if (e.Icon != 0)
                        {
                            var tex = Plugin.Textures.GetFromGameIcon(new Dalamud.Interface.Textures.GameIconLookup(e.Icon)).GetWrapOrEmpty();
                            ImGui.Image(tex.Handle, new Vector2(iconSize));
                            ImGui.SameLine();
                        }
                        ImGui.AlignTextToFramePadding();
                        ImGui.TextColored(on ? Theme.Text : Theme.TextSoft, e.Name);
                        if (e.Command.Length > 0)
                        {
                            ImGui.SameLine();
                            ImGui.TextColored(Theme.Muted, e.Command);
                        }
                    }
                    if (list.Count > 0)
                    {
                        ImGui.SetCursorPosY(top0 + list.Count * rowH - ImGui.GetStyle().ItemSpacing.Y);
                        ImGui.Dummy(new Vector2(1, 0));
                    }
                }
            }

            if (Config.MutedEmotes.Count > 0)
            {
                ImGuiHelpers.ScaledDummy(2f);
                if (TextButton("Unmute all"))
                    ImGui.OpenPopup("##unmuteall");
                using var popup = ImRaii.Popup("##unmuteall");
                if (popup.Success)
                {
                    ImGui.TextColored(Theme.Text, $"Unmute all {Config.MutedEmotes.Count} emotes?");
                    if (Theme.Button("Unmute all"))
                    {
                        Config.MutedEmotes.Clear();
                        plugin.Save();
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Cancel"))
                        ImGui.CloseCurrentPopup();
                }
            }
            Theme.Wrapped("Mutes the emote's voice and its sound effects (like clapping).", Theme.Muted);
        });
    }

    // ---------------------------------------------------------------- settings tab: import / export

    private readonly Dalamud.Interface.ImGuiFileDialog.FileDialogManager fileDialog = new();
    private bool transferWhite;
    private string importText = string.Empty;
    private string? transferMessage;
    private bool transferError;

    private void DrawImportExportCard()
    {
        var ind = plugin.Individual;
        Theme.Card("transfer", () =>
        {
            var w = Theme.CardInnerWidth;
            ImGui.TextColored(Theme.Text, "Import / Export");
            Theme.Wrapped("Share your lists or keep a copy. One person per line: [Server] First Last", Theme.Muted);
            ImGuiHelpers.ScaledDummy(2f);
            if (ImGui.RadioButton($"MuteList ({Config.MutedPeople.Count})", !transferWhite)) transferWhite = false;
            ImGui.SameLine();
            if (ImGui.RadioButton($"WhiteList ({Config.WhiteList.Count})", transferWhite)) transferWhite = true;
            var listName = transferWhite ? "WhiteList" : "MuteList";

            // ---- export
            Label("Export");
            var export = transferWhite
                ? Individual.Export(Config.WhiteList.Select(p => (p.Name, p.WorldName)))
                : Individual.Export(Config.MutedPeople.Select(p => (p.Name, p.WorldName)));
            var lines = Math.Clamp(export.Split('\n').Length, 2, 8);
            var ro = export;
            ImGui.InputTextMultiline("##export", ref ro, 100000, new Vector2(w, ImGui.GetTextLineHeightWithSpacing() * lines + S(8)), ImGuiInputTextFlags.ReadOnly);
            using (ImRaii.Disabled(export.Length == 0))
            {
                if (Theme.Button("Copy"))
                {
                    ImGui.SetClipboardText(export);
                    SetTransfer($"Copied your {listName}.", false);
                }
                ImGui.SameLine();
                if (Theme.Button("Save as file..."))
                {
                    var text = export;
                    fileDialog.SaveFileDialog("Save your " + listName, ".txt", $"Will-o-Muter {listName}.txt", ".txt", (ok, path) =>
                    {
                        if (!ok) return;
                        try
                        {
                            System.IO.File.WriteAllText(path, text);
                            SetTransfer($"Saved as {System.IO.Path.GetFileName(path)}.", false);
                        }
                        catch (Exception e)
                        {
                            SetTransfer($"Couldn't save the file: {e.Message}", true);
                        }
                    });
                }
            }

            // ---- import
            Label("Import");
            ImGui.InputTextMultiline("##import", ref importText, 100000, new Vector2(w, ImGui.GetTextLineHeightWithSpacing() * 5 + S(8)));
            var (people, skipped) = Individual.Parse(importText);
            var already = people.Count(p => transferWhite ? ind.FindWhite(p.Name, p.WorldId) != null : ind.Find(p.Name, p.WorldId) != null);
            var fresh = people.Count - already;
            if (importText.Trim().Length > 0)
            {
                ImGui.TextColored(Theme.Green, $"{fresh} new");
                if (already > 0) { ImGui.SameLine(); ImGui.TextColored(Theme.Muted, $"· {already} already on the list"); }
                if (skipped.Count > 0)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.Amber, $"· {skipped.Count} skipped");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(string.Join("\n", skipped.Take(15)) + (skipped.Count > 15 ? $"\n… and {skipped.Count - 15} more" : string.Empty));
                }
            }
            using (ImRaii.Disabled(fresh == 0))
            {
                if (Theme.Button($"Add to {listName}"))
                {
                    var added = people.Count(p => transferWhite ? ind.AddWhite(p.Name, p.WorldId, p.WorldName) : ind.Add(p.Name, p.WorldId, p.WorldName));
                    SetTransfer($"Added {added} to your {listName}.", false);
                    importText = string.Empty;
                }
            }
            ImGui.SameLine();
            using (ImRaii.Disabled(people.Count == 0))
            {
                if (Theme.Button($"Replace {listName}"))
                    ImGui.OpenPopup("##confirmreplace");
            }
            ImGui.SameLine();
            if (Theme.Button("Open file..."))
            {
                fileDialog.OpenFileDialog("Open a list", ".txt", (ok, paths) =>
                {
                    if (!ok || paths.Count == 0) return;
                    try
                    {
                        importText = System.IO.File.ReadAllText(paths[0]);
                    }
                    catch (Exception e)
                    {
                        SetTransfer($"Couldn't read the file: {e.Message}", true);
                    }
                }, 1);
            }

            using (var popup = ImRaii.Popup("##confirmreplace"))
            {
                if (popup.Success)
                {
                    ImGui.TextColored(Theme.Text, $"Replace your whole {listName} with these {people.Count} people?");
                    if (Theme.Button("Replace"))
                    {
                        if (transferWhite)
                        {
                            Config.WhiteList.Clear();
                            foreach (var p in people) ind.AddWhite(p.Name, p.WorldId, p.WorldName);
                        }
                        else
                        {
                            Config.MutedPeople.Clear();
                            foreach (var p in people) ind.Add(p.Name, p.WorldId, p.WorldName);
                        }
                        plugin.Save();
                        ind.Refresh();
                        SetTransfer($"Your {listName} now has {people.Count} people.", false);
                        importText = string.Empty;
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Cancel"))
                        ImGui.CloseCurrentPopup();
                }
            }

            if (transferMessage != null)
                Theme.Wrapped(transferMessage, transferError ? Theme.Amber : Theme.AccentText);
        });
    }

    private void SetTransfer(string message, bool error)
    {
        transferMessage = message;
        transferError = error;
    }

    // ---------------------------------------------------------------- footer

    private void DrawFooter()
    {
        ImGuiHelpers.ScaledDummy(2f);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var linkW = Math.Max(S(96), (ImGui.GetContentRegionAvail().X - spacing) / 2f);

        if (Theme.LinkButton("##kofi", FontAwesomeIcon.Coffee, "Ko-fi", rainbow: true, width: linkW))
            Util.OpenLink(Theme.KofiUrl);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Support me on Ko-fi");
        ImGui.SameLine();
        if (Theme.LinkButton("##github", FontAwesomeIcon.CodeBranch, "GitHub", width: linkW))
            Util.OpenLink(GitHubUrl);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(GitHubUrl);
    }
}
