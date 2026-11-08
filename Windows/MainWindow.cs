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
        Theme.CenteredText("A quieter Eorzea.", Theme.TextSoft);
        Theme.AccentBar();
        ImGuiHelpers.ScaledDummy(4f);

        DrawStatusCard();
        DrawSettingsCard();
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
            var w = Theme.StatusBadgeWidth(icon, label);
            var after = ImGui.GetCursorScreenPos();
            if (right - left > w + S(150))
            {
                ImGui.SetCursorScreenPos(new Vector2(right - w, top + (bottom - top - h) / 2f));
                Theme.StatusBadge(icon, label, on, h);
                ImGui.SetCursorScreenPos(after);
                ImGui.Dummy(Vector2.Zero);
            }
            else
            {
                Theme.StatusBadge(icon, label, on, h);   // narrow window: under the count
            }
        });
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

    // ---------------------------------------------------------------- footer

    private void DrawFooter()
    {
        ImGuiHelpers.ScaledDummy(2f);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var avail = ImGui.GetContentRegionAvail().X;
        var linkW = S(96);
        var h = ImGui.GetFrameHeight() + S(4);

        // Enable / disable (pink when it would turn the plugin back on)
        var enabled = Config.Enabled;
        var toggleLabel = enabled ? "Disable Will-o-Muter" : "Enable Will-o-Muter";
        var toggleW = Math.Max(S(170), avail - (linkW * 2 + spacing * 2));
        var oneRow = toggleW + linkW * 2 + spacing * 2 <= avail + 0.5f;
        if (!oneRow)
            toggleW = avail;

        bool clicked;
        if (enabled)
        {
            clicked = Theme.LinkButton("##toggle", FontAwesomeIcon.PowerOff, toggleLabel, width: toggleW);
        }
        else
        {
            using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, S(10)))
                clicked = Theme.Button(toggleLabel, new Vector2(toggleW, h));
        }
        if (clicked)
        {
            Config.Enabled = !enabled;
            plugin.Save();
        }

        if (oneRow)
            ImGui.SameLine();
        else
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (avail - (linkW * 2 + spacing)) / 2f));

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
