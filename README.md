<div align="center">

<img src="images/readme/header.svg" alt="Will-o-Muter: mutes the ambience when a crowd gathers around you" width="100%">

<br><br>

<a href="#how-to-install"><img src="images/readme/btn-install.svg" alt="Install in Dalamud" height="46"></a>&nbsp;
<a href="https://github.com/NeonGhostCinnamon/WillOPlugins"><img src="images/readme/btn-plugins.svg" alt="All my plugins" height="46"></a>

</div>

## Usage

I've created these plugins for my own use, but figured I might as well release them on GitHub. They're not as fancy as "proper" plugins, but I hope you can make good use of them too!

## Description

Will-o-Muter mutes the ambient sounds when a crowd gathers around you, and can silence specific people, whole races or genders, and emotes you never want to hear.

<details>
<summary><b>More details and settings</b></summary>
<br>

- **Crowd**: mutes the Ambient Sounds channel when at least a number of players are within a **radius** you choose, with an **unmute margin** and **unmute delay** so it doesn't flicker. Your own volume always comes back: when you turn it off, when the plugin unloads, even after a crash.
- **People**: a **MuteList** of people you never want to hear, and a **WhiteList** of people who are never muted. For each person you choose what to mute: their **emotes** (voices and sound effects like clapping), **footsteps** and **skills and actions**. Add someone by right-clicking them in chat, the party list, the target window, the social windows or the world, with "Mute target", or by name and server. You can also hide their emote messages in chat.
- **Races and genders**: mute the emotes, footsteps and skills of everyone of a race and/or gender, optionally leaving out your party, friends and Free Company.
- **Emotes**: pick emotes to mute for everyone (also when you use them, if you like).
- **Quick access**: one button turns everything off, plus "Mute target" and shortcuts to your lists.
- **Server info bar**: "Muting crowd" at the top right of your screen (also usable in Umbra), with your own text and colour.
- **Import / Export**: share your MuteList or WhiteList as "[Server] First Last" lines, or keep a copy as a file.

</details>

## How to install

1. In game, type `/xlsettings` and open the **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste this URL, click **+**, tick **Enabled** and click **Save and Close**:

   ```
   https://raw.githubusercontent.com/NeonGhostCinnamon/WillOPlugins/HEAD/repo.json
   ```

3. Type `/xlplugins`, search for **Will-o-Muter** and install it.
4. Open it any time with `/willomuter`.

## AI Disclosure

This plugin was written and tested with the help of AI.

<br>

<div align="center">
<img src="images/readme/footer.svg" alt="Will-o-Cinnamon" width="100%">
<br>
<sub><a href="https://ko-fi.com/willocinnamon">Support me on Ko-fi</a></sub>
</div>
