// <mdk sortorder="1000" />
using EmptyKeys.UserInterface.Generated.DataTemplatesContracts_Bindings;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.GameSystems;
using Sandbox.Game.GUI;
using Sandbox.Game.Screens.Helpers;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Media;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Xml.Serialization;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game.VisualScripting.Utils;
using VRageMath;
using VRageRender;
using VRageRender.ExternalApp;

namespace IngameScript
{
	public partial class Program : MyGridProgram
	{
		#region mdk preserve
		public const int maxLogEntries = 20;
		private const int consoleClearAfter = 5;
		#endregion

		public MyIni _ini = new MyIni();
		public enum levels { Debug, Info, Warning, Error }

		#region PB Custom Data
			public string airlockGroupLookup;
			public string innerContains;
			public string outerContains;
			public string statusTextPanelPrefix;
			public double airtightnessCheckDelay;		
			public double airlockCooldown;
			public double alarmRepeatDelay;
			public bool hideAirlockBlocks;
			public levels desiredLogLevel;
			public levels statusPanelLogLevel;
			public bool detectSubgrids;
		#endregion

		public double timer = 0;
		public List<Airlock> airlocks = new List<Airlock>();
		public VRageMath.Color lightColor_OK;
		public VRageMath.Color lightColor_ERR;
		public VRageMath.Color lightColor_CYCLE;
		public CentralMonitors status_monitors;
		private string OutputText = "Run 'setuphelp' for help with setting the script up";
		private double lastModifiedAt = -1;
		private bool lastOutputState = false;

		public readonly Dictionary<levels, string> levelToString = new Dictionary<levels, string>()
		{
			{ levels.Debug, "DEBUG" },
			{ levels.Info, "INFO" },
			{ levels.Warning, "WARN" },
			{ levels.Error, "ERROR" }
		};
		public readonly Dictionary<levels, string> levelColors = new Dictionary<levels, string>()
		{
			{ levels.Debug, "229922" },
			{ levels.Info, "000088" },
			{ levels.Warning, "888800" },
			{ levels.Error, "880000" }
		};
		
		public Program()
		{
			_ini = ReadCustomData(Me);

			airlockGroupLookup = GetIniVal("lookup", "group", "Airlock #", ref _ini, comment:"Group names to look up. \"#\" can mean any number.\nAccepts regex (# gets replaced unless entered as '\\#'). Case sensitive.");
			innerContains = GetIniVal("lookup", "innerContains", "inner", ref _ini, comment:"Script will look for this text in the names of blocks\nUsed to determine the inner side's blocks. Case sensitive.");
			outerContains = GetIniVal("lookup", "outerContains", "outer", ref _ini, comment:"Script will look for this text in the names of blocks\nUsed to determine the outer side's blocks. Case sensitive.");
			if (innerContains == outerContains)
			{
				Echo($"{TextUtils.levelPrefix(this, levels.Error)} You can not assign the same text to both the inner and outer airlock sides.");
				return;
			}
			statusTextPanelPrefix = GetIniVal("lookup", "statusPanelPrefix", "[Airlocks]", ref _ini, comment:"Script will look for this text at the beginning of textpanels' names\nUsed to determine which panels are 'monitoring' panels");
			detectSubgrids = GetIniVal("lookup", "detectSubgrids", false, ref _ini, comment:"Whether to look for airlocks on other grids.\nRecommended to keep on false, unless needed.");

			lightColor_OK = ParseHex(GetIniVal("lights", "colorPressurized", "FFFFFF", ref _ini, comment:"HEX color value used when nothing is wrong"));
			lightColor_ERR = ParseHex(GetIniVal("lights", "colorError", "FF0000", ref _ini, comment:"HEX color value used when there is some sort of error"));
			lightColor_CYCLE = ParseHex(GetIniVal("lights", "colorDepressurized", "FFFF00", ref _ini, comment:"HEX color value used when cycling the airlock"));

			airtightnessCheckDelay = GetIniVal("timing", "airtightnessCheckDelay", 2, ref _ini, comment:"How frequently to look for air leaks");	
			airlockCooldown = GetIniVal("timing", "cooldownSeconds", 2, ref _ini, comment:"Cooldown after finishing a cycle");
			alarmRepeatDelay = GetIniVal("timing", "alarmRepeatDelay", 2, ref _ini, comment:"How frequently to play the alarm");
			
			hideAirlockBlocks = GetIniVal("behavior", "hideAirlockBlocks", false, ref _ini, comment:"If enabled, automatically hides most of the airlock's blocks\nexcludes config blocks\nOnly hides in terminal");

			try
			{
				desiredLogLevel = ConvertToLevel(GetIniVal("behavior", "logLevel", "INFO", ref _ini, comment:"Log level to output. Valid values: 'DEBUG', 'INFO', 'WARNING', 'ERROR'"), throwForFail:true);
			}
			catch (KeyNotFoundException)
			{
				WriteText($"{TextUtils.levelPrefix(this, levels.Warning)} Invalid logging level '{_ini.Get("behavior", "logLevel")}' selected, defaulting to 'INFO'");
				desiredLogLevel = levels.Info;
			}

			try
			{
				statusPanelLogLevel = ConvertToLevel(GetIniVal("behavior", "statusPanelLogLevel", "INFO", ref _ini, comment:"Log level to output to the status panels"), throwForFail:true);
			}
			catch (KeyNotFoundException)
			{
				WriteText($"{TextUtils.levelPrefix(this, levels.Warning)} Invalid logging level '{_ini.Get("behavior", "statusPanelLoglevel")}' selected, defaulting to 'INFO'");
				statusPanelLogLevel = levels.Info;
			}


			List<IMyBlockGroup> blockGroups = new List<IMyBlockGroup>();
			GridTerminalSystem.GetBlockGroups(blockGroups);

			StringBuilder lookupString = new StringBuilder();

			for (int i = 0; i < airlockGroupLookup.Length; i++)
			{
				if (airlockGroupLookup[i] == '#' && i-1 >= 0 && airlockGroupLookup[i-1] != '\\')
				{
					lookupString.Append("([0-9]+)".ToCharArray());
					continue;
				}
				if (airlockGroupLookup[i] == '\\' && airlockGroupLookup.Length >= i+1 && airlockGroupLookup[i+1] == '#') 
					continue;
				lookupString.Append(airlockGroupLookup[i]);
			}

			System.Text.RegularExpressions.Regex nameRegex = new System.Text.RegularExpressions.Regex($"^{lookupString}$");
			foreach (IMyBlockGroup group in blockGroups)
			{
				System.Text.RegularExpressions.Match match = nameRegex.Match(group.Name);
				if (!match.Success)
				{
					continue;
				}

				int airlockIndex;
				if (!int.TryParse(match.Groups[1].Value, out airlockIndex))
				{
					continue;
				}
				if (!detectSubgrids)
				{
					List<IMyTerminalBlock> groupBlocks = new List<IMyTerminalBlock>();
					group.GetBlocks(groupBlocks);
					if (groupBlocks.Any(b => !b.IsSameConstructAs(Me)))
					{
						WriteText($"{TextUtils.levelPrefix(this, levels.Warning)} Group \"{group.Name}\" found, but has blocks on another grid", true);
						continue;
					}
				}
				try
				{
					airlocks.Add(new Airlock(this, group, airlockIndex));
				}
				catch (LookupError e)
				{
					WriteText($"{TextUtils.levelPrefix(this, levels.Error)} {group.Name} | {e.Message}");
				}
			}

			status_monitors = new CentralMonitors(this);
			Runtime.UpdateFrequency = UpdateFrequency.Update100;
		}

		public static levels ConvertToLevel(string text, levels _default = levels.Warning, bool throwForFail = false)
		{
			try
			{
				return new Dictionary<string, levels>
				{
					{ "DEBUG", levels.Debug },
					{ "INFO", levels.Info },
					{ "WARNING", levels.Warning },
					{ "ERROR", levels.Error }
				}[text];
			}
			catch (KeyNotFoundException e)
			{
				if (throwForFail)
					throw e;
				return _default;
			}
		}
		public T GetIniVal<T>(string section, string key, T _default, ref MyIni ini, IMyTerminalBlock block = null, string comment = null)
		{
			if (block == null)
				block = Me;
			if (!ini.ContainsKey(section, key))
			{
				ini = ReadCustomData(block) ;
				ini.Set(section, key, _default.ToString());
				if (comment != null)
					ini.SetComment(section, key, comment); 
				block.CustomData = ini.ToString();
				return _default;
			}
			MyIniValue value = ini.Get(section, key);
			if (_default is bool)
				return (T)(object)value.ToBoolean((bool)(object)_default);
			if (_default is int)
				return (T)(object)value.ToInt32((int)(object)_default);
			if (_default is float)
				return (T)(object)value.ToSingle((float)(object)_default);
			if (_default is double)
				return (T)(object)value.ToDouble((double)(object)_default);
			if (_default is string)
				return (T)(object)value.ToString((string)(object)_default);
			throw new Exception($"Type '{typeof(T)}' not accepted from MyIni");
		} 
		public VRageMath.Color ParseHex(string value)
		{
			value = value.Trim().TrimStart('#');

			if (value.Length != 6)
			{
				throw new ArgumentException(
					$"Expected a 6-digit hex color, but received '{value}'"
				);
			}
			System.Text.RegularExpressions.Regex hexRegex = new System.Text.RegularExpressions.Regex($"^[0-9A-Fa-f]{{6}}$");
			System.Text.RegularExpressions.Match match = hexRegex.Match(value);

			if (!match.Success)
				throw new Exception("Invalid HEX code provided");

			byte red = Convert.ToByte(value.Substring(0, 2), 16);
			byte green = Convert.ToByte(value.Substring(2, 2), 16);
			byte blue = Convert.ToByte(value.Substring(4, 2), 16);

			return new VRageMath.Color(red, green, blue);
		}
		public MyIni ReadCustomData(IMyTerminalBlock block)
		{
			MyIni _ini;
			MyIniParseResult result;
			_ini = new MyIni();
			if (!_ini.TryParse(block.CustomData.Trim(), out result)) 
				throw new Exception($"Could not parse custom data for block '{block.CustomName}'\n{result}");
			return _ini;
		}
		public void WriteText(string text, bool append = false)
		{
			if (append)
			{
				OutputText += "\n" + text;
			}
			else
			{
				OutputText = text;
			}
			lastModifiedAt = timer;
		}
		public void ResetOutput()
		{
			OutputText = "";
			lastModifiedAt = -1;
			lastOutputState = true;
		}
		private void SendOutput()
		{
			if (!string.IsNullOrWhiteSpace(OutputText))
			{
				Echo(OutputText);
				if (lastModifiedAt != -1 && timer - lastModifiedAt >= 60.0*consoleClearAfter) // Console messages stay for X minutes
					ResetOutput();
			}
			else if (lastOutputState)
			{
				Echo("");
				lastOutputState = false;
			}
		}
		public void Save()
		{
			Storage = "";
			foreach (Airlock airlock in airlocks)
			{
				if (!airlock.isActive)
					continue;

				Storage += $"{airlock.group.Name};{(byte)airlock.goal};{(byte)airlock.state};{(byte)airlock.prev_state}\n";
			}
		}
		public void Main(string argument, UpdateType updateSource)
		{
			timer += Runtime.TimeSinceLastRun.TotalSeconds;
			if ((updateSource & (UpdateType.Update100 | UpdateType.Update1)) != 0)
			{
				bool keep_fast = false;
				foreach (Airlock airlock in airlocks)
				{
					try {
						if (airlock.runRoutines() == Airlock.Routine.Result.Running)
							keep_fast = true;
					}
					catch (Exception e) {
						airlock.error($"An exception occurred during airlock runtime: {e.Message}");
					}
				}
				if (!keep_fast)
				{
					Runtime.UpdateFrequency = UpdateFrequency.Update100;
				}
				else
				{
					Runtime.UpdateFrequency = UpdateFrequency.Update1 | UpdateFrequency.Update100;
				}
				SendOutput();
				return;
			}

			string[] arguments = argument.Trim().ToLower().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			try
			{
				new CLI(this, arguments).Run();
			}
			catch (Exception e)
			{
				WriteText($"{TextUtils.levelPrefix(this, levels.Error)} An error occurred while running the CLI\n{e.Message}\n\n{e.StackTrace}");
			}
			SendOutput();
		}
	}
}
