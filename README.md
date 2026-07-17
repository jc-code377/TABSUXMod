# Better UX

Quality-of-life fixes for the Totally Accurate Battle Simulator custom content browser. No new units, factions, or gameplay changes — just a smoother workflow for managing your custom content.

## Features

**Bulk delete custom units**
Adds a "SELECT TO DELETE" button to the custom unit browser. Click it to enter select mode, tick the units you want to remove with the checkbox that appears on each card, then hit "DELETE" to remove them all at once — no more deleting one unit at a time through the confirmation popup.

**Adds all unit bases to UC**
All unit bases can be added via the UC base selector, even bases from mods!
This also allows you to load units with custom bases in UC without having to reapply the base, and fixes some isssues with custom base units getting corrupted.


**Auto-confirms the "load mods" warning**
Skips the "Mods can cause the game to become unstable, do you want to load mods now?" popup that otherwise appears every time you open custom content. It's answered "Yes" automatically, exactly as if you'd clicked it yourself.

**Removes multiplayer invite popup on launch**
When launched through mod managers, TABS fires a bogus invite popup on startup. This mod automatically declines only that first invite.

**Double-click to play a custom map**
Double-click a custom map card to jump straight into playing it, instead of opening the card and clicking "Play" separately.

## ⚠️ Warnings

- **This mod may interfere with receiving multiplayer invites.** The launch-invite fix works by auto-declining the *first* invite-related popup each session to get rid of a fake one — but it cannot always tell a real invite from the spurious one, especially if a real invite arrives right at launch. If you're expecting an invite from a friend, be aware it could be silently declined.
- **This mod will always allow custom content**, since it auto-confirms the "load mods" permission prompt every time. If you'd rather be asked before mods/custom content load, this mod isn't for you.

## Installation

Install via a mod manager (recommended) such as the Thunderstore Mod Manager or r2modman. This mod requires **BepInEx 5**.

## Compatibility

Works purely through the custom content browser and menu UI — doesn't touch battle logic, units, or saves. Should be compatible with most other mods.
