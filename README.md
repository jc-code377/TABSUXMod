# Better UX

Quality-of-life fixes for the Totally Accurate Battle Simulator custom content browser. No new units, factions, or gameplay changes — just a smoother workflow for managing your custom content.

## Features

**Bulk delete custom units**
Adds a "SELECT TO DELETE" button to the custom unit browser. Click it to enter select mode, tick the units you want to remove with the checkbox that appears on each card, then hit "DELETE" to remove them all at once — no more deleting one unit at a time through the confirmation popup.

**Double-click to play a custom map**
Double-click a custom map card to jump straight into playing it, instead of opening the card and clicking "Play" separately.

**Auto-confirms the "load mods" warning**
Skips the "Mods can cause the game to become unstable, do you want to load mods now?" popup that otherwise appears every time you open custom content. It's answered "Yes" automatically, exactly as if you'd clicked it yourself.

**Suppresses a spurious multiplayer invite popup on launch**
When launched through a mod manager, TABS can fire a bogus "You were invited to a multiplayer game" popup on startup. This mod automatically declines only that first, spurious invite so it doesn't interrupt loading.

## ⚠️ Warnings

- **This mod may interfere with receiving real multiplayer invites.** The launch-invite fix works by auto-declining the *first* invite-related popup each session to get rid of a fake one — but it cannot always tell a real invite from the spurious one, especially if a real invite arrives right at launch. If you're expecting an invite from a friend, be aware it could be silently declined.
- **This mod will always allow custom content**, since it auto-confirms the "load mods" permission prompt every time. If you'd rather be asked before mods/custom content load, this mod isn't for you.

## Installation

Install via a mod manager (recommended) such as the Thunderstore Mod Manager or r2modman. This mod requires **BepInEx 5**.

## Compatibility

Works purely through the custom content browser and menu UI — doesn't touch battle logic, units, or saves. Should be compatible with most other mods.
