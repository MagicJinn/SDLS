# **~~Revolutionize~~ Simplify Sunless Sea modding with Sunless Data Loading Simplified**

## Sunless Data Loading Simplified (SDLS) is both a modders tool and a clientside mod that allows for Sunless Sea mod creation, without (most of) the jank. About 70% of the fields in a Sunless Sea .json are unused, and this mod allows you to safely remove them!

### **How does it work?**

When creating a mod, a mod author can leave any values that their story or quality doesn't use blank, as well as not having to include any unused fields, which are plentiful as holdouts from Fallen London. Once the mod is created, they can rename the file to `filename`SDLS.json or `filename`.sdls, which will get recognized by the game as valid files. These files can be placed in the same place as regular mods would go.

On startup, before any game logic executes, SDLS looks for valid SDLS files in your addon directory, recursively loops through the data, constructs a temporary data structure, a "dummy tree", and casts the values of the original file into it. It then saves this data next to the original SDLS file, as a .json file, which the game then loads. As a modder, you can distribute the SDLS files, as well as the generated json files, for those who do and don't have SDLS installed. As a user, you can use SDLS to load SDLS mods, if the modder doesn't provide the json files alongside them.

### **Current Support and experimental features:**

During startup, the game tries to load these 13 files:

* **qualities**
* areas
* **events**
* exchanges
* personas
* TileRules
* Tiles
* TileSets
* CombatAttacks
* CombatItems
* SpawnedEntities
* Associations
* Tutorials
* Flavours

Of these 13, only Qualities and Events are currently officially supported. The code to load and convert the remaining 11 is in place, but is **STILL EXPERIMENTAL**. I cannot guarantee I didn't make any mistakes.

Additionally, **COMMENTS ARE NOT SUPPORTED**.

### **What this mod DOESN'T DO:**

* Change the experience for an end user. This mod does not touch any game code, and installing this mod without any SDLS mods will not do anything.
* Improve performance. The loaded .json files are equally bulky as they always were. The .json compilation process can also take a second.
* Fix compatibility issues (yet). If you edit the image of a quality, and another mod edits its name, these mods will still have conflicts.
* Fix mistakes you make, or assume default values when you don't enter one. If you leave Enhancements blank, SDLS handles that because not *every* quality has Enhancements. When you leave BaseWarmUp blank, the game will not load. This is because EVERY GUN has a BaseWarmUp. Except for very specific circumstances, there won't be a default value to catch your screwups.

## **Planned Features:**

* Mod compatibility. Have a config option to merge mods to resolve compatibility issues.
* Additional loaded data support. Currently, I only officially support Qualities and Events, but the other 11 moddable jsons will be refined.
* Sunless Skies support. I do not own Sunless Skies, but will strive to make this mod compatible with it.
* Comment support? (Removing comments from the .json during conversion).

**Special thanks:**

* [Desblat](http://next.nexusmods.com/profile/desblat) and [CleverCrumbish](http://next.nexusmods.com/profile/CleverCrumbish) for helping me with realizing and refining the concept.
* The people in the [BepInEx Discord](http://discord.gg/MpFEDAg)
