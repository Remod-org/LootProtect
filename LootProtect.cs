#region License (GPL v2)
/*
    Loot Protection - Prevent access to player containers
    Copyright (c) 2020 RFC1920 <desolationoutpostpve@gmail.com>

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License v2.0.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/
#endregion License (GPL v2)
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Loot Protection", "RFC1920", "1.0.41")]
    [Description("Prevent access to player containers, locks, etc.")]
    internal class LootProtect : RustPlugin
    {
        #region vars
        private Dictionary<string, List<Share>> sharing = new Dictionary<string, List<Share>>();
        private Dictionary<string, long> lastConnected = new Dictionary<string, long>();
        private Dictionary<ulong, ulong> lootingBackpack = new Dictionary<ulong, ulong>();
        private const string permLootProtAdmin = "lootprotect.admin";
        private const string permLootProtAll = "lootprotect.all";
        private const string permLootProtShare = "lootprotect.share";
        private const string permLootProtected = "lootprotect.player";
        private ConfigData configData;
        private bool newsave;

        [PluginReference]
        private readonly Plugin ZoneManager, Friends, Clans, RustIO, NextGenPVE;

        private readonly string logfilename = "log";
        private bool dolog;
        private bool logtofile;
        private bool enabled = true;

        public class Share
        {
            public string name;
            public uint netid;
            public ulong sharewith;
        }
        #endregion vars

        #region Message
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        private void Message(IPlayer player, string key, params object[] args) => player.Message(Lang(key, player.Id, args));
        private void LMessage(IPlayer player, string key, params object[] args) => player.Reply(Lang(key, player.Id, args));
        #endregion Message

        #region init
        private void Init()
        {
            LoadConfigVariables();
            AddCovalenceCommand("lp", "CmdLootProt");
            AddCovalenceCommand("share", "CmdShare");
            AddCovalenceCommand("unshare", "CmdUnShare");
            AddCovalenceCommand("bshare", "CmdShareBuilding");
            AddCovalenceCommand("bunshare", "CmdUnShareBuilding");
            permission.RegisterPermission(permLootProtected, this);
            permission.RegisterPermission(permLootProtAdmin, this);
            permission.RegisterPermission(permLootProtAll, this);
            permission.RegisterPermission(permLootProtShare, this);
            if (configData.Options.useSchedule) RunSchedule(true);
        }

        private void OnServerInitialized() => LoadData();
        private void Unload() => SaveData();

        private void OnHammerHit(BasePlayer player, HitInfo hit)
        {
            if (!configData.Options.useHammerForShareStatus) return;
            BaseEntity ent = hit.HitEntity;
            if (ent != null && sharing.ContainsKey(ent.OwnerID.ToString()))
            {
                string ename = ent.ShortPrefabName;
                if (ename.Equals("cupboard.tool.deployed"))
                {
                    player.SendConsoleCommand("bshare ?");
                }
                else
                {
                    player.SendConsoleCommand("share ?");
                }
            }
        }

        private void OnUserConnected(IPlayer player)
        {
            if (!sharing.ContainsKey(player.Id))
            {
                sharing.Add(player.Id, new List<Share>());
                SaveData();
            }
            OnUserDisconnected(player);
        }

        private void OnUserDisconnected(IPlayer player)
        {
            long lc = 0;
            lastConnected.TryGetValue(player.Id, out lc);
            if (lc > 0)
            {
                lastConnected[player.Id] = ToEpochTime(DateTime.UtcNow);
            }
            else
            {
                lastConnected.Add(player.Id, ToEpochTime(DateTime.UtcNow));
            }
            SaveData();
        }

        // Call out from Backpacks plugin
        private void OnBackpackOpened(BasePlayer looter, ulong OwnerId, ItemContainer _itemContainer)
        {
            if (!lootingBackpack.ContainsKey(looter.userID)) lootingBackpack.Add(looter.userID, OwnerId);
        }

        private void OnBackpackClosed(BasePlayer looter, ulong OwnerId, ItemContainer _itemContainer)
        {
            if (lootingBackpack.ContainsKey(looter.userID)) lootingBackpack.Remove(looter.userID);
        }

        private void OnNewSave()
        {
            newsave = true;
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name + "/sharing", sharing);
            Interface.Oxide.DataFileSystem.WriteObject(Name + "/lastconnected", lastConnected);
        }

        private void LoadData()
        {
            if (newsave)
            {
                newsave = false;
                lastConnected = new Dictionary<string, long>();
                sharing = new Dictionary<string, List<Share>>();
                SaveData();
                return;
            }
            else
            {
                lastConnected = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<string, long>>(Name + "/lastconnected");
                sharing = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<string, List<Share>>>(Name + "/sharing");
            }
            if (sharing == null)
            {
                sharing = new Dictionary<string, List<Share>>();
                SaveData();
            }
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["checkinglocal"] = "[LootProtect] Checking {0} local entities",
                ["enabled"] = "LootProtect enabled.",
                ["disabled"] = "LootProtect disabled.",
                ["status"] = "LootProtect enable is set to {0}.",
                ["logging"] = "Logging set to {0}",
                ["all"] = "all",
                ["friends"] = "friends",
                ["nonefound"] = "No entity found.",
                ["settings"] = "{0} Settings:\n{1}",
                ["shared"] = "{0} shared with {1}.",
                ["sharedf"] = "{0} shared with friends.",
                ["removeshare"] = "Sharing removed.",
                ["removesharefor"] = "Sharing removed for {0} entities.",
                ["shareinfo"] = "Share info for {0}",
                ["lpshareinfo"] = "[LootProtect] Share info for {0}",
                ["notauthorized"] = "You don't have permission to use this command.",
            }, this);
        }

        private void RunSchedule(bool refresh = false)
        {
            TimeSpan ts = new TimeSpan();

            if (configData.Options.useRealTime)
            {
                ts = new TimeSpan((int)DateTime.Now.DayOfWeek, 0, 0, 0).Add(DateTime.Now.TimeOfDay);
            }
            else
            {
                try
                {
                    ts = TOD_Sky.Instance.Cycle.DateTime.TimeOfDay;
                }
                catch
                {
                    DoLog("TOD_Sky failure...");
                    refresh = true;
                }
            }

            Schedule parsed;
            bool doenable = false;
            if (ParseSchedule(configData.Schedule, out parsed))
            {
                foreach (int x in parsed.day)
                {
                    DoLog($"Schedule day == {x} {parsed.dayName[0]} {parsed.starthour} to {parsed.endhour}");
                    if (ts.Days == x)
                    {
                        DoLog($"Day matched.  Comparing {ts.Hours}:{ts.Minutes.ToString().PadLeft(2, '0')} to start time {parsed.starthour}:{parsed.startminute} and end time {parsed.endhour}:{parsed.endminute}");
                        if (ts.Hours >= Convert.ToInt32(parsed.starthour) && ts.Hours <= Convert.ToInt32(parsed.endhour))
                        {
                            // Hours matched for activating ruleset, check minutes
                            DoLog("Hours matched", 1);
                            if (ts.Hours == Convert.ToInt32(parsed.starthour) && ts.Minutes >= Convert.ToInt32(parsed.startminute))
                            {
                                DoLog("Matched START hour and minute.", 2);
                                doenable = true;
                            }
                            else if (ts.Hours == Convert.ToInt32(parsed.endhour) && ts.Minutes <= Convert.ToInt32(parsed.endminute))
                            {
                                DoLog("Matched END hour and minute.", 2);
                                doenable = true;
                            }
                            else if (ts.Hours > Convert.ToInt32(parsed.starthour) && ts.Hours < Convert.ToInt32(parsed.endhour))
                            {
                                DoLog("Between start and end hours.", 2);
                                doenable = true;
                            }
                            else
                            {
                                DoLog("Minute mismatch for START OR END.", 2);
                                doenable = false;
                            }
                        }
                        else
                        {
                            DoLog("Hours NOT matched", 1);
                            doenable = false;
                        }
                    }
                }
            }
            enabled = doenable;

            timer.Once(configData.Options.useRealTime ? 30f : 5f, () => RunSchedule(refresh));
        }
        #endregion init

        #region commands
        [Command("unshare")]
        private void CmdUnShare(IPlayer iplayer, string command, string[] args)
        {
            if (iplayer == null) return;
            if (!iplayer.HasPermission(permLootProtShare) && !iplayer.HasPermission(permLootProtAdmin)) { Message(iplayer, "notauthorized"); return; }

            string debug = string.Join(",", args); DoLog($"{command} {debug}");

            if (!sharing.ContainsKey(iplayer.Id))
            {
                sharing.Add(iplayer.Id, new List<Share>());
            }
            if (args.Length == 0)
            {
                BasePlayer player = iplayer.Object as BasePlayer;
                RaycastHit hit;
                if (Physics.Raycast(player.eyes.HeadRay(), out hit, 2.2f))
                {
                    BaseEntity ent = hit.GetEntity();
                    if (ent != null)
                    {
                        if (ent.OwnerID != player.userID && !IsFriend(player.userID, ent.OwnerID)) return;
                        List<Share> repl = new List<Share>();
                        foreach (Share x in sharing[iplayer.Id])
                        {
                            if (x.netid != (uint)ent.net.ID.Value)
                            {
                                repl.Add(x);
                            }
                            else
                            {
                                DoLog($"Removing {ent.net.ID} from sharing list...");
                            }
                        }
                        sharing[iplayer.Id] = repl;
                        SaveData();
                        LoadData();
                        Message(iplayer, "removeshare");
                    }
                }
            }
        }

        [Command("share")]
        private void CmdShare(IPlayer iplayer, string command, string[] args)
        {
            if (iplayer == null) return;
            if (!iplayer.HasPermission(permLootProtShare) && !iplayer.HasPermission(permLootProtAdmin)) { Message(iplayer, "notauthorized"); return; }

            string debug = string.Join(",", args); DoLog($"{command} {debug}");

            if (!sharing.ContainsKey(iplayer.Id))
            {
                sharing.Add(iplayer.Id, new List<Share>());
            }
            if (args.Length == 0)
            {
                BasePlayer player = iplayer.Object as BasePlayer;
                RaycastHit hit;
                if (Physics.Raycast(player.eyes.HeadRay(), out hit, 2.2f))
                {
                    BaseEntity ent = hit.GetEntity();
                    if (ent != null)
                    {
                        if (ent.OwnerID != player.userID && !IsFriend(player.userID, ent.OwnerID)) return;
                        string ename = ent.ShortPrefabName;
                        sharing[iplayer.Id].Add(new Share { netid = (uint)ent.net.ID.Value, name = ename, sharewith = 0 });
                        SaveData();
                        Message(iplayer, "shared", ename, Lang("all"));
                    }
                }
            }
            else if (args.Length == 1)
            {
                if (args[0] == "?")
                {
                    BasePlayer player = iplayer.Object as BasePlayer;
                    RaycastHit hit;
                    if (Physics.Raycast(player.eyes.HeadRay(), out hit, 2.2f))
                    {
                        BaseEntity ent = hit.GetEntity();
                        string message = "";
                        if (ent != null)
                        {
                            if (ent.OwnerID != player.userID && !IsFriend(player.userID, ent.OwnerID)) return;
                            // SHOW SHARED BY, KEEP IN MIND WHO OWNS BUT DISPLAY IF FRIEND, ETC...
                            if (sharing.ContainsKey(ent.OwnerID.ToString()))
                            {
                                string ename = ent.ShortPrefabName;
                                message += $"{ename}({ent.net.ID}):\n";
                                foreach (Share x in sharing[ent.OwnerID.ToString()])
                                {
                                    if (x.netid != (uint)ent.net.ID.Value) continue;
                                    if (x.sharewith == 0)
                                    {
                                        message += "\t" + Lang("all") + "\n";
                                    }
                                    else if (x.sharewith == 1)
                                    {
                                        message += "\t" + Lang("friends") + "\n";
                                    }
                                    else
                                    {
                                        message += $"\t{x.sharewith}\n";
                                    }
                                }
                                Message(iplayer, "lpshareinfo", message);
                            }
                        }
                        else
                        {
                            Message(iplayer, "nonefound");
                        }
                    }
                }
                else if (args[0] == "friends")
                {
                    if (!configData.Options.HonorRelationships) return;
                    BasePlayer player = iplayer.Object as BasePlayer;
                    RaycastHit hit;
                    if (Physics.Raycast(player.eyes.HeadRay(), out hit, 2.2f))
                    {
                        BaseEntity ent = hit.GetEntity();
                        if (ent != null)
                        {
                            if (ent.OwnerID != player.userID && !IsFriend(player.userID, ent.OwnerID)) return;
                            string ename = ent.ShortPrefabName;
                            sharing[iplayer.Id].Add(new Share { netid = (uint)ent.net.ID.Value, name = ename, sharewith = 1 });
                            SaveData();
                            Message(iplayer, "sharedf", ename);
                        }
                    }
                }
                else
                {
                    BasePlayer sharewith = FindPlayerByName(args[0]);
                    BasePlayer player = iplayer.Object as BasePlayer;
                    RaycastHit hit;
                    if (Physics.Raycast(player.eyes.HeadRay(), out hit, 2.2f))
                    {
                        BaseEntity ent = hit.GetEntity();
                        if (ent != null)
                        {
                            if (ent.OwnerID != player.userID && !IsFriend(player.userID, ent.OwnerID)) return;
                            string ename = ent.ShortPrefabName;
                            if (sharewith == null)
                            {
                                if (!configData.Options.HonorRelationships) return;
                                sharing[iplayer.Id].Add(new Share { netid = (uint)ent.net.ID.Value, name = ename, sharewith = 1 });
                            }
                            else
                            {
                                sharing[iplayer.Id].Add(new Share { netid = (uint)ent.net.ID.Value, name = ename, sharewith = sharewith.userID });
                            }
                            SaveData();
                            Message(iplayer, "shared", ename, sharewith.displayName);
                        }
                    }
                }
            }
        }

        [Command("bunshare")]
        private void CmdUnShareBuilding(IPlayer iplayer, string command, string[] args)
        {
            if (iplayer == null) return;
            if (!iplayer.HasPermission(permLootProtShare) && !iplayer.HasPermission(permLootProtAdmin)) { Message(iplayer, "notauthorized"); return; }
            BasePlayer player = iplayer.Object as BasePlayer;

            string debug = string.Join(",", args); DoLog($"{command} {debug}");

            if (!sharing.ContainsKey(iplayer.Id))
            {
                sharing.Add(iplayer.Id, new List<Share>());
            }
            int found = 0;

            Collider[] hits = Physics.OverlapSphere(player.transform.position, configData.Options.BuildingShareRange, LayerMask.GetMask("Default", "Deployed"));
            List<Share> repl = new List<Share>(sharing[iplayer.Id]);

            DoLog($"Checking {hits.Length} local entities");
            Message(iplayer, "checkinglocal", hits.Length.ToString());
            for (int i = 0; i < hits.Length; i++)
            {
                BaseEntity ent = hits[i].GetComponentInParent<BaseEntity>();
                if (ent == null) continue;
                // Skip actual TC
                if (ent.ShortPrefabName.Equals("cupboard.tool.deployed"))
                {
                    continue;
                }

                if (ent.OwnerID != player.userID && !IsFriend(player.userID, ent.OwnerID)) continue;
                if (ent.GetBuildingPrivilege() == null) continue;

                foreach (Share x in sharing[iplayer.Id])
                {
                    if (x.netid == 0) continue;
                    if (x.netid == (uint)ent.net.ID.Value)
                    {
                        found++;
                        if (repl.Contains(x)) repl.Remove(x);
                        DoLog($"Removing {ent.ShortPrefabName} ({ent.net.ID}) from sharing list...");
                    }
                }
            }

            if (found > 0)
            {
                sharing[iplayer.Id] = repl;
                SaveData();
                LoadData();
                Message(iplayer, "removesharefor", found.ToString());
            }
        }

        private void ShareBuilding(Vector3 position, ulong owner, float range = 0)
        {
            if (range == 0) range = configData.Options.BuildingShareRange;
            if (!sharing.ContainsKey(owner.ToString()))
            {
                sharing.Add(owner.ToString(), new List<Share>());
                SaveData();
            }

            Collider[] hits = Physics.OverlapSphere(position, range, LayerMask.GetMask("Default", "Deployed"));
            List<string> excludestd = new List<string>() { "cupboard.tool.deployed", "doorcloser", "rug.deployed", "shelves", "table.deployed", "spinner.wheel.deployed", "metal-ore", "sulfur-ore", "stone-ore", "loot_barrel_2", "loot-barrel-1", "trash-pile-1" };
            List<string> excludelights = new List<string>() { "ceilinglight.deployed", "tunalight.deployed", "lantern.deployed" };

            DoLog($"Checking {hits.Length} local entities");
            int count = 0;
            List<ulong> populated = new List<ulong>();
            for (int i = 0; i < hits.Length; i++)
            {
                BaseEntity ent = hits[i].GetComponentInParent<BaseEntity>();
                if (ent == null) continue;
                if (string.IsNullOrEmpty(ent.ShortPrefabName)) continue;
                if (owner > 0)
                {
                    ent.OwnerID = owner;
                }
                DoLog($"Checking {ent.ShortPrefabName}, owner: {ent.OwnerID}");

                if (excludestd.Contains(ent.ShortPrefabName))
                {
                    continue;
                }
                else if (excludelights.Contains(ent.ShortPrefabName) && !configData.Options.BShareIncludeLights)
                {
                    continue;
                }
                else if (ent.ShortPrefabName.Contains("sign") && !configData.Options.BShareIncludeSigns)
                {
                    continue;
                }
                else if (ent.ShortPrefabName.Contains("electric") && !configData.Options.BShareIncludeElectrical)
                {
                    continue;
                }

                count++;

                if (populated.Contains((uint)ent.net.ID.Value)) continue;
                populated.Add((uint)ent.net.ID.Value);
                DoLog($"Adding {ent.ShortPrefabName} ({ent.net.ID}) to sharing list...");
                //Message(iplayer, $"Sharing {ent.ShortPrefabName}");
                // Entity under control of TC
                sharing[owner.ToString()].Add(new Share { netid = (uint)ent.net.ID.Value, name = ent.ShortPrefabName, sharewith = 0 });
            }
            DoLog($"Shared {count} entities");
            SaveData();
        }

        [Command("bshare")]
        private void CmdShareBuilding(IPlayer iplayer, string command, string[] args)
        {
            if (iplayer == null) return;
            if (!iplayer.HasPermission(permLootProtShare) && !iplayer.HasPermission(permLootProtAdmin)) { Message(iplayer, "notauthorized"); return; }
            BasePlayer player = iplayer.Object as BasePlayer;

            string debug = string.Join(",", args); DoLog($"{command} {debug}");

            if (!sharing.ContainsKey(iplayer.Id))
            {
                sharing.Add(iplayer.Id, new List<Share>());
            }
            bool query = false;
            if (args.Length == 1 && args[0] == "?")
            {
                query = true;
            }

            string message = "";
            Collider[] hits = Physics.OverlapSphere(player.transform.position, configData.Options.BuildingShareRange, LayerMask.GetMask("Default", "Deployed"));
            List<string> excludestd = new List<string>() { "cupboard.tool.deployed", "doorcloser", "rug.deployed", "shelves", "table.deployed", "spinner.wheel.deployed", "metal-ore", "sulfur-ore", "stone-ore", "loot_barrel_2", "loot-barrel-1", "trash-pile-1" };
            List<string> excludelights = new List<string>() { "ceilinglight.deployed", "tunalight.deployed", "lantern.deployed" };

            DoLog($"Checking {hits.Length} local entities");
            Message(iplayer, "checkinglocal", hits.Length.ToString());
            int count = 0;
            List<ulong> populated = new List<ulong>();
            for (int i = 0; i < hits.Length; i++)
            {
                BaseEntity ent = hits[i].GetComponentInParent<BaseEntity>();
                if (ent == null) continue;
                if (string.IsNullOrEmpty(ent.ShortPrefabName)) continue;
                if (ent.OwnerID == 0) continue;
                DoLog($"Checking {ent.ShortPrefabName}, owner: {ent.OwnerID}");

                if (excludestd.Contains(ent.ShortPrefabName))
                {
                    continue;
                }
                else if (excludelights.Contains(ent.ShortPrefabName) && !configData.Options.BShareIncludeLights)
                {
                    continue;
                }
                else if (ent.ShortPrefabName.Contains("sign") && !configData.Options.BShareIncludeSigns)
                {
                    continue;
                }
                else if (ent.ShortPrefabName.Contains("electric") && !configData.Options.BShareIncludeElectrical)
                {
                    continue;
                }

                // If player has lootprotect admin, allow the share.  Otherwise, check ownership or friend relationship.
                if (!iplayer.HasPermission(permLootProtAdmin) && ent.OwnerID != player.userID && !IsFriend(player.userID, ent.OwnerID))
                {
                    continue;
                }
                if (ent.GetBuildingPrivilege() == null) continue;
                count++;

                if (query)
                {
                    //foreach (Share x in sharing[ent.OwnerID.ToString()])
                    foreach (Share x in sharing[iplayer.Id])
                    {
                        if (x.netid != (uint)ent.net.ID.Value) continue;
                        message += $"{count}. {ent.ShortPrefabName}: ";
                        if (x.sharewith == 0)
                        {
                            message += Lang("all") + "\n";
                        }
                        else
                        {
                            message += $"{x.sharewith}\n";
                        }
                    }
                    continue;
                }

                if (populated.Contains((uint)ent.net.ID.Value)) continue;
                populated.Add((uint)ent.net.ID.Value);
                DoLog($"Adding {ent.ShortPrefabName} ({ent.net.ID}) to sharing list...");
                //Message(iplayer, $"Sharing {ent.ShortPrefabName}");
                // Entity under control of TC
                sharing[iplayer.Id].Add(new Share { netid = (uint)ent.net.ID.Value, name = ent.ShortPrefabName, sharewith = 0 });
            }
            if (query)
            {
                if (!string.IsNullOrEmpty(message))
                {
                    Message(iplayer, message);
                }
                return;
            }
            Message(iplayer, $"Shared {count} entities");
            SaveData();
        }

        [Command("lp")]
        private void CmdLootProt(IPlayer iplayer, string command, string[] args)
        {
            if (iplayer == null) return;
            if (!iplayer.HasPermission(permLootProtAdmin)) { Message(iplayer, "notauthorized"); return; }

            string debug = string.Join(",", args); DoLog($"{command} {debug}");

            if (args.Length == 0)
            {
                ShowStatus(iplayer);
                return;
            }
            switch (args[0])
            {
                case "1":
                case "e":
                case "true":
                case "enable":
                    enabled = true;
                    Message(iplayer, "enabled");
                    break;
                case "0":
                case "d":
                case "false":
                case "disable":
                    enabled = false;
                    Message(iplayer, "disabled");
                    break;
                case "status":
                    ShowStatus(iplayer);
                    break;
                case "l":
                case "log":
                case "logging":
                    dolog = !dolog;
                    Message(iplayer, "logging", dolog.ToString());
                    break;
            }
        }
        #endregion commands

        #region hooks
        private object CanPickupEntity(BasePlayer player, BaseCombatEntity ent)
        {
            if (player == null || ent == null) return null;
            if (player.userID.IsSteamId() != true) return null;
            DoLog($"Player {player.displayName} picking up {ent?.ShortPrefabName}");
            if ((player.IsAdmin || permission.UserHasPermission(player?.UserIDString, permLootProtAdmin)) && configData.Options.AdminBypass) return null;

            if (CheckCupboardAccess(ent, player)) return null;
            if (CanAccess(ent?.ShortPrefabName, player.userID, ent.OwnerID)) return null;
            if (CheckShare(ent, player.userID)) return null;
            return configData.Options.useNextGenPVE && CanLootPVP(ent) ? null : (object)false;
        }

        private object CanPickupLock(BasePlayer player, BaseLock ent)
        {
            if (player == null || ent == null) return null;
            if (player.userID.IsSteamId() != true) return null;
            DoLog($"Player {player.displayName} picking up {ent?.ShortPrefabName}");
            if ((player.IsAdmin || permission.UserHasPermission(player?.UserIDString, permLootProtAdmin)) && configData.Options.AdminBypass) return null;

            if (CheckCupboardAccess(ent, player)) return null;
            if (CanAccess(ent?.ShortPrefabName, player.userID, ent.OwnerID)) return null;
            return CheckShare(ent, player.userID) ? null : (object)true;
        }

        private object CanUpdateSign(BasePlayer player, PhotoFrame sign)
        {
            if (player == null || sign == null) return null;
            if (player.userID.IsSteamId() != true) return null;
            BaseEntity ent = sign.GetComponentInParent<BaseEntity>();
            DoLog($"Player {player.displayName} painting PhotoFrame {ent?.ShortPrefabName}");
            if ((player.IsAdmin || permission.UserHasPermission(player?.UserIDString, permLootProtAdmin)) && configData.Options.AdminBypass) return null;

            if (CheckCupboardAccess(ent, player)) return null;
            if (CanAccess(ent?.ShortPrefabName, player.userID, ent.OwnerID)) return null;
            if (CheckShare(ent, player.userID)) return null;
            return configData.Options.useNextGenPVE && CanLootPVP(ent as BaseCombatEntity) ? null : (object)false;
        }

        private object CanUpdateSign(BasePlayer player, Signage sign)
        {
            if (player == null || sign == null) return null;
            if (player.userID.IsSteamId() != true) return null;
            BaseEntity ent = sign.GetComponentInParent<BaseEntity>();
            DoLog($"Player {player.displayName} painting SIGN {ent?.ShortPrefabName}");
            if ((player.IsAdmin || permission.UserHasPermission(player?.UserIDString, permLootProtAdmin)) && configData.Options.AdminBypass) return null;

            if (CheckCupboardAccess(ent, player)) return null;
            if (CanAccess(ent?.ShortPrefabName, player.userID, ent.OwnerID)) return null;
            if (CheckShare(ent, player.userID)) return null;
            return configData.Options.useNextGenPVE && CanLootPVP(ent as BaseCombatEntity) ? null : (object)false;
        }

        private object CanLootEntity(BasePlayer player, VendingMachine container)
        {
            if (player == null || container == null) return null;
            if (player.userID.IsSteamId() != true) return null;
            BaseEntity ent = container.GetComponentInParent<BaseEntity>();
            if (container.PlayerInfront(player))
            {
                DoLog($"Player {player.displayName} looting front of {ent?.ShortPrefabName} - allowed");
                return null;
            }

            DoLog($"Player {player.displayName} looting VM {ent?.ShortPrefabName}");
            if ((player.IsAdmin || permission.UserHasPermission(player?.UserIDString, permLootProtAdmin)) && configData.Options.AdminBypass) return null;
            if (CheckCupboardAccess(ent, player)) return null;
            if (CanAccess(ent?.ShortPrefabName, player.userID, ent.OwnerID)) return null;
            if (CheckShare(ent, player.userID)) return null;
            return configData.Options.useNextGenPVE && CanLootPVP(ent as BaseCombatEntity) ? null : (object)true;
        }

        private object CanLootEntity(BasePlayer player, StorageContainer container)
        {
            if (player == null || container == null) return null;
            if (player.userID.IsSteamId() != true) return null;
            BaseEntity ent = container?.GetComponentInParent<BaseEntity>();
            if (ent == null) return null;
            DoLog($"Player {player.displayName} looting StorageContainer {ent.ShortPrefabName} owned by {ent.OwnerID}");
            if ((player.IsAdmin || permission.UserHasPermission(player?.UserIDString, permLootProtAdmin)) && configData.Options.AdminBypass)
            {
                DoLog("Admin Bypass");
                return null;
            }
            if (CheckCupboardAccess(ent, player)) return null;
            if (CanAccess(ent?.ShortPrefabName, player.userID, ent.OwnerID)) return null;
            if (CheckShare(ent, player.userID)) return null;
            return configData.Options.useNextGenPVE && CanLootPVP(ent as BaseCombatEntity) ? null : (object)true;
        }

        private bool CanLootPVP(BaseCombatEntity entity)
        {
            object pve = NextGenPVE?.CallHook("IsInPVEZone", entity);
            return pve != null && pve is bool && !(bool)pve ? configData.Options.allowLootingInPVPAreas : false;
        }

        private object CanLootEntity(BasePlayer player, DroppedItemContainer container)
        {
            if (player == null || container == null) return null;
            if (player.userID.IsSteamId() != true) return null;
            BaseEntity ent = container.GetComponentInParent<BaseEntity>();
            DoLog($"Player {player.displayName} looting DroppedItemContainer {ent?.ShortPrefabName}:{container.playerSteamID}");
            if ((player.IsAdmin || permission.UserHasPermission(player?.UserIDString, permLootProtAdmin)) && configData.Options.AdminBypass) return null;

            if (!container.OwnerID.IsSteamId()) return null;
            if (CanAccess(ent?.ShortPrefabName, player.userID, container.playerSteamID)) return null;
            return configData.Options.useNextGenPVE && CanLootPVP(ent as BaseCombatEntity) ? null : (object)true;
        }

        private object CanLootEntity(BasePlayer player, LootableCorpse corpse)
        {
            if (player == null || corpse == null) return null;
            if (player.userID.IsSteamId() != true) return null;
            DoLog($"Player {player.displayName}:{player.UserIDString} looting corpse {corpse.name}:{corpse.playerSteamID}");
            if ((player.IsAdmin || permission.UserHasPermission(player?.UserIDString, permLootProtAdmin)) && configData.Options.AdminBypass) return null;
            if (CanAccess(corpse?.ShortPrefabName, player.userID, corpse.playerSteamID)) return null;
            return configData.Options.useNextGenPVE && CanLootPVP(corpse) ? null : (object)true;
        }

        private object CanRenameBed(BasePlayer player, SleepingBag bag, string bedName)
        {
            if (player == null || bag == null) return null;
            if (player.userID.IsSteamId() != true) return null;
            DoLog($"Player {player.displayName}:{player.UserIDString} renaming SleepingBag {bag.name}:{bag.OwnerID}");
            if ((player.IsAdmin || permission.UserHasPermission(player?.UserIDString, permLootProtAdmin)) && configData.Options.AdminBypass) return null;
            return CanAccess(bag?.ShortPrefabName, player.userID, bag.OwnerID) ? null : (object)true;
        }

        private object CanAssignBed(BasePlayer player, SleepingBag bag, ulong targetPlayerId)
        {
            if (player == null || bag == null) return null;
            if (player.userID.IsSteamId() != true) return null;
            DoLog($"Player {player.displayName}:{player.UserIDString} assigning SleepingBag {bag.name}:{bag.OwnerID}");
            if ((player.IsAdmin || permission.UserHasPermission(player?.UserIDString, permLootProtAdmin)) && configData.Options.AdminBypass) return null;
            return CanAccess(bag?.ShortPrefabName, player.userID, bag.OwnerID) ? null : (object)true;
        }

        private object CanSetBedPublic(BasePlayer player, SleepingBag bag)
        {
            if (player == null || bag == null) return null;
            if (player.userID.IsSteamId() != true) return null;
            DoLog($"Player {player.displayName}:{player.UserIDString} setting SleepingBag {bag.name}:{bag.OwnerID} public");
            if ((player.IsAdmin || permission.UserHasPermission(player?.UserIDString, permLootProtAdmin)) && configData.Options.AdminBypass) return null;
            return CanAccess(bag?.ShortPrefabName, player.userID, bag.OwnerID) ? null : (object)true;
        }

        private object CanLootPlayer(BasePlayer target, BasePlayer player)
        {
            if (player == null || target == null) return null;
            if (player.userID.IsSteamId() != true) return null;
            if (player.userID == target.userID) return null;
            DoLog($"Player {player.displayName}:{player.UserIDString} looting Player {target.displayName}:{target.UserIDString}");
            if ((player.IsAdmin || permission.UserHasPermission(player?.UserIDString, permLootProtAdmin)) && configData.Options.AdminBypass) return null;
            if (CanAccess(target?.ShortPrefabName, player.userID, target.userID)) return null;
            return configData.Options.useNextGenPVE && CanLootPVP(target) ? null : (object)false;
        }

        private object OnOvenToggle(BaseOven oven, BasePlayer player)
        {
            if (player == null || oven == null) return null;
            if (player.userID.IsSteamId() != true) return null;
            DoLog($"Player {player.displayName} toggling {oven?.ShortPrefabName}");
            if (configData.Options.OverrideOven) return null;
            if ((player.IsAdmin || permission.UserHasPermission(player?.UserIDString, permLootProtAdmin)) && configData.Options.AdminBypass) return null;
            if (CheckCupboardAccess(oven, player)) return null;
            if (CanAccess(oven?.ShortPrefabName, player.userID, oven.OwnerID)) return null;
            return CheckShare(oven, player.userID) ? null : (object)true;
        }

        private object OnButtonPress(PressButton button, BasePlayer player)
        {
            if (player == null || button == null) return null;
            if (player.userID.IsSteamId() != true) return null;
            DoLog($"Player {player.displayName} toggling button {button.ShortPrefabName}:{button.OwnerID}");
            if ((player.IsAdmin || permission.UserHasPermission(player?.UserIDString, permLootProtAdmin)) && configData.Options.AdminBypass) return null;

            if (!button.OwnerID.IsSteamId()) return null;
            return CanAccess(button?.ShortPrefabName, player.userID, button.OwnerID) ? null : (object)true;
        }

        private object CanLock(BasePlayer player, BaseLock baseLock)
        {
            if (player == null || baseLock == null) return null;
            if (player.userID.IsSteamId() != true) return null;
            DoLog($"Player {player.displayName} toggling IOEntity {baseLock.ShortPrefabName}:{baseLock.OwnerID}");
            if ((player.IsAdmin || permission.UserHasPermission(player?.UserIDString, permLootProtAdmin)) && configData.Options.AdminBypass) return null;

            if (!baseLock.OwnerID.IsSteamId()) return null;
            return CanAccess(baseLock?.ShortPrefabName, player.userID, baseLock.OwnerID) ? null : (object)true;
        }

        private object CanChangeCode(BasePlayer player, CodeLock codeLock, string newCode, bool isGuestCode)
        {
            if (player == null || codeLock == null) return null;
            if (player.userID.IsSteamId() != true) return null;
            DoLog($"Player {player.displayName} toggling IOEntity {codeLock.ShortPrefabName}:{codeLock.OwnerID}");
            if ((player.IsAdmin || permission.UserHasPermission(player?.UserIDString, permLootProtAdmin)) && configData.Options.AdminBypass) return null;

            if (!codeLock.OwnerID.IsSteamId()) return null;
            return CanAccess(codeLock?.ShortPrefabName, player.userID, codeLock.OwnerID) ? null : (object)true;
        }

        private object OnSwitchToggle(IOEntity entity, BasePlayer player)
        {
            if (player == null || entity == null) return null;
            if (player.userID.IsSteamId() != true) return null;
            DoLog($"Player {player.displayName} toggling IOEntity {entity.ShortPrefabName}:{entity.OwnerID}");
            if ((player.IsAdmin || permission.UserHasPermission(player?.UserIDString, permLootProtAdmin)) && configData.Options.AdminBypass) return null;

            if (!entity.OwnerID.IsSteamId()) return null;
            return CanAccess(entity?.ShortPrefabName, player.userID, entity.OwnerID) ? null : (object)true;
        }

        private object CanTakeCutting(BasePlayer player, GrowableEntity plant)
        {
            if (player == null || plant == null) return null;
            if (player.userID.IsSteamId() != true) return null;
            DoLog($"Player {player.displayName}:{player.UserIDString} looting plant cutting {plant.ShortPrefabName}:{plant.OwnerID}");
            if ((player.IsAdmin || permission.UserHasPermission(player?.UserIDString, permLootProtAdmin)) && configData.Options.AdminBypass) return null;

            if (CanAccess(plant?.ShortPrefabName, player.userID, plant.OwnerID)) return null;
            return configData.Options.useNextGenPVE && CanLootPVP(plant) ? null : (object)true;
        }

        private object OnRemoveDying(GrowableEntity plant, BasePlayer player)
        {
            if (player == null || plant == null) return null;
            if (player.userID.IsSteamId() != true) return null;
            DoLog($"Player {player.displayName}:{player.UserIDString} looting dying plant {plant.ShortPrefabName}:{plant.OwnerID}");
            if ((player.IsAdmin || permission.UserHasPermission(player?.UserIDString, permLootProtAdmin)) && configData.Options.AdminBypass) return null;

            if (CanAccess(plant?.ShortPrefabName, player.userID, plant.OwnerID)) return null;
            return configData.Options.useNextGenPVE && CanLootPVP(plant) ? null : (object)true;
        }

        private object OnGrowableGather(GrowableEntity plant, BasePlayer player)
        {
            if (player == null || plant == null) return null;
            if (player.userID.IsSteamId() != true) return null;
            DoLog($"Player {player.displayName}:{player.UserIDString} looting plant {plant.ShortPrefabName}:{plant.OwnerID}");
            if ((player.IsAdmin || permission.UserHasPermission(player?.UserIDString, permLootProtAdmin)) && configData.Options.AdminBypass) return null;

            if (CanAccess(plant?.ShortPrefabName, player.userID, plant.OwnerID)) return null;
            return configData.Options.useNextGenPVE && CanLootPVP(plant) ? null : (object)true;
        }

        private object OnCupboardAuthorize(BuildingPrivlidge privilege, BasePlayer player)
        {
            if (player == null || privilege == null) return null;
            if (player.userID.IsSteamId() != true) return null;
            DoLog($"Player {player.displayName} attempting to authenticate to a TC.");
            if (configData.Options.OverrideTC) return null;
            if ((player.IsAdmin || permission.UserHasPermission(player?.UserIDString, permLootProtAdmin)) && configData.Options.AdminBypass) return null;

            if (CanAccess(privilege?.ShortPrefabName, player.userID, privilege.OwnerID)) return null;
            if (CheckShare(privilege, player.userID)) return null;
            return configData.Options.useNextGenPVE && CanLootPVP(privilege) ? null : (object)true;
        }
        #endregion hooks

        #region helpers
        // Accept external zone config from DynamicPVP
        private bool AddOrUpdateMapping(string key, string rulesetname)
        {
            if (configData.DisabledZones == null || configData.DisabledZones?.Length == 0)
            {
                List<string> newzones = new List<string> { key };
                configData.DisabledZones = newzones.ToArray();
                SaveConfig(configData);
                return true;
            }
            if (!configData.DisabledZones.Contains(key))
            {
                List<string> newzones = new List<string>
                {
                    configData.DisabledZones.ToArray()[0],
                    key
                };
                configData.DisabledZones = newzones.ToArray();
                SaveConfig(configData);
                return true;
            }
            return false;
        }

        // Accept external zone config from DynamicPVP
        private bool RemoveMapping(string key)
        {
            if (configData.DisabledZones == null || configData.DisabledZones?.Length == 0)
            {
                return true;
            }
            if (configData.DisabledZones?.Length > 0 && configData.DisabledZones.Contains(key))
            {
                List<string> newzones = new List<string>();
                foreach (string zone in configData.DisabledZones)
                {
                    if (zone == key) continue;
                    newzones.Add(zone);
                }
                configData.DisabledZones = newzones.ToArray();
                SaveConfig(configData);
                return true;
            }
            return true;
        }

        // From PlayerDatabase
        private long ToEpochTime(DateTime dateTime)
        {
            DateTime date = dateTime.ToUniversalTime();
            long ticks = date.Ticks - new DateTime(1970, 1, 1, 0, 0, 0, 0).Ticks;
            return ticks / TimeSpan.TicksPerSecond;
        }

        private bool CheckCupboardAccess(BaseEntity entity, BasePlayer player)
        {
            if (!configData.Options.TCAuthedUserAccess) return false;

            BuildingPrivlidge tc = entity.GetBuildingPrivilege();
            if (tc == null)
            {
                DoLog($"CheckCupboardAccess:     Unable to find building privilege in range of {entity.ShortPrefabName}.");
                return false; // NO TC to check...
            }

            foreach (ProtoBuf.PlayerNameID p in tc.authorizedPlayers.ToArray())
            {
                float distance = (float)Math.Round(Vector3.Distance(tc.transform.position, entity.transform.position), 2);
                if (p.userid == player.userID)
                {
                    DoLog($"CheckCupboardAccess:     Found authorized cupboard {distance}m from {entity.ShortPrefabName}!");
                    return true;
                }
            }

            DoLog($"CheckCupboardAccess:     Unable to find authorized cupboard for {entity.ShortPrefabName}.");
            return false;
        }

        private bool CheckShare(BaseEntity target, ulong userid)
        {
            if (target == null) return false;
            if (sharing.ContainsKey(target.OwnerID.ToString()))
            {
                DoLog($"Found entry for {target.OwnerID}");
                foreach (Share x in sharing[target.OwnerID.ToString()])
                {
                    if (x.netid == (uint)target.net.ID.Value && (x.sharewith == userid || x.sharewith == 0))
                    {
                        DoLog($"Found netid {target.net.ID} shared to {userid} or all.");
                        return true;
                    }
                    if (IsFriend(target.OwnerID, userid))
                    {
                        DoLog($"{userid} is friend of {target.OwnerID}");
                        return true;
                    }
                }
            }
            return false;
        }

        // Main access check function
        private bool CanAccess(string prefab, ulong source, ulong target)
        {
            if (!enabled) return true;
            if (string.IsNullOrEmpty(prefab)) return true;
            bool inzone = false;

            object externalCheck = Interface.CallHook("OnLootProtectionCanAccess", new object[] { prefab, source, target });
            if (externalCheck != null && externalCheck is bool && (bool)externalCheck) return true;

            // The following skips a ton of logging if the user has their own backpack open.
            if (lootingBackpack.ContainsKey(source)) return true;

            // Offline looting
            if (configData.Options.allowLootingOfflineOwner && configData.Options.protectedDays == 0 && target.IsSteamId())
            {
                foreach (BasePlayer sleeper in BasePlayer.sleepingPlayerList.Where(x => x.userID == target))
                {
                    DoLog("Allowing access to container owned by offline player");
                    return true;
                }
            }
            else if (configData.Options.protectedDays > 0 && target.IsSteamId())
            {
                long lc = 0;
                lastConnected.TryGetValue(target.ToString(), out lc);
                if (lc > 0)
                {
                    long now = ToEpochTime(DateTime.UtcNow);
                    float days = Math.Abs((now - lc) / 86400);
                    if (days > configData.Options.protectedDays)
                    {
                        DoLog($"Allowing access to container owned by player offline for {configData.Options.protectedDays} days");
                        return true;
                    }
                    else
                    {
                        DoLog($"Owner was last connected {days} days ago and is still protected...");
                        // Move on to the remaining checks...
                    }
                }
            }

            // Online target
            if (configData.Options.RequirePermission && target.IsSteamId())
            {
                BasePlayer tgt = FindPlayerByID(target, false);
                if (!permission.UserHasPermission(tgt?.UserIDString, permLootProtected)) return true;
            }

            BasePlayer player = FindPlayerByID(source, false);
            if (player == null) return true;

            if (configData.Options.useZoneManager)
            {
                if (configData.EnabledZones?.Length == 0 && configData.DisabledZones?.Length == 0)
                {
                    DoLog("Admin set useZoneManager but didn't list any zones...");
                    inzone = true;
                }
                else
                {
                    // Check that the player (source) is in a zone where we are configured to take action.
                    // If no zones are set, this will be skipped altogether.
                    string[] pzones = GetPlayerZones(player);

                    if (configData.EnabledZones?.Length > 0 && pzones?.Length > 0)
                    {
                        // Compare player's zones to our zone list
                        foreach (string z in pzones)
                        {
                            if (configData.EnabledZones.Contains(z))
                            {
                                DoLog($"Player {player.displayName} is in zone {z}, which we control.");
                                inzone = true;
                                break;
                            }
                        }
                    }
                    if (configData.DisabledZones?.Length > 0 && pzones?.Length > 0)
                    {
                        inzone = true;
                        // Compare player's zones to our disabled zone list
                        foreach (string z in pzones)
                        {
                            if (configData.DisabledZones.Contains(z))
                            {
                                DoLog($"Player {player?.displayName} is in zone {z}, which we have disabled.");
                                inzone = false;
                                break;
                            }
                        }
                    }
                }
                if (!inzone)
                {
                    DoLog($"Player {player?.displayName} not in a zone we control, or in a zone we have disabled.");
                    return true;
                }
            }

            DoLog($"Checking access to {prefab}");
            //if (target == 0)
            if (!target.IsSteamId())
            {
                DoLog("Not owned by a real player.  Access allowed.");
                return true;
            }
            if (source == target)
            {
                DoLog("Player-owned.  Access allowed.");
                return true;
            }
            if (IsFriend(source, target))
            {
                DoLog("Friend-owned.  Access allowed.");
                return true;
            }

            if (permission.UserHasPermission(player?.UserIDString, permLootProtAll))
            {
                DoLog("User has ALL permission.  Access allowed.");
                return true;
            }

            // Check protection rules since there is no relationship to the target owner.
            if (configData.Rules.ContainsKey(prefab))
            {
                if (configData.Rules[prefab])
                {
                    DoLog($"Rule found for type {prefab}.  Access BLOCKED!");
                    return false;
                }
                DoLog($"Rule found for type {prefab}.  Access allowed.");
                return true;
            }

            return false;
        }

        private static BasePlayer FindPlayerByName(string name)
        {
            BasePlayer result = null;
            foreach (BasePlayer current in BasePlayer.activePlayerList)
            {
                if (current.displayName.Equals(name, StringComparison.OrdinalIgnoreCase)
                    || current.UserIDString.Contains(name, CompareOptions.OrdinalIgnoreCase)
                    || current.displayName.Contains(name, CompareOptions.OrdinalIgnoreCase))
                {
                    result = current;
                }
            }
            return result;
        }

        private BasePlayer FindPlayerByID(ulong userid, bool includeSleepers = true)
        {
            foreach (BasePlayer activePlayer in BasePlayer.activePlayerList)
            {
                if (activePlayer.userID == userid)
                {
                    return activePlayer;
                }
            }
            if (includeSleepers)
            {
                foreach (BasePlayer sleepingPlayer in BasePlayer.sleepingPlayerList)
                {
                    if (sleepingPlayer.userID == userid)
                    {
                        return sleepingPlayer;
                    }
                }
            }
            return null;
        }

        private bool ParseSchedule(string dbschedule, out Schedule parsed)
        {
            int day = 0;
            parsed = new Schedule();

            string[] lootprotschedule = Regex.Split(dbschedule, @"(.*)\;(.*)\:(.*)\;(.*)\:(.*)");
            if (lootprotschedule.Length < 6) return false;

            parsed.starthour = lootprotschedule[2];
            parsed.startminute = lootprotschedule[3];
            parsed.endhour = lootprotschedule[4];
            parsed.endminute = lootprotschedule[5];

            parsed.day = new List<int>();
            parsed.dayName = new List<string>();

            string tmp = lootprotschedule[1];
            string[] days = tmp.Split(',');

            if (tmp == "*")
            {
                for (int i = 0; i < 7; i++)
                {
                    parsed.day.Add(i);
                    parsed.dayName.Add(Enum.GetName(typeof(DayOfWeek), i));
                }
            }
            else if (days.Length > 0)
            {
                foreach (string d in days)
                {
                    int.TryParse(d, out day);
                    parsed.day.Add(day);
                    parsed.dayName.Add(Enum.GetName(typeof(DayOfWeek), day));
                }
            }
            else
            {
                int.TryParse(tmp, out day);
                parsed.day.Add(day);
                parsed.dayName.Add(Enum.GetName(typeof(DayOfWeek), day));
            }
            return true;
        }

        private void ShowStatus(IPlayer player)
        {
            string settings = "";
            foreach (FieldInfo field in typeof(Options).GetFields())
            {
                settings += "\t" + field.Name + ": " + field.GetValue(configData.Options) + "\n";
            }
            settings += Lang("status", null, enabled.ToString());
            Message(player, "settings", Title, settings);
        }

        private void DisableMe()
        {
            if (!configData.Options.respondToActivationHooks) return;
            enabled = false;
            Puts($"{Name} disabled");
        }
        private void EnableMe()
        {
            if (!configData.Options.respondToActivationHooks) return;
            enabled = true;
            Puts($"{Name} enabled");
        }

        private bool IsFriend(ulong playerid, ulong ownerid)
        {
            if (!configData.Options.HonorRelationships) return false;

            if (configData.Options.useFriends && Friends != null)
            {
                object fr = Friends?.CallHook("AreFriends", playerid, ownerid);
                if (fr != null && (bool)fr)
                {
                    return true;
                }
            }
            if (configData.Options.useClans && Clans != null)
            {
                string playerclan = (string)Clans?.CallHook("GetClanOf", playerid);
                string ownerclan = (string)Clans?.CallHook("GetClanOf", ownerid);
                if (playerclan != null && ownerclan != null && playerclan == ownerclan)
                {
                    return true;
                }
            }
            if (configData.Options.useTeams)
            {
                RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindPlayersTeam(playerid);
                if (playerTeam?.members.Contains(ownerid) == true)
                {
                    return true;
                }
            }
            return false;
        }

        private string[] GetPlayerZones(BasePlayer player)
        {
            if (player == null) return null;
            if (player.userID.IsSteamId() != true) return null;
            return ZoneManager && configData.Options.useZoneManager
                ? (string[])ZoneManager?.Call("GetPlayerZoneIDs", new object[] { player })
                : null;
        }

        private string[] GetEntityZones(BaseEntity entity)
        {
            if (entity == null) return null;
            return ZoneManager && configData.Options.useZoneManager && entity.IsValid()
                ? (string[])ZoneManager?.Call("GetEntityZoneIDs", new object[] { entity })
                : null;
        }

        private void DoLog(string message, int indent = 0)
        {
            if (!enabled) return;
            if (dolog)
            {
                if (logtofile) LogToFile(logfilename, "".PadLeft(indent, ' ') + message, this);
                else Puts(message);
            }
        }
        #endregion helpers

        #region config
        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();

            enabled = configData.Options.StartEnabled;
            dolog = configData.Options.StartLogging;
            logtofile = configData.Options.LogToFile;

            if (configData.Version < new VersionNumber(1, 0, 2))
            {
                configData.Rules.Add("fuelstorage", true);
                configData.Rules.Add("hopperoutput", true);
            }
            if (configData.Version < new VersionNumber(1, 0, 8))
            {
                configData.Rules.Add("scientist_corpse", false);
            }
            if (configData.Version < new VersionNumber(1, 0, 9))
            {
                configData.Rules.Add("murderer_corpse", false);
            }
            if (configData.Version < new VersionNumber(1, 0, 10))
            {
                if (!configData.Rules.ContainsKey("vendingmachine.deployed"))
                {
                    configData.Rules.Add("vendingmachine.deployed", false);
                }
            }
            if (configData.Version < new VersionNumber(1, 0, 15))
            {
                configData.Rules.Add("sign.small.wood", true);
                configData.Rules.Add("sign.medium.wood", true);
                configData.Rules.Add("sign.large.wood", true);
                configData.Rules.Add("sign.huge.wood", true);
                configData.Rules.Add("sign.pictureframe.landscape", true);
                configData.Rules.Add("sign.pictureframe.portrait", true);
            }
            if (configData.Version < new VersionNumber(1, 0, 16))
            {
                configData.Rules.Add("lock.code", true);
                configData.Rules.Add("lock.key", true);
            }
            if (configData.Version < new VersionNumber(1, 0, 24))
            {
                configData.EnabledZones = configData.Zones;
                configData.Zones = null;
            }
            if (configData.Version < new VersionNumber(1, 0, 30))
            {
                configData.Options.useHammerForShareStatus = false;
            }
            if (configData.Version < new VersionNumber(1, 0, 31))
            {
                configData.Options.useNextGenPVE = false;
                configData.Options.allowLootingInPVPAreas = false;
            }
            configData.EnabledZones ??= new string[0];
            configData.DisabledZones ??= new string[0];

            configData.Version = Version;
            SaveConfig(configData);
        }

        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file.");
            ConfigData config = new ConfigData
            {
                Options = new Options()
                {
                    RequirePermission = false,
                    protectedDays = 0f,
                    useZoneManager = false,
                    useSchedule = false,
                    useRealTime = false,
                    useFriends = false,
                    useNextGenPVE = false,
                    useClans = false,
                    useTeams = false,
                    HonorRelationships = false,
                    OverrideOven = false,
                    OverrideTC = false,
                    StartEnabled = true,
                    StartLogging = false,
                    LogToFile = false,
                    AdminBypass = false,
                    BuildingShareRange = 150f,
                    BShareIncludeSigns = false,
                    BShareIncludeLights = false,
                    BShareIncludeElectrical = false,
                    TCAuthedUserAccess = false,
                    useHammerForShareStatus = false,
                    respondToActivationHooks = false,
                    allowLootingInPVPAreas = false,
                    allowLootingOfflineOwner = false
                },
                Rules = new Dictionary<string, bool>
                {
                    { "box.wooden.large", true },
                    { "button", true },
                    { "item_drop_backpack", true },
                    { "woodbox_deployed", true },
                    { "bbq.deployed",     true },
                    { "fridge.deployed",  true },
                    { "workbench1.deployed", true },
                    { "workbench2.deployed", true },
                    { "workbench3.deployed", true },
                    { "cursedcauldron.deployed", true },
                    { "campfire",      true },
                    { "furnace.small", true },
                    { "furnace.large", true },
                    { "player",        true },
                    { "player_corpse", true },
                    { "scientist_corpse", false },
                    { "murderer_corpse", false },
                    { "fuelstorage", true },
                    { "hopperoutput", true },
                    { "recycler_static", false },
                    { "sign.small.wood", true},
                    { "sign.medium.wood", true},
                    { "sign.large.wood", true},
                    { "sign.huge.wood", true},
                    { "sign.pictureframe.landscape", true},
                    { "sign.pictureframe.portrait", true},
                    { "sign.hanging", true},
                    { "sign.pictureframe.tall", true},
                    { "sign.pictureframe.xl", true},
                    { "sign.pictureframe.xxl", true},
                    { "repairbench_deployed", false },
                    { "refinery_small_deployed", false },
                    { "researchtable_deployed", false },
                    { "mixingtable.deployed", false },
                    { "vendingmachine.deployed", false },
                    { "lock.code", true },
                    { "lock.key", true },
                    { "abovegroundpool.deployed", true },
                    { "paddlingpool.deployed", true }
                },
                Version = Version,
                Schedule = ""
            };

            SaveConfig(config);
        }

        private void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }

        public class ConfigData
        {
            public Options Options;
            public Dictionary<string, bool> Rules = new Dictionary<string, bool>();
            public string[] Zones;
            public string[] EnabledZones;
            public string[] DisabledZones;
            public string Schedule;
            public VersionNumber Version;
        }

        public class Options
        {
            public bool RequirePermission;
            public float protectedDays;
            public bool useZoneManager;
            public bool useSchedule;
            public bool useRealTime;
            public bool useFriends;
            public bool useClans;
            public bool useTeams;
            public bool useNextGenPVE;
            public bool HonorRelationships;
            public bool OverrideOven;
            public bool OverrideTC;
            public bool StartEnabled;
            public bool StartLogging;
            public bool LogToFile;
            public bool AdminBypass;
            public float BuildingShareRange;
            public bool BShareIncludeSigns;
            public bool BShareIncludeLights;
            public bool BShareIncludeElectrical;
            public bool TCAuthedUserAccess;
            public bool useHammerForShareStatus;
            public bool respondToActivationHooks;
            public bool allowLootingInPVPAreas;
            public bool allowLootingOfflineOwner;
        }

        public class Schedule
        {
            public List<int> day;
            public List<string> dayName;
            public string starthour;
            public string startminute;
            public string endhour;
            public string endminute;
            public bool enabled = true;
        }
        #endregion config
    }
}
