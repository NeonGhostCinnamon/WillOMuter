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
        : base("Will-o-Muter##WillOMuterMain", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove)
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

        DrawStatusCard();
        using (var bar = ImRaii.TabBar("##tabs"))
        {
            if (bar.Success)
            {
                using (var tab = ImRaii.TabItem("Muting"))
                {
                    if (tab.Success)
                    {
                        ImGuiHelpers.ScaledDummy(2f);
                        DrawSettingsCard();
                    }
                }
                using (var tab = ImRaii.TabItem("Settings"))
                {
                    if (tab.Success)
                    {
                        ImGuiHelpers.ScaledDummy(2f);
                        DrawServerBarCard();
                    }
                }
            }
        }
        DrawFooter();
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
        => !Config.Enabled ? ("Off", false) : Config.IsMuted ? ("Muted", true) : ("Unmuted", false);

    // ---------------------------------------------------------------- status card

    private void DrawStatusCard()
    {
        Theme.Card("status", () =>
        {
            var left = Theme.CardInnerLeft;
            var right = Theme.CardInnerRight;
            var top = ImGui.GetCursorScreenPos().Y;

            // Left: big "count / threshold"
            ImGui.TextColored(Theme.Muted, "Nearby players");
            var count = Plugin.LastCount;
            using (plugin.TitleFont.Available ? plugin.TitleFont.Push() : null)
            {
                ImGui.TextColored(count >= Config.MuteAtPlayers ? Theme.AccentText : Theme.Text, count.ToString());
                ImGui.SameLine(0, S(6));
                ImGui.TextColored(Theme.AccentText, "/");
                ImGui.SameLine(0, S(6));
                ImGui.TextColored(Theme.Muted, Config.MuteAtPlayers.ToString());
            }
            var radiusText = Config.Radius > 0 ? $"Players within {Config.Radius:0} yalms." : "All players around you.";
            Theme.Wrapped(radiusText, Theme.Muted);
            var bottom = ImGui.GetItemRectMax().Y;

            // Right: status badge, vertically centred on the left block
            var (label, on) = StatusText();
            var icon = !Config.Enabled ? FontAwesomeIcon.PowerOff : on ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeUp;
            var h = ImGui.GetFrameHeight() + S(8);
            var gap = S(8);
            var w = h + gap + Theme.StatusBadgeWidth(icon, label);
            var after = ImGui.GetCursorScreenPos();
            if (right - left > w + S(150))
            {
                ImGui.SetCursorScreenPos(new Vector2(right - w, top + (bottom - top - h) / 2f));
                DrawPowerButton(h);
                ImGui.SameLine(0, gap);
                Theme.StatusBadge(icon, label, on, h);
                ImGui.SetCursorScreenPos(after);
                ImGui.Dummy(Vector2.Zero);
            }
            else
            {
                // Narrow window: under the count.
                DrawPowerButton(h);
                ImGui.SameLine(0, gap);
                Theme.StatusBadge(icon, label, on, h);
            }
        });
    }

    /// <summary>Round power button next to the status badge: turns Will-o-Muter off, or (filled pink) back on.</summary>
    private void DrawPowerButton(float diameter)
    {
        var enabled = Config.Enabled;
        var clicked = enabled
            ? Theme.IconCircle("##power", FontAwesomeIcon.PowerOff, Theme.Text, diameter)
            : Theme.IconCircle("##power", FontAwesomeIcon.PowerOff, Theme.OnPink, diameter, filled: true);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(enabled ? "Disable Will-o-Muter" : "Enable Will-o-Muter");
        if (clicked)
        {
            Config.Enabled = !enabled;
            plugin.Save();
        }
    }

    // ---------------------------------------------------------------- settings card

    private void DrawSettingsCard()
    {
        Theme.Card("settings", () =>
        {
            ImGui.TextColored(Theme.Text, "Mute conditions");
            Theme.BrandText("Configure when Will-o-Muter should mute and unmute.", Theme.Muted, Theme.CardInnerRight);
            ImGuiHelpers.ScaledDummy(4f);

            var w = Theme.CardInnerWidth;

            var mute = Config.MuteAtPlayers;
            if (IntSlider("Mute when at least", "##mute", ref mute, 1, 100, "%d players", w,
                    "Mute the ambient sounds once this many players are around you."))
                Config.MuteAtPlayers = mute;

            var margin = Config.UnmuteMargin;
            if (IntSlider("Unmute margin", "##margin", ref margin, 0, 50, "%d players", w,
                    $"Unmute again once the crowd drops below {Math.Max(1, Config.MuteAtPlayers - margin)} players. Prevents flickering on and off."))
                Config.UnmuteMargin = margin;

            var radius = Config.Radius;
            if (FloatSlider("Radius", "##radius", ref radius, 5, 100, 1f, "%.0f yalms", w,
                    "Only count players within this distance."))
                Config.Radius = radius;

            var delay = Config.UnmuteDelaySeconds;
            if (FloatSlider("Unmute delay", "##delay", ref delay, 0, 30, 0.5f, "%.1f seconds", w,
                    "How long it has to stay quiet before your volume comes back."))
                Config.UnmuteDelaySeconds = delay;
        });
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
