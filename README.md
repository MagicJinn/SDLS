# **~~Revolutionize~~ Simplify Sunless Sea modding with Sunless Data Loading Simplified**

SDLS is a Unity BepInEx plugin for Sunless Sea.

## Sunless Data Loading Simplified (SDLS) is both a modders tool and a clientside mod that allows for Sunless Sea mod creation, without (most of) the jank. About 70% of the fields in a Sunless Sea .json are unused, and this mod allows you to safely remove them!<br><br>It also [increases loading speed](https://github.com/MagicJinn/SDLS/wiki/FastLoad), and adds quality of life features like [LoadIntoSave](https://github.com/MagicJinn/SDLS/wiki/LoadIntoSave)!


### **How does it work?**

When creating a mod, a mod author can leave any values that their story or quality doesn't use blank, as well as not having to include any unused fields, which are plentiful as holdouts from Fallen London. Once the mod is created, they can rename the file to `filename`SDLS.json or `filename`.sdls, which will get recognized by the plugin as valid files. These files can be placed in the same place as regular mods would go.

On startup, before any game logic executes, SDLS looks for valid SDLS files in your addon directory, recursively loops through the data, constructs a temporary data structure, a "dummy tree", and casts the values of the original file into it. It then saves this data next to the original SDLS file, as a .json file, which the game then loads. As a modder, you can distribute the SDLS files, as well as the generated json files, for those who do and don't have SDLS installed. As a user, you can use SDLS to load SDLS mods, if the modder doesn't provide the json files alongside them.

**For information and tutorials on how to use SDLS, see the [Wiki](https://github.com/MagicJinn/SDLS/wiki).**

### **Current Support and experimental features**

During certain phases of the game's startup, the game tries to load these 16 files:

* **qualities**
* areas
* **events**
* **exchanges**
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
* **CombatConstants**
* **NavigationConstants**

The highlighted entries are **officially** supported. The others will still work, but this is **experimental**. I cannot guarantee I didn't make any mistakes.

### **What this mod DOESN'T DO**

* Create a new mod format. SDLS *simplifies* mod creation by allowing you to omit unused fields, but the mod format stays the same.
* Modify the game's files. SDLS is a compatibility layer, meaning it runs *before* the game is loaded, and does not touch the game's files at all.
* Fix mistakes you make, or assume default values when you don't enter one. Some values such as BaseHullDamage have been defaulted to 0 in SDLS, but do not rely on this.

## **Planned Features**

* Mod compatibility. Have a config option to merge mods to resolve compatibility issues.
* Expanded official support for all loaded .json files.
* Additional QOL features similar to FastLoad and LoadIntoSave.
* Additional small performance patches.

## **Compiling the plugin**

To develop and build the plugin, there are a couple of prerequisites. After cloning the repository

```bash
https://github.com/MagicJinn/SDLS.git
cd SDLS
```

run the following command to download the necessary packages, like BepInEx.

```bash
dotnet restore
```

After this, you need to acquire some DLLs SDLS relies on. Create a `dependencies` folder, and find `JsonFx.dll` and `Sunless.Game.dll` in your `SunlessSea\Sunless Sea_Data\Managed` folder. Copy them into the `dependencies` folder. After this, you should be able to compile the project with the following commands:

Build in debug mode (will include DLog):

```bash
dotnet build
```

Build in release mode:

```bash
dotnet build -c Release -p:Optimize=true
```

The DLLs should be located in `bin/Debug/net35` and `bin/Release/net35` respectively.

### **Special thanks**

* [Desblat](http://next.nexusmods.com/profile/desblat) and [CleverCrumbish](http://next.nexusmods.com/profile/CleverCrumbish) for helping me with realizing and refining the concept.
* [Exotico](https://github.com/andraemon) for creating the excellent reference document on which the SDLS reference is based.
* The people in the [BepInEx Discord](http://discord.gg/MpFEDAg)
