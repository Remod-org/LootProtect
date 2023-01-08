## Loot Protection
Yet another player loot protection plugin for Rust

- Uses ZoneManager, Friends, Clans, Rust teams

- Player boxes, workbenches, etc. are protected from opening and pickup by others.  With Friends/Clans/Teams support, a player's friends will maintain access.

- A list of zones can be set to only protect boxes in those zones.

- A schedule can be set to disable/enable the plugin throughout the actual or in-game time and day.

Note that this is not damage control, only access to contents and pickup of entities.

### Commands
  - /share (Requires lootprotect.share)
    - /share ? - Show sharing status of object in front of you.
    - /share   - Share object in front of you to ALL.
    - /share PLAYERNAME - Share object in front of you to specified player.
    - /share friends - Share with friends only

  - /unshare - Remove all sharing for object in front of you.

  - /bshare - (Requires lootprotect.admin) Share all entities in range of the local TC (Range set by BuildingShareRange config)
  - /bunshare - (Requires lootprotect.admin) Unshare all entities in range of the local TC (Range set by BuildingShareRange config)

  - /lp (Requires lootprotect.admin)
    - /lp enable/e/1/true  - Enable plugin
    - /lp disable/d/0/false - Disable plugin
    - /lp logging/log/l - Toggle logging on/off
    - /lp status  - Show current config and enable status

For the above, you can type /lp enable OR /lp 1 to enable, etc.

If a player does not own the storage or are not a friend of the owner, they cannot share/unshare it.

If Friends/Clans/Teams support is NOT enabled, players can share/unshare items they own.  If any of those features are enabled, they can still share/unshare to players not in their friend list, etc.

Note that bshare/bunshare are currently bulk commands for share/unshare with all.  In other words they do not currently allow you to be specific about with whom you are (un)sharing.  This is mostly needed for admins sharing items in a town, etc.

### Permissions
  - lootprotect.all - Player overrides all access controls (for admins/moderators/etc.)
  - lootprotect.admin - Player can run the /lp command (for admins/moderators/etc.)
  - lootprotect.share - Player can run the /share and /unshare commands
  - lootprotect.player - Player boxes protected (only if RequirePermission is true)

### Configuration
```json
{
  "Options": {
    "RequirePermission": false,
    "useZoneManager": false,
    "protectedDays": 0,
    "useSchedule": false,
    "useRealTime": true,
    "useFriends": false,
    "useClans": false,
    "useTeams": false,
    "HonorRelationships": false,
    "OverrideOven": false,
    "OverrideTC": false,
    "StartEnabled": true,
    "StartLogging": false,
    "LogToFile": false,
    "AdminBypass": false,
    "BuildingShareRange": 150.0,
    "BShareIncludeSigns": false,
    "BShareIncludeLights": false,
    "BShareIncludeElectrical": false,
    "TCAuthedUserAccess": true,
    "useHammerForShareStatus": false,
    "respondToActivationHooks": false,
    "allowLootingInPVPAreas": false,
    "allowLootingOfflineOwner": false

  },
  "Rules": {
    "bbq.deployed": true,
    "box.wooden.large": true,
    "campfire": true,
    "cursedcauldron.deployed": true,
    "fridge.deployed": true,
    "furnace.large": true,
    "furnace.small": true,
    "mixingtable.deployed": false,
    "player": true,
    "player_corpse": true,
    "fuelstorage": true,
    "hopperoutput": true,
    "recycler_static": false,
    "refinery_small_deployed": false,
    "repairbench_deployed": false,
    "researchtable_deployed": false,
    "woodbox_deployed": true,
    "workbench1.deployed": true,
    "workbench2.deployed": true,
    "workbench3.deployed": true,
    "scientist_corpse": false,
    "murderer_corpse": false,
    "vendingmachine.deployed": false
  },
  "EnabledZones": null,
  "DisabledZones": null,
  "Schedule": "",
  "Version": {
    "Major": 1,
    "Minor": 0,
    "Patch": 32
  }
}
```

#### Global Options
  - `useZoneManager` -- Use ZoneManager to only protect boxes in specified zones or to disable action in specified zones.
  - `protectedDays` -- If set to any value other than zero, player containers will only be protected if the user has been online sometime within that number of days.
  - `useClans` -- Use various Clans plugins for determining relationships.
  - `useFriends` -- Use various Friends plugins for determining relationships.
  - `useTeams` -- Use Rust native teams for determining relationships.
  - `HonorRelationships` -- If set, honor any of the useXXX features to determine ability to access boxes.
  - `OverrideOven` -- Allow access to ovens (campfire, furnace, etc.).  Set this to only protect storage boxes, etc.
  - `OverrideTC` -- Allow access to authenticate on an unlocked TC.
  - `StartEnabled` -- Start plugin in enabled mode (default true).
  - `StartLogging` -- Log all check activity by defaul on plugin load.
  - `LogToFile` -- Log to dated file in oxide/logs/LootProtect folder.  If false, log to oxide log file/rcon.
  - `AdminBypass` -- Allow admins or players with permLootProtAdmin permission to bypass checks for access.
  - `BuildingShareRange` -- Range within which to take action when running bshare/bunshare.
  - `BShareIncludeSigns"` -- Include signs when giving access with bshare/bunshare.
  - `BShareIncludeLights` -- Include lighting fixtures when giving access with bshare/bunshare.
  - `BShareIncludeElectrical` -- Include electric switches, etc.,  when giving access with bshare/bunshare.
  - `TCAuthedUserAccess` -- Player authed to local TC gets access
  - `useHammerForShareStatus` -- Hitting an object with a hammer will display share status.
  - `respondToActivationHooks` -- Enable/disable if EnableMe/DisableMe hooks are called.
  - `allowLootingInPVPAreas` -- If in a NextGenPVE PVP (damage allowed) zone, allow looting.
  - `allowLootingOfflineOwner` -- If true, and protectedDays == 0, any item owned by an offline player can be looted.

#### Rules
  This is a simple list of prefab names and whether or not they will be protected.  Several defaults are included to work with standard storage boxes, furnaces, campfire, etc.

  For each prefab, if true is specified, they will be protected.

  If NOT listed, access will be allowed...

#### ZoneManager (Optional)

  If ZoneManager is loaded, and useZoneManager is true, you can specify enabled or disabled zone ids here.  The default value is:

```json
  "EnabledZones": null,
  "DisabledZones": null,
```

    To set a zone or list of zones, specify them as follows:
```json
  "Zones": [
    "123456"
  ],
```
    Or:

```json
  "Zones": [
    "123456",
    "345678"
  ],
```

#### Schedule (Optional)
  The schedule follows this simple format.  A schedule determines when the plugin is active.  If not set, it is always active.

  FORMAT: DAYOFWEEK_OR_*;START:TIME;END:TIME
      1;1:00:21:00 == Monday between 1AM local time and 9PM local time
      *;4:00;15:00 == Every day between 4AM and 3PM

  Enter your schedule into the config as follows:

```json
  "Schedule": "*;4:00;15:00",
```

##### Schedule flags (global options)

  - `useSchedule` -- Must be true to enable the schedule
  - `useRealTime` -- Use the actual server host clock to determine activity (if false, use in-game time)
