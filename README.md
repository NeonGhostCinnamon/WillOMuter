# Will-o-Muter

Tired of noisy crowds? **Will-o-Muter** mutes the **Ambient Sounds** channel when too many players are nearby, and restores your volume once the crowd thins out.

*(Formerly CrowdMute.)*

## Install

1. In game, type `/xlsettings` and open the **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste this URL (it has all of my plugins), click **+**, tick **Enabled** and **Save and Close**:

   ```
   https://raw.githubusercontent.com/NeonGhostCinnamon/WillOPlugins/HEAD/repo.json
   ```

3. Type `/xlplugins`, search for **Will-o-Muter** and install it.

## Usage

Open the settings with `/willomuter` (or the plugin's settings button in `/xlplugins`).

- **Mute at least**: the number of nearby players at which ambient sound is muted.
- **Unmute margin**: prevents flapping (e.g. mute at 10, unmute below 5 with a margin of 5).
- **Radius**: only count players within this many yalms (default 50).
- **Unmute delay**: how long the area has to stay quiet before your volume comes back.

Your original volume is always restored when the plugin is turned off, unloaded or the game restarts.


## AI usage

AI was used to help create this plugin.
