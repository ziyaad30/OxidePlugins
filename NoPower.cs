using System;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Configuration;
using UnityEngine;


namespace Oxide.Plugins
{
    [Info("No Power", "Xavier", "0.0.2")]
	[Description("Allows AutoTurrets and SamSites to function without electricity.")]
	
	public class NoPower : RustPlugin
    {

		static NoPower plugin;
		const string perms = "nopower.use";
		
		private static BuildingPrivlidge buildingPrivlidge;
		
		private bool ConfigChanged;
		private DynamicConfigFile data;
		private StoredData storedData;
		
		private List<ulong> PoweredTurrets = new List<ulong>();
		private List<ulong> PoweredSams = new List<ulong>();
		
		private class StoredData
        {
			public List<ulong> PoweredTurrets = new List<ulong>();
			public List<ulong> PoweredSams = new List<ulong>();
		}
		
		private void SaveData()
        {
			storedData.PoweredTurrets = PoweredTurrets;
			storedData.PoweredSams = PoweredSams;
			data.WriteObject(storedData);
		}
		
		#region Configuration
		BUTTON PowerButton = BUTTON.FIRE_THIRD;
		string Button ="FIRE_THIRD";
		
		void LoadVariables()
        {
			Button = Convert.ToString(GetConfig("Settings","Default Power Button", "FIRE_THIRD"));
			
			if (ConfigChanged)
			{
				SaveConfig();
			}
			else
			{
				ConfigChanged = false;
				return;
			}
		}
		#endregion
		
		#region Config Reader
		private object GetConfig(string menu, string datavalue, object defaultValue)
        {
            var data = Config[menu] as Dictionary<string, object>;
            if (data == null)
            {
                data = new Dictionary<string, object>();
                Config[menu] = data;
                ConfigChanged = true;
            }
            object value;
            if (!data.TryGetValue(datavalue, out value))
            {
                value = defaultValue;
                data[datavalue] = value;
                ConfigChanged = true;
            }
            return value;
        }
		#endregion
		
		#region MessageHelper
		private string msg(string key, string id = null) => lang.GetMessage(key, this, id);
		#endregion
		
		 #region Localization
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
				{"NoPermissions", "You do not permissions to use this."},
				{"NotAuthed", "You are not authed on this AutoTurret."},
				{"NoBuildPrivilege", "You do not have building privilege."},
            }, this, "en");
        }
        #endregion
		
		protected override void LoadDefaultConfig()
		{
			LoadVariables();
		}
		
		void Init()
		{
			LoadVariables();
			permission.RegisterPermission(perms, this);
			
			data = Interface.Oxide.DataFileSystem.GetFile(Name);
			
			try
			{
                storedData = data.ReadObject<StoredData>();
				PoweredTurrets = storedData.PoweredTurrets;
				PoweredSams = storedData.PoweredSams;
			}
            catch
            {
                storedData = new StoredData();
            }
			
			plugin = this;
		}
		
		private void OnServerSave() => SaveData();
		
		void Unload()
        {
			foreach (var autoturret in UnityEngine.Object.FindObjectsOfType<AutoTurret>())
            {
				if (autoturret.IsOnline()) // Turn turrets off on Unload
				{
					autoturret.SetIsOnline(false);
					autoturret.SendNetworkUpdateImmediate();
				}
			}
			
			foreach (var sam in UnityEngine.Object.FindObjectsOfType<SamSite>())
			{
				if (sam.IsPowered()) // Turn sams off on Unload
				{
					sam.UpdateHasPower(0, 1);
					sam.SendNetworkUpdateImmediate();
				}
			}
			
			SaveData();
			plugin = null;
		}
		
		void OnServerInitialized()
		{
			foreach (var autoturret in UnityEngine.Object.FindObjectsOfType<AutoTurret>())
			{
				if (PoweredTurrets.Contains(autoturret.net.ID))
				{
					autoturret.SetIsOnline(true);
					autoturret.SendNetworkUpdateImmediate();
				}
			}
			
			foreach (var sam in UnityEngine.Object.FindObjectsOfType<SamSite>())
			{
				if (PoweredSams.Contains(sam.net.ID))
				{
					sam.UpdateHasPower(25, 1);
					sam.SendNetworkUpdateImmediate();
				}
			}
		}
		
		void OnPlayerInput(BasePlayer player, InputState input)
		{
			TurretInput(input, player);
		}
		
		private static bool IsAuthed(BasePlayer player, BaseEntity entity)
        {
            buildingPrivlidge = entity.GetBuildingPrivilege();
            return buildingPrivlidge != null && entity.GetBuildingPrivilege().authorizedPlayers.Any(x => x.userid == player.userID);
        }
		
		public void TurretInput(InputState input, BasePlayer player)
		{
			if (input == null || player == null) return;
			
			BUTTON.TryParse(Button, out PowerButton);
			
			if (input.WasJustPressed(PowerButton))
			{
				bool hasPermission = permission.UserHasPermission(player.UserIDString, perms);
				if (!hasPermission)
				{
					player.ChatMessage(msg("NoPermissions", player.UserIDString));
					return;
				}
				
				RaycastHit hit;
				if (Physics.SphereCast(player.eyes.position, 0.5f, Quaternion.Euler(player.serverInput.current.aimAngles) * Vector3.forward, out hit))
				{
					AutoTurret autoturret = hit.GetEntity()?.GetComponent<AutoTurret>();
					SamSite samsite = hit.GetEntity()?.GetComponent<SamSite>();
					
					if (autoturret != null)
					{
						if (hit.distance >= 1.5)
							return;
						
						if (!autoturret.IsAuthed(player))
						{
							player.ChatMessage(msg("NotAuthed", player.UserIDString));
							return;
						}
						
						if (!IsAuthed(player, autoturret))
						{
							player.ChatMessage(msg("NoBuildPrivilege", player.UserIDString));
							return;
						}
						
						if (autoturret.IsOnline())
						{
							autoturret.SetIsOnline(false);
							PoweredTurrets.Remove(autoturret.net.ID);
						}
						else
						{
							autoturret.SetIsOnline(true);
							PoweredTurrets.Add(autoturret.net.ID);
						}
						autoturret.SendNetworkUpdateImmediate();
					}
					
					if (samsite != null)
					{
						if (hit.distance >= 1.5)
							return;
						
						if (!IsAuthed(player, samsite))
						{
							player.ChatMessage(msg("NoBuildPrivilege", player.UserIDString));
							return;
						}
						
						if (samsite.IsPowered())
						{
							samsite.UpdateHasPower(0, 1);
							PoweredSams.Remove(samsite.net.ID);
						}
						else
						{
							samsite.UpdateHasPower(25, 1);
							PoweredSams.Add(samsite.net.ID);
						}
						samsite.SendNetworkUpdateImmediate();
					}
				}
			}
		}
	}
	
}
