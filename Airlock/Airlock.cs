// <mdk sortorder="999" />
using EmptyKeys.UserInterface.Generated.DataTemplatesContracts_Bindings;
using Sandbox.Game;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.GameSystems;
using Sandbox.Game.GUI;
using Sandbox.Game.Lights;
using Sandbox.Game.Screens;
using Sandbox.Game.Screens.Helpers;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.CodeDom;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Configuration;
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
using System.Threading;
using System.Windows.Markup;
using System.Xml.Serialization;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game.VisualScripting.Utils;
using VRageMath;
using VRageRender;

namespace IngameScript
{
    public class LookupError: Exception
	{
		public LookupError() {}
		public LookupError(string message) : base(message) {}
	}
    public class Airlock
	{
		public enum States { Idle, WaitingForDoorClose, Cycling, WaitingForDoorOpen, Delay, Cooldown }
		public enum LightColor { OK, Cycle, Error }
		public enum RequestID { Cycle, Alarm, LeakDetection, Maintenance, RunCycles }
		#region mdk preserve
		public enum Goals { None, Depressurize, Pressurize }
		#endregion
		public class LightEntry
		{
			public LightColor color;
			public int blinkPercent;
			public LightEntry(LightColor color, int blinkPercent = 100)
			{
				this.color = color;
				this.blinkPercent = blinkPercent;
			}
		}
		public class AirlockCore
		{
			public enum OxygenBalance { High, OK, Low }
			private List<IMyGasTank> oxygen_tanks = new List<IMyGasTank>();
			private List<IMyAirVent> vents = new List<IMyAirVent>();
			private List<IMyLightingBlock> lights = new List<IMyLightingBlock>();
			private List<Display> textPanels = new List<Display>();
			private List<IMySoundBlock> soundBlocks = new List<IMySoundBlock>();
			private Dictionary<RequestID, LightEntry> lightLock = new Dictionary<RequestID, LightEntry>();
			private Dictionary<RequestID, bool> alarmLock = new Dictionary<RequestID, bool>();
			public Routine alarmRoutine;

			private readonly IMyAirVent mainVent;
			private readonly Airlock airlock;
			private readonly Program program;

			public AirlockCore(Airlock airlock, Program program)
			{
				this.airlock = airlock;
				this.program = program;

				#region Vents
				airlock.group.GetBlocksOfType(vents, v => {
					string tmp = v.CustomName.Trim();
					return !tmp.Contains(program.innerContains) && !tmp.Contains(program.outerContains);
				});
				if (vents.Count <= 0)
				{
					throw new Exception($"Could not find any airlock vents in group '{airlock.group.Name}'.");
				}

				if (!vents.Any(v => v.CustomName.ToLowerInvariant().Contains("[main]")))
				{
					IMyAirVent vent = vents.First();
					vent.CustomName = $"[Main] {vent.CustomName}";
				}
				mainVent = vents.First(v => v.CustomName.ToLower().Contains("[main]"));
				mainVent.ShowInTerminal = true;

				vents.ForEach(v => {
					airlock.addAirlockIndex(v);
					if (program.hideAirlockBlocks && !v.CustomName.ToLower().Contains("[main]"))
						v.ShowInTerminal = false;
				});
				#endregion
				#region Read Custom Data
					MyIni _ini = program.ReadCustomData(mainVent);

					airlock.intakeOxygenAtLevel = program.GetIniVal("cycling", "intakeOxygenAtLevel", 0.1, ref _ini, mainVent, "Airlock will intake oxygen at this level from the inner parts of the ship if oxygen falls below this level. Intakes until the level hits value+10%");
					airlock.dumpOxygenAtLevel = program.GetIniVal("cycling", "dumpOxygenAtLevel", 0.9, ref _ini, mainVent, "Airlock will dump a room full of oxygen if the airlock's oxygen tanks are above this level");
					airlock.airlockTolerance = program.GetIniVal("cycling", "oxygenLevelTolerance", 0.05, ref _ini, mainVent, "Airlock cycle will finish if the current oxygen amount is off by max this much.");

					airlock.enableBypassTolerance = program.GetIniVal("behavior", "enableBypassTolerance", -1.0, ref _ini, mainVent, "Percentage difference allowed for automatically enabling bypass. Setting to a value below 0 disables bypassing. WARNING: Enabling bypassing also enables the possibility of a single leak removing ALL oxygen from the grid");
					airlock.cooldownSeconds = program.GetIniVal("behavior", "cooldownSeconds", program.airlockCooldown, ref _ini, mainVent, "Seconds of cooldown after use. If set to -1 it will inherit from the global config.");

				#endregion

				#region Lights
				airlock.group.GetBlocksOfType(lights, l => {
					string tmp = l.CustomName.Trim();
					return !tmp.Contains(program.innerContains) && !tmp.Contains(program.outerContains);
				});
				lights.ForEach(l => {;
					if (program.hideAirlockBlocks)
						l.ShowInTerminal = false;
					airlock.addAirlockIndex(l);
					l.Color = program.lightColor_OK;
				});
				#endregion
				#region Text panels
				List<IMyTextPanel> panels = new List<IMyTextPanel>();
				airlock.group.GetBlocksOfType(panels);
				panels.ForEach(p => {;
					if (program.hideAirlockBlocks)
						p.ShowInTerminal = false;

					textPanels.Add(new Display(program, p));

					airlock.addAirlockIndex(p);
					p.ContentType = ContentType.TEXT_AND_IMAGE;
				});
				#endregion
				#region Sound blocks
				airlock.group.GetBlocksOfType(soundBlocks);
				soundBlocks.ForEach(sb => {
					airlock.addAirlockIndex(sb);
					if (program.hideAirlockBlocks)
						sb.ShowInTerminal = false;
					if (sb.SelectedSound == "")
						sb.SelectedSound = "Alert 2";
				});
				#endregion
				#region Oxygen tanks
				airlock.group.GetBlocksOfType(oxygen_tanks);
				if (oxygen_tanks.Count <= 0)
				{
					throw new LookupError("Could not find any airlock tanks.");
				} 
				oxygen_tanks.ForEach(t => {
					airlock.addAirlockIndex(t);
					if (program.hideAirlockBlocks)
						t.ShowInTerminal = false;
				});
				#endregion

				alarmRoutine = new Routine(airlock, alarmCore(), "alarm_routine");

				#region Debug output
				airlock.debug($"- Group index: {airlock.index}");
				airlock.debug($"- Vents: {vents.Count}");
				airlock.debug($"- Lights: {lights.Count}");
				airlock.debug($"- Oxygen tanks: {oxygen_tanks.Count}");
				airlock.debug($"- Text panels: {textPanels.Count}");
				airlock.debug($"- Sound blocks: {soundBlocks.Count}");
				#endregion
			}
			public void SetLights(LightColor color, RequestID id, int blinkPercent = 100)
			{
				if (!lightLock.ContainsKey(id))
					lightLock.Add(id, new LightEntry(color, blinkPercent));
				else
					lightLock[id] = new LightEntry(color, blinkPercent);

				VRageMath.Color vrageColor = airlock.colorEnumDict[lightLock.Values.Max(e => e.color)];
				blinkPercent = lightLock.Values.Min(e => e.blinkPercent);

				foreach (IMyLightingBlock light in lights)
				{
					light.Color = vrageColor;
					light.BlinkLength = blinkPercent;
				}
			}
			public void Depressurize() => 
				vents.ForEach(v => {
					v.Depressurize = true;
					v.Enabled = true;
				});
			public void Pressurize() => 
				vents.ForEach(v => {
					v.Depressurize = false;
					v.Enabled = true;
				});
			public void DisableVents() => vents.ForEach(v => v.Enabled = false);
			public void WriteLogs()
			{
				string[] logs = airlock.getLogs(false, program.desiredLogLevel);
				textPanels.ForEach(p => p.WriteText(logs));
			}
			public double GetOxygenLevel() => oxygen_tanks.Average(t => t.FilledRatio);
			public bool CanPressurize() => vents.All(v => {
				bool prevState = v.Enabled;
				v.Enabled = true;
				bool result = v.CanPressurize;
				v.Enabled = prevState;
				return result;
			});
			public OxygenBalance SetOxygenLevel(double targetOxygenLevel)
			{
				float oxyLevel = mainVent.GetOxygenLevel();
				if (oxyLevel > targetOxygenLevel + airlock.airlockTolerance)
				{
					Depressurize();
					return OxygenBalance.High;
				}
				else if (oxyLevel < targetOxygenLevel - airlock.airlockTolerance)
				{
					Pressurize();
					return OxygenBalance.Low;
				}
				airlock.info($"Target oxygen level reached ({Math.Round(mainVent.GetOxygenLevel()*100, 2)}%)");
				DisableVents();
				return OxygenBalance.OK;
			}
			private enum alarmStateEnum { Alarm, Maintenance, OK }
			public void setAlarm(RequestID id, bool value)
			{
				if (!alarmLock.ContainsKey(id))
					alarmLock.Add(id, value);
				else
					alarmLock[id] = value;
			}
			public bool getAlarmStatus() => alarmLock.Any(kvp => kvp.Value);
			private IEnumerator<double> alarmCore()
			{
				alarmStateEnum prev_state = alarmStateEnum.OK; // Used to prevent log duplication
				while (true)
				{
					bool active_alarm = alarmLock.Any(kvp => kvp.Value);
					if (active_alarm && !airlock.maintenance)
					{
						airlock.debug("Sounding alarm", prev_state != alarmStateEnum.Alarm);
						foreach (IMySoundBlock sound in soundBlocks)
						{
							sound.Enabled = true;
							sound.Play();
						}
						SetLights(LightColor.Error, RequestID.Alarm, 50);
						prev_state = alarmStateEnum.Alarm;
					}
					else if (active_alarm && airlock.maintenance)
					{
						airlock.debug("Skipping alarm due to active maintenance", prev_state != alarmStateEnum.Maintenance);
						SetLights(LightColor.Cycle, RequestID.Alarm, 50);
						prev_state = alarmStateEnum.Maintenance;
					}
					else
					{
						airlock.debug("Alarm over", send: prev_state != alarmStateEnum.OK);
						foreach (IMySoundBlock sound in soundBlocks)
						{
							sound.Stop();
							sound.Enabled = false;
						}
						SetLights(LightColor.OK, RequestID.Alarm);
						prev_state = alarmStateEnum.OK;
					}
					yield return program.alarmRepeatDelay;
				}
			}
		
		}
		public class AirlockSide
		{
			public class SideVent
			{
				private readonly IMyAirVent vent;
				public bool ignoreAirtightness;
				public bool useForOxygenIntake;
				public bool connectedToMain;
				
				public SideVent(IMyAirVent vent, bool ignoreAirtightness, bool useForOxygenIntake, bool connectedToMain)
				{
					this.vent = vent;
					this.ignoreAirtightness = ignoreAirtightness;
					this.useForOxygenIntake = useForOxygenIntake;
					this.connectedToMain = connectedToMain;
				}
				public float GetOxygenLevel() => vent.GetOxygenLevel();
				public bool CanPressurize => vent.CanPressurize;
				public bool Depressurize
				{
					get { return vent.Depressurize; }
					set { vent.Depressurize = value; }
				}
			}
			public IMyDoor door;
			public SideVent vent;
			public List<IMyLightingBlock> status_lights;
			public bool openDuringLeak;
			public double closeAfter;
			public bool doorUseForOxygenIntake;
			public bool useForOxygenDump;
			private Airlock airlock;
			private Dictionary<RequestID, LightEntry> lightLock = new Dictionary<RequestID, LightEntry>();

			public AirlockSide(Airlock airlock, IMyDoor door, IMyAirVent vent, List<IMyLightingBlock> lights, Program program, MyIni ini)
			{
				this.door = door;
				this.airlock = airlock;
				status_lights = lights;

				openDuringLeak = program.GetIniVal("door", "openDuringLeak", false, ref ini, door, "Open this side during an air leak");
				closeAfter = program.GetIniVal("door", "closeAfter", -1, ref ini, door, "Close this side after this many seconds. -1 makes it stay open");
				doorUseForOxygenIntake = program.GetIniVal("door", "useForOxygenIntake", false, ref ini, door, "Use this door for intaking oxygen. If the vent is set to intake, and it can intake air, the door won't be used.");
				useForOxygenDump = program.GetIniVal("door", "useForOxygenDump", false, ref ini, door, "Use this door for dumping oxygen");
				bool ventignoreAirtightness = program.GetIniVal("vent", "ignoreAirtightness", false, ref ini, door, "Ignore the airtightness of the side's vent. Set to true if the vent is placed outside the ship, or somewhere that can suffer from depressurization, like a hangar");
				bool ventUseForOxygenIntake = program.GetIniVal("vent", "useForOxygenIntake", true, ref ini, door, "Use this vent for intaking oxygen");
				bool ventConnectedToMain = program.GetIniVal("vent", "connectedToAirlock", true, ref ini, door, "Whether the side's vent is actually connected to the airlock's vents");
				
				this.vent = new SideVent(vent, ventignoreAirtightness, ventUseForOxygenIntake, ventConnectedToMain);

				if (!door.CustomName.Contains($"[{airlock.index}]"))
				{
					door.CustomName = $"[{airlock.index}] {door.CustomName}";
				}
				foreach (IMyLightingBlock light in status_lights)
				{
					airlock.addAirlockIndex(light);
				}
				
			}
			private IEnumerator<double> waitForDoorCore(DoorStatus status, bool disableAfter)
			{
				if (door.Status == status)
				{
					door.Enabled = !disableAfter;
					yield break;
				}
				if (door.Closed)
					throw new Exception("Selected door doesn't exist anymore");
				door.Enabled = true;
				yield return 0.005;
				if (!door.IsWorking)
					throw new Exception("Selected door is non-functional");
				
				if (status == DoorStatus.Open)
					door.OpenDoor();
				else if (status == DoorStatus.Closed)
					door.CloseDoor();
				else
				{
					airlock.error($"Door status '{status}' not supported");
					door.Enabled = false;
					yield break;
				}
				DoorStatus interimStatus = status == DoorStatus.Open ? DoorStatus.Opening : DoorStatus.Closing; 

				yield return 0.2;
				while (door.Status != status)
				{
					if (door.Status != interimStatus)
					{
						if (!door.Enabled)
						{
							airlock.info("Door was disabled. Reenabling.");
							door.Enabled = true;
							yield return 0.1;
							continue;	
						}
						airlock.warning("Door was blocked. Retrying in 1 second.");
						yield return 1;
						door.ToggleDoor();
					}
					yield return 0.2;
				}
				door.Enabled = !disableAfter;
				yield return 0.1;
				yield break;
			}
			public Routine waitForDoor(DoorStatus status, bool disableAfter = true, double timeout = -1)
			{
				return new Routine(airlock, waitForDoorCore(status, disableAfter), "doorWait", timeout);
			}
			public void SetStatusLight(LightColor color, RequestID id, int blinkPercent = 100)
			{
				if (status_lights.Count <= 0)
					return;

				if (!lightLock.ContainsKey(id))
					lightLock.Add(id, new LightEntry(color, blinkPercent));
				else
					lightLock[id] = new LightEntry(color, blinkPercent);

				
				VRageMath.Color vrageColor = airlock.colorEnumDict[lightLock.Values.Max(e => e.color)];
				blinkPercent = lightLock.Values.Min(e => e.blinkPercent);

				status_lights.ForEach(l =>
				{
					l.Color = vrageColor;
					l.BlinkLength = blinkPercent;
				});
			}
		}
		public class Routine
		{
			#region mdk preserve
			public enum Result { Running, Success, Failed, TimedOut }
			#endregion
			private IEnumerator<double> routine;
			private double last_check = -1;
			private double wait_time = 0;
			private string identifier;
			private double timeout = -1;
			private Airlock airlock;
			public Routine(Airlock airlock,IEnumerator<double> routine, string identifier, double timeout = -1)
			{
				this.airlock = airlock;
				this.routine = routine;
				this.identifier = identifier;
				if (timeout > 0)
				{
					this.timeout = airlock.program.timer + timeout;	
				}
			}
			public Result runCycle()
			{
				if (routine == null)
					return Result.Success;

				if (last_check < 0)
				{
					last_check = airlock.program.timer;
				}
				else
				{
					double elapsed = airlock.program.timer - last_check;
					last_check = airlock.program.timer;
					wait_time -= elapsed;
				}

				if (wait_time > 0)
					return Result.Running;

				if (timeout > 0 && airlock.program.timer >= timeout)
				{
					airlock.error($"Routine '{identifier}' timed out");
					routine.Dispose();
					routine = null;
					return Result.TimedOut;
				}
				try
				{
					if (routine.MoveNext())
					{
						wait_time = Math.Max(0.0d, routine.Current);
						return Result.Running;
					}	
				}
				catch (Exception e)
				{
					airlock.error($"An error occurred during coroutine runtime '{identifier}':\n{e.Message}");
					routine.Dispose();
					routine = null;
					return Result.Failed;
				}

				routine.Dispose();
				routine = null;
				return Result.Success;
			}
		}

		public class logEntry
		{
			public DateTime timestamp {get; private set;}
			public string text {get; private set;}
			public int count {get; private set;}
			public Program.levels level {get; private set;}
			public readonly Airlock airlock;
			public logEntry(Airlock airlock, string text, Program.levels level)
			{
				timestamp = DateTime.Now;
				this.airlock = airlock;
				this.text = text;
				count = 1;
				this.level = level;
			}
			public void IncrementCount() {
				count++;
				airlock.program.status_monitors.UpdateLogs();
			}
			public string ToString(bool colored = false)
			{

				string prefix = $"| {airlock.program.levelToString[level]} |";

				if (colored)
				{
					prefix = TextUtils.colorText(prefix, airlock.program.levelColors[level]);
				}

				return $"{timestamp} {prefix} {text}{(count > 1 ? $" (x{count})" : "")}";
			}
		}
		
		#region Per-Airlock Custom data
			private double enableBypassTolerance;
			private double intakeOxygenAtLevel;
			private double dumpOxygenAtLevel;
			private double airlockTolerance;
			private double cooldownSeconds;
		#endregion
		public int index { get; private set; } = -1;
		public IMyBlockGroup group { get; private set; }
		public Goals goal { get; private set; } = Goals.None;
		public Goals prev_goal { get; private set; } = Goals.Depressurize; // Used for determining cycle direction
		public States state {get; private set;} = States.Idle;
		public States prev_state {get; private set;} = States.Idle;
		private AirlockCore core;
		public AirlockSide inner { get; private set; }
		public AirlockSide outer { get; private set; }
		public bool isActive { get; private set; } = false;
		private Program program;
		private List<logEntry> logs = new List<logEntry>();

		public bool maintenance {get; private set;} = false;

		private Routine mainRoutine;
		private Routine leakCheckRoutine;
		private Routine maintenanceEnableRoutine;
		private Queue<Goals> command_queue = new Queue<Goals>();
		private readonly Dictionary<LightColor, VRageMath.Color> colorEnumDict;
		public Airlock(Program program, IMyBlockGroup group, int index)
		{
			this.group = group;
			this.program = program;
			this.index = index;

			core = new AirlockCore(this, program);
			core.WriteLogs();

			#region Doors
			List<IMyDoor> doors = new List<IMyDoor>();
			group.GetBlocksOfType(doors);
			if (doors.Count < 2)
			{
				throw new LookupError("Could not find 2 doors, so the airlock can't function. Please add them and then recompile");
			}
			if (doors.Count > 2) 
			{
				warning("More than 2 doors detected, only 1 inner and outer door is allowed per airlock. The extra doors won't be used by the script");
			}
			#endregion
			#region Airlock Sides
				#region Vents
					IMyAirVent inner_vent = null, outer_vent = null;
					List<IMyAirVent> outside_vents = new List<IMyAirVent>();
					group.GetBlocksOfType(outside_vents, v =>
					{
						string name = v.CustomName.Trim();
						return name.Contains(program.innerContains) || name.Contains(program.outerContains);
					});
					inner_vent = outside_vents.FirstOrDefault(v => v.CustomName.Trim().Contains(program.innerContains));
					outer_vent = outside_vents.FirstOrDefault(v => v.CustomName.Trim().Contains(program.outerContains));
					if (outer_vent == null)
						throw new LookupError($"Outer vent with '{program.outerContains}' in its name not found.");
					if (inner_vent == null)
						throw new LookupError($"Inner vent with '{program.innerContains}' in its name not found.");
					outer_vent.Enabled = true;
					inner_vent.Enabled = true;	
					if (program.hideAirlockBlocks)
					{
						outer_vent.ShowInTerminal = false;
						inner_vent.ShowInTerminal = false;	
					}
				#endregion
				#region Doors
					IMyDoor inner_door = null, outer_door = null;
					foreach (IMyDoor door in doors)
					{
						if (program.hideAirlockBlocks)
							door.ShowInTerminal = false;
						if (inner_door == null && door.CustomName.Contains(program.innerContains))
							inner_door = door;
						else if (outer_door == null && door.CustomName.Contains(program.outerContains))
							outer_door = door;
					}
					if (inner_door == null)
					{
						throw new LookupError($"No door with the word(s) '{program.innerContains}' in its name found");
					}
					if (outer_door == null)
					{
						throw new LookupError($"No door with the word(s) '{program.outerContains}' in its name found");
					}
				#endregion
				#region Status lights
					List<IMyLightingBlock> inner_status = new List<IMyLightingBlock>(), outer_status = new List<IMyLightingBlock>();
					group.GetBlocksOfType(inner_status, l => l.CustomName.Trim().ToLower().Contains(program.innerContains));
					group.GetBlocksOfType(outer_status, l => l.CustomName.Trim().ToLower().Contains(program.outerContains));
				#endregion
			#endregion

			leakCheckRoutine = new Routine(this, check_airleak(), "airleak_check");

			inner = new AirlockSide(this, inner_door, inner_vent, inner_status,	program, program.ReadCustomData(inner_door));
			outer = new AirlockSide(this, outer_door, outer_vent, outer_status, program, program.ReadCustomData(outer_door));

			if (inner.openDuringLeak && outer.openDuringLeak)
				warning("Both doors will open during an air leak. Please make sure this is the setting you wanted");

			if (cooldownSeconds < 0)
				cooldownSeconds = program.airlockCooldown;
			
			#region Color Enum
			colorEnumDict = new Dictionary<LightColor, VRageMath.Color>
			{
				{ LightColor.OK, program.lightColor_OK },
				{ LightColor.Cycle, program.lightColor_CYCLE },
				{ LightColor.Error, program.lightColor_ERR }
			};
			#endregion
		
			#region Load saved data
				string[] data = program.Storage.Split('\n').Select(l => l.Split(';')).SingleOrDefault(l => l[0] == group.Name);
				if (data != null)
				{
					isActive = true;
					goal = (Goals)byte.Parse(data[1]);
					state = (States)byte.Parse(data[2]);
					prev_state = (States)byte.Parse(data[3]);
					
					program.Runtime.UpdateFrequency = UpdateFrequency.Update1 | UpdateFrequency.Update100;
					mainRoutine = new Routine(this, cycle(), "airlock_cycle");
				}
			#endregion
			core.WriteLogs();
		}
		public void addAirlockIndex(IMyTerminalBlock block)
		{
			if (!block.CustomName.Contains($"[{index}]"))
				block.CustomName = $"[{index}] {block.CustomName}";
			if (program.hideAirlockBlocks && !block.CustomName.ToLower().Contains("[main]"))
				block.ShowInTerminal = false;
		}
		public bool queueCommand(Goals goal)
		{
			if (command_queue.Contains(goal))
				return false;
			
			if (this.goal == goal && isActive)
				return false;

			command_queue.Enqueue(goal);
			info($"Action '{goal}' queued");
			return true;
		}
		public Goals? dequeue(Goals? goal = null)
		{
			try
			{
				if (goal == null)
					return command_queue.Dequeue();
				
				Queue<Goals> new_queue = new Queue<Goals>();
				bool removed = false;
				for (int i = 0; i < command_queue.Count; i++)
				{
					Goals element = command_queue.ElementAt(i);
					if (!removed && element == goal)
					{
						removed = true;
						continue;
					}
					new_queue.Enqueue(element);
				}
				command_queue = new_queue;
				return null;
			}
			catch (InvalidOperationException)
			{
				return null;
			}

		} 

		private IEnumerator<double> waitForDoor(AirlockSide side, DoorStatus status, bool disableAfter = true, double timeout = -1, AirlockSide other_side = null)
		{
			Routine side1 = side.waitForDoor(status, disableAfter, timeout);
			Routine side2 = other_side == null ? null : other_side.waitForDoor(status, disableAfter, timeout);

			Routine.Result result1 = Routine.Result.Running;
			Routine.Result result2 = side2 == null ? Routine.Result.Success : Routine.Result.Running;

			while (result1 == Routine.Result.Running || result2 == Routine.Result.Running)
			{
				if (result1 == Routine.Result.Running)
					result1 = side1.runCycle();
				
				if (result2 == Routine.Result.Running)
					result2 = side2.runCycle();
				
				if (result1 == Routine.Result.Running || result2 == Routine.Result.Running)
					yield return 0.1;
			}

			if (result1 != Routine.Result.Success)
			{
				throw new Exception($"Failed to move door '{side.door.CustomName}' to '{status}': {result1}");
			}
			if (result2 != Routine.Result.Success)
			{
				throw new Exception($"Failed to move door '{other_side.door.CustomName}' to '{status}': {result2}");
			}
		}

		/// <summary>
		/// Returns the bool value of the `mainRoutine` routine.
		/// Ignores other routines
		/// </summary>
		public Routine.Result runRoutines()
		{
			Routine.Result result;
			result = leakCheckRoutine.runCycle();
			if (result == Routine.Result.Failed || result == Routine.Result.TimedOut)
			{
				core.SetLights(LightColor.Error, RequestID.LeakDetection, 50);
				core.setAlarm(RequestID.RunCycles, true);
			}

			result = core.alarmRoutine.runCycle();
			if (result == Routine.Result.Failed || result == Routine.Result.TimedOut)
			{
				core.SetLights(LightColor.Error, RequestID.Alarm, 50);
				core.setAlarm(RequestID.RunCycles, true);
			}

			if (maintenanceEnableRoutine != null)
			{
				result = maintenanceEnableRoutine.runCycle();
				if (result == Routine.Result.Success)
					maintenanceEnableRoutine = null;
			}

			if (!isActive)
				return Routine.Result.Success;

			result = mainRoutine.runCycle();
			if (result == Routine.Result.Failed || result == Routine.Result.TimedOut)
			{
				core.SetLights(LightColor.Error, RequestID.Cycle, 50);
				core.setAlarm(RequestID.RunCycles, true);
			}
			return result;
		}
		#region Logging
		private void log(string text, Program.levels severity = Program.levels.Info, bool send = true)
		{
			if (!send)
				return;

			logEntry entry = new logEntry(this, text, severity);
			if (logs.Count > 0)
			{
				logEntry prev_entry = logs[logs.Count - 1];
				if (entry.text == prev_entry.text)
				{
					prev_entry.IncrementCount();
				}
				else
				{
					logs.Add(entry);	
				}
			}
			else
			{
				logs.Add(entry);
			}
			if (logs.Count > Program.maxLogEntries) 
				logs.RemoveAt(0);
			
			if (core != null)
				core.WriteLogs();
			if (program.status_monitors != null)
				program.status_monitors.UpdateLogs();
		}
		public void debug(string text, bool send = true) => log(text, Program.levels.Debug, send);
		public void info(string text, bool send = true) => log(text, Program.levels.Info, send);
		public void warning(string text, bool send = true) => log(text, Program.levels.Warning, send);
		public void error(string text, bool send = true) => log(text, Program.levels.Error, send);
		public string[] getLogs(bool color = false, Program.levels? desiredLevel = Program.levels.Warning)
		{
			logEntry[] logEntries = logs.Where(l => l.level >= desiredLevel).ToArray();

			return logEntries.Select(e => e.ToString(color)).ToArray();
		}
		#endregion
		public bool getAlarmStatus() => core.getAlarmStatus();
		public logEntry[] getLogEntries() => logs.ToArray();
		private IEnumerator<double> setMaintenanceCore(bool value)
		{
			if (maintenance == value)
				yield break;

			maintenance = value;
			if (maintenance)
			{
				IEnumerator<double> doorWait = waitForDoor(inner, DoorStatus.Closed, other_side:outer);
				while (doorWait.MoveNext())
					yield return doorWait.Current;
				
				core.SetLights(LightColor.Cycle, RequestID.Maintenance, 50);
			}
			else
			{
				core.SetLights(LightColor.OK, RequestID.Maintenance);
				if (goal != Goals.None)
					StartCycle(goal);
				else
					StartCycle(Goals.Pressurize);
			}
			warning($"Maintenance mode {(maintenance ? "enabled" : "disabled")}");
			yield break;
		}
		public void setMaintenance(bool value)
		{
			if (maintenanceEnableRoutine != null)
				return;
			maintenanceEnableRoutine = new Routine(this, setMaintenanceCore(value), "setMaintenance");
		}
		private IEnumerator<double> check_airleak()
		{
			IEnumerator<double> doorWait;
			bool prev_state = false;
			while (true)
			{
				if (isActive && (inner.door.Status != DoorStatus.Closed || outer.door.Status != DoorStatus.Closed))
				{
					yield return program.airtightnessCheckDelay;
					continue;
				}
				bool problem = (
					(!inner.vent.CanPressurize && !inner.vent.ignoreAirtightness) || 
					(!outer.vent.CanPressurize && !outer.vent.ignoreAirtightness) || 
					!core.CanPressurize()
				) && !maintenance;
				
				if (problem && !prev_state)
				{
					core.setAlarm(RequestID.LeakDetection, true);
					error("Airleak detected");

					core.Pressurize(); // Airlock won't intake extra air
					
					if (inner.openDuringLeak && !outer.openDuringLeak)
						doorWait = waitForDoor(outer, DoorStatus.Closed);
					else if (!inner.openDuringLeak && outer.openDuringLeak)
						doorWait = waitForDoor(inner, DoorStatus.Closed);
					else
						doorWait = waitForDoor(outer, DoorStatus.Closed, other_side:inner);
					while (doorWait.MoveNext())
						yield return doorWait.Current;

					yield return 0.2;

					if (!core.CanPressurize())
					{
						error("Airlock has an air leak. Keeping both doors closed");
						prev_state = problem;
						continue;
					}
					info("Air leak detected outside of airlock");

					if (inner.openDuringLeak)
					{
						doorWait = waitForDoor(inner, DoorStatus.Open);
					}
					if (outer.openDuringLeak)
					{
						if (inner.openDuringLeak)
							doorWait = waitForDoor(inner, DoorStatus.Open, other_side:outer);
						else
							doorWait = waitForDoor(outer, DoorStatus.Open);
					}

					while (doorWait.MoveNext())
						yield return doorWait.Current;
					
					prev_state = problem;
				}
				else if (!problem && prev_state)
				{
					if (!maintenance)
						info("Airleak over");
					else
						debug("Alert over due to enabling of maintenance mode");

					core.setAlarm(RequestID.LeakDetection, false);
					
					yield return 2;
					if (enableBypassTolerance >= 0)
					{

						double inner_level = inner.vent.GetOxygenLevel();
						double outer_level = outer.vent.GetOxygenLevel();
						if (
							inner_level < outer_level + enableBypassTolerance &&
							inner_level > outer_level - enableBypassTolerance &&
							!inner.vent.ignoreAirtightness && inner.vent.CanPressurize && 
							!outer.vent.ignoreAirtightness && outer.vent.CanPressurize
						)
						{
							debug("Automatically reopening bypass after air leak");
							doorWait = waitForDoor(inner, DoorStatus.Open, other_side:outer);
							while (doorWait.MoveNext())
								yield return doorWait.Current;
						}
					}
					prev_state = problem;
				}
				yield return program.airtightnessCheckDelay;
			}
		}
		public bool StartCycle(Goals requestedGoal, bool fromQueue = false)
		{
			if (isActive && !fromQueue)
				return false;

			goal = requestedGoal;
			state = States.WaitingForDoorClose;
			isActive = true;
			program.Runtime.UpdateFrequency = UpdateFrequency.Update1 | UpdateFrequency.Update100;

			mainRoutine = new Routine(this, cycle(), "airlock_cycle");
			return true;

		}
		public void StopCycle()
		{
			prev_goal = goal;
			inner.vent.Depressurize = false;
			outer.vent.Depressurize = false;
			if (command_queue.Count > 0)
			{
				Goals new_goal;
				if (command_queue.TryDequeue(out new_goal))
				{
					info($"Running queued command '{new_goal}'");
					StartCycle(new_goal, true);
				}
				return;
			}
			core.SetLights(LightColor.OK, RequestID.Cycle);
			inner.SetStatusLight(LightColor.OK, RequestID.Cycle);
			outer.SetStatusLight(LightColor.OK, RequestID.Cycle);
			SetState(States.Idle, States.Idle);
			goal = Goals.None;
			isActive = false;
		}
		private void SetState(States new_state, States? prev_state = null)
		{
			if (prev_state != null)
				this.prev_state = (States)prev_state;
			else
				this.prev_state = state;
			state = new_state;
		}
		private IEnumerator<double> intakeOxygen(AirlockSide dest, AirlockSide src) {
			IEnumerator<double> doorWait;
			double dest_oxylevel = dest.vent.GetOxygenLevel();
			double src_oxylevel = src.vent.GetOxygenLevel();
			if (dest.vent.connectedToMain && dest.vent.useForOxygenIntake && dest_oxylevel > 0.1f) // At least 10% oxygen level to even TRY intaking from said side
			{
				dest.vent.Depressurize = true;
				while (core.GetOxygenLevel() <= intakeOxygenAtLevel + 0.1f)
				{
					yield return 0.01;
				}
				dest.vent.Depressurize = false;
			}
			else if (dest.doorUseForOxygenIntake && dest_oxylevel > 0.1f)
			{
				doorWait = waitForDoor(dest, DoorStatus.Open);
				while (doorWait.MoveNext())
					yield return doorWait.Current;

				core.Depressurize();
				while (core.GetOxygenLevel() <= intakeOxygenAtLevel + 0.1f)
				{
					yield return 0.01;
				}
				core.Pressurize();

				SetState(States.Delay);
				yield break;
			}
			else if (src.vent.connectedToMain && src.vent.useForOxygenIntake && src_oxylevel > 0.1f)
			{
				src.vent.Depressurize = true;
				while (core.GetOxygenLevel() <= intakeOxygenAtLevel + 0.1f)
				{
					yield return 0.01;
				}
				src.vent.Depressurize = false;
			}
			else if (src.doorUseForOxygenIntake && src_oxylevel > 0.1f)
			{
				doorWait = waitForDoor(src, DoorStatus.Open);
				while (doorWait.MoveNext())
					yield return doorWait.Current;

				core.Depressurize();
				while (core.GetOxygenLevel() <= intakeOxygenAtLevel + 0.1f)
				{
					yield return 0.01;
				}
				core.Pressurize();
				doorWait = waitForDoor(src, DoorStatus.Closed);
				while (doorWait.MoveNext())
					yield return doorWait.Current;
			}
			else
			{
				throw new Exception("No oxygen intake path for the airlock");
			}
		}
		private bool CanEnableBypass() 
		{
			double inner_level = inner.vent.GetOxygenLevel();
			double outer_level = outer.vent.GetOxygenLevel();
			return (
				core.CanPressurize() &&
				!maintenance &&
				enableBypassTolerance >= 0 &&
				inner_level < outer_level + enableBypassTolerance &&
				inner_level > outer_level - enableBypassTolerance &&
				!inner.vent.ignoreAirtightness && inner.vent.CanPressurize && 
				!outer.vent.ignoreAirtightness && outer.vent.CanPressurize
			);
		}
		private IEnumerator<double> cycle()
		{
			info($"Starting cycle (Goal: {goal})");
			IEnumerator<double> doorWait;

			AirlockSide source, destination;
			if (goal == Goals.Depressurize)
			{
				source = inner;
				destination = outer;
			}
			else if (goal == Goals.Pressurize)
			{
				source = outer;
				destination = inner;
			}
			else
				throw new Exception($"Invalid goal given: '{goal}'");
			
			core.SetLights(LightColor.Cycle, RequestID.Cycle);
			source.SetStatusLight(LightColor.Cycle, RequestID.Cycle, 50);
			destination.SetStatusLight(LightColor.Cycle, RequestID.Cycle, 50);

			while (isActive)
			{
				switch (state)
				{
					case States.WaitingForDoorClose:
						if (CanEnableBypass())
						{
							info("Both sides of the airlock have the same pressure, enabling bypass");
							doorWait = waitForDoor(inner, DoorStatus.Open, other_side:outer);
							while (doorWait.MoveNext())
								yield return doorWait.Current;
							command_queue.Clear();
							StopCycle();
							break;
						}
						if (destination.door.Status == DoorStatus.Open)
						{
							info("Airlock is already open in the given direction");
							StopCycle();
							break;
						}

						source.SetStatusLight(LightColor.Cycle, RequestID.Cycle);

						doorWait = waitForDoor(inner, DoorStatus.Closed, timeout:5, other_side:outer);
						while (doorWait.MoveNext())
							yield return doorWait.Current;

						source.SetStatusLight(LightColor.OK, RequestID.Cycle);

						core.Pressurize();
						debug("Beginning cycling");
						SetState(States.Cycling);
						break;
					case States.Cycling:

						float targetOxyLevel = destination.vent.GetOxygenLevel();

						if (core.GetOxygenLevel() <= intakeOxygenAtLevel)
						{
							warning("Airlock tanks are low on oxygen, intaking from outside of the airlock");
							IEnumerator<double> intakeRoutine = intakeOxygen(destination, source);
							while (intakeRoutine.MoveNext())
								yield return intakeRoutine.Current;
						}
						else if (core.GetOxygenLevel() >= dumpOxygenAtLevel)
						{
							if (!inner.useForOxygenDump && !outer.useForOxygenDump)
								warning("Oxygen dumping level reached, but could not dump anywhere");
							SetState(States.WaitingForDoorOpen);
							break;
						}
						core.Pressurize();
						AirlockCore.OxygenBalance balance;
						do
						{
							yield return 0.01;
							balance = core.SetOxygenLevel(targetOxyLevel);
						}
						while (balance != AirlockCore.OxygenBalance.OK);
						SetState(States.WaitingForDoorOpen);
						break;
					case States.WaitingForDoorOpen:
						info("Opening airlock door");
						core.SetLights(LightColor.OK, RequestID.Cycle);
						destination.SetStatusLight(LightColor.Cycle, RequestID.Cycle, 50);

						doorWait = waitForDoor(destination, DoorStatus.Open);
						while (doorWait.MoveNext()) 
							yield return doorWait.Current;

						SetState(States.Delay);
						break;
					case States.Delay:
						core.Pressurize();
						if (destination.closeAfter < 0 && !maintenance)
						{
							SetState(States.Cooldown);
							break;
						}
						else if (maintenance)
							yield return 2;
						else 
							yield return destination.closeAfter;
						try
						{
							Goals next_cmd = command_queue.Peek();
							if (next_cmd == Goals.Depressurize)
								outer.SetStatusLight(LightColor.Cycle, RequestID.Cycle, 50);
							else
								inner.SetStatusLight(LightColor.Cycle, RequestID.Cycle, 50);			
						}
						catch (InvalidOperationException) {}
						
						doorWait = waitForDoor(destination, DoorStatus.Closed);
						while (doorWait.MoveNext()) 
							yield return doorWait.Current;

						SetState(States.Cooldown);
						break;
					case States.Cooldown:
						if (cooldownSeconds >= 0)
							yield return cooldownSeconds;
						else
							yield return program.airlockCooldown;

						info("Cycle done");
						info($"Oxygen tanks level: {Math.Round(core.GetOxygenLevel()*100, 2)}%");  
						StopCycle();
						yield break;
				}
				yield return 0.1;
			}        
			yield break;
		}
	}
}