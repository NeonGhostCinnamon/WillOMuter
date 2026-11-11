using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace WillOMuter;

/// <summary>
/// Chat 2 draws its own right-click menu for chat, so the game's menu entries don't show there. Chat 2 lets other
/// plugins add entries to that menu (under "Integrations"); this puts the MuteList/WhiteList entries there too.
/// </summary>
public sealed class ChatTwoMenu : IDisposable
{
    private readonly Individual individual;
    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<string> register;
    private readonly ICallGateSubscriber<string, object?> unregister;
    private readonly ICallGateSubscriber<object?> available;
    private readonly ICallGateSubscriber<string, PlayerPayload?, ulong, Payload?, SeString?, SeString?, object?> invoke;
    private string? id;

    public ChatTwoMenu(IDalamudPluginInterface pi, Individual individual, IPluginLog log)
    {
        this.individual = individual;
        this.log = log;
        register = pi.GetIpcSubscriber<string>("ChatTwo.Register");
        unregister = pi.GetIpcSubscriber<string, object?>("ChatTwo.Unregister");
        available = pi.GetIpcSubscriber<object?>("ChatTwo.Available");
        invoke = pi.GetIpcSubscriber<string, PlayerPayload?, ulong, Payload?, SeString?, SeString?, object?>("ChatTwo.Invoke");

        available.Subscribe(Register); // Chat 2 loaded or updated after us
        invoke.Subscribe(Draw);
        Register();                    // Chat 2 already running
    }

    private void Register()
    {
        try
        {
            id = register.InvokeFunc();
        }
        catch
        {
            id = null; // Chat 2 isn't installed or running
        }
    }

    public void Dispose()
    {
        available.Unsubscribe(Register);
        invoke.Unsubscribe(Draw);
        if (id != null)
        {
            try { unregister.InvokeAction(id); }
            catch { /* Chat 2 already gone */ }
            id = null;
        }
    }

    // Called by Chat 2 inside its "Integrations" menu.
    private void Draw(string callId, PlayerPayload? sender, ulong contentId, Payload? payload, SeString? senderString, SeString? content)
    {
        if (callId != id)
            return;
        try
        {
            // The clicked name when it's a player, otherwise whoever sent the message.
            var player = payload as PlayerPayload ?? sender;
            if (player == null || Individual.Player(player.PlayerName, player.World.RowId) is not { } p)
                return;

            foreach (var (label, click) in individual.MenuEntries(p.Name, p.WorldId, p.WorldName))
            {
                if (PinkOItem(label))
                    click();
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "Couldn't add the Will-o-Muter entries to Chat 2's menu.");
        }
    }

    /// <summary>A menu entry with the same pink O in front as in the game's menus.</summary>
    private static bool PinkOItem(string label)
    {
        var h = ImGui.GetTextLineHeight();
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.Selectable($"##{label}", false, ImGuiSelectableFlags.None,
            new Vector2(h + ImGui.GetStyle().ItemSpacing.X + ImGui.CalcTextSize(label).X, 0));
        var dl = ImGui.GetWindowDrawList();
        var pink = ImGui.GetColorU32(new Vector4(1f, 126 / 255f, 182 / 255f, 1f));
        dl.AddRect(pos + new Vector2(1, 1), pos + new Vector2(h - 1, h - 1), pink, h * 0.25f, ImDrawFlags.None, 1.5f);
        var o = ImGui.CalcTextSize("O");
        dl.AddText(pos + new Vector2((h - o.X) / 2f, (h - o.Y) / 2f), pink, "O");
        dl.AddText(pos + new Vector2(h + ImGui.GetStyle().ItemSpacing.X, 0), ImGui.GetColorU32(ImGuiCol.Text), label);
        return clicked;
    }
}
