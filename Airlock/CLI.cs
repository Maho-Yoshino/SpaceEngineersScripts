// <mdk sortorder="999" />
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
using System.CodeDom;
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
using System.Runtime.Remoting.Messaging;
using System.Security.Cryptography;
using System.Security.Policy;
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

namespace IngameScript
{
	public class InvalidInput : Exception
	{
		public InvalidInput() {}
		public InvalidInput(string message) : base(message) {}
	}
	public class CLI
	{
		public class CommandContext
		{
			public string name;
			public List<string> positionals = new List<string>();
			public HashSet<string> flags = new HashSet<string>();
			public Dictionary<string, string> options = new Dictionary<string, string>();
		}
		private Program program;
		private CommandContext context;
		private readonly string[] available_commands = { "help", "in", "out", "cycle", "stop", "bypass", "logs", "maintenance", "dequeue", "status", "setuphelp", "clear" };

		public CLI(Program program, string[] arguments)
		{
			this.program = program;
			context = Parse(arguments);
		}
		public void Run()
		{
			bool result;
			switch (context.name)
			{
				case "in":
					result = commandIn();
					break;
				case "out":
					result = commandOut();
					break;
				case "cycle":
					result = commandCycle();
					break;
				case "stop":
					result = commandStop();
					break;
				case "logs":
					result = commandLogs();
					break;
				case "bypass":
					result = commandBypass();
					break;
				case "dequeue":
					result = commandDequeue();
					break;
				case "status":
					result = commandStatus();
					break;
				case "maintenance":
					result = commandMaintenance();
					break;
				case "setuphelp":
					commandSetuphelp();
					return;
				case "clear":
					commandClear();
					return;
				case "help":
				default:
					commandHelp();
					return;
			}
			if (!result)
				program.WriteText("Command was not entered correctly");
		}
		private bool TryGetAirlock(out Airlock airlock, int index = 0)
		{
			airlock = null;

			if (context.positionals.Count < index+1)
			{
				program.WriteText($"{TextUtils.levelPrefix(program, Program.levels.Error)} Missing airlock index");
				return false;
			}

			int idx;
			if (!int.TryParse(context.positionals[index], out idx))
			{
				program.WriteText($"{TextUtils.levelPrefix(program, Program.levels.Error)} Invalid airlock index '{context.positionals[index]}'");
				return false;
			}
			airlock = program.airlocks.FirstOrDefault(item => item.index == idx);

			if (airlock == null)
			{
				program.WriteText($"{TextUtils.levelPrefix(program, Program.levels.Error)} No airlock matches index {idx}");
				return false;
			}

			return true;
		}
		private bool commandIn()
		{
			if (context.flags.Contains("help"))
			{
				program.WriteText(
					"Command usage: in {airlock index}\n" + 
					"Available flags: --queue, --force\n\n" + 
					"Opens the given airlock inwards. By default it blocks if the airlock is already cycling"
				);
				return true;
			}
			Airlock airlock;
			if (!TryGetAirlock(out airlock))
				return false;
			bool force = context.flags.Contains("force");
			bool queue = context.flags.Contains("queue");
			if (force)
			{
				airlock.info("Force opening the inner door");
				airlock.inner.door.Enabled = true;
				airlock.inner.door.OpenDoor();
				return true;
			}
			if (queue && airlock.isActive)
			{
				airlock.queueCommand(Airlock.Goals.Pressurize);
				return true;
			}
			if (!airlock.StartCycle(Airlock.Goals.Pressurize))
			{
				if (airlock.isActive)
				{
					airlock.info("Airlock is currently cycling. Please wait until it finishes");
					return true;
				}
				airlock.error("Failed to start the cycle");
				return true;
			}
			program.Runtime.UpdateFrequency = UpdateFrequency.Update1 | UpdateFrequency.Update100;
			return true;
		}
		private bool commandOut()
		{

			if (context.flags.Contains("help"))
			{
				program.WriteText(
					"Command usage: out {airlock index}\n" + 
					"Available flags: --queue, --force\n\n" + 
					"Opens the given airlock outwards. By default it blocks if the airlock is already cycling"
				);
				return true;
			}
			Airlock airlock;
			if (!TryGetAirlock(out airlock))
				return false;
			bool force = context.flags.Contains("force");
			bool queue = context.flags.Contains("queue");
			if (force)
			{
				airlock.info("Force opening the outer door");
				airlock.outer.door.Enabled = true;
				airlock.outer.door.OpenDoor();
				return true;
			}
			if (queue && airlock.isActive)
			{
				airlock.queueCommand(Airlock.Goals.Depressurize);
				return true;
			}
			if (!airlock.StartCycle(Airlock.Goals.Depressurize))
			{
				if (airlock.isActive)
				{
					airlock.info("Airlock is currently cycling. Please wait until it finishes");
					return true;
				}
				airlock.error("Failed to start the cycle");
				return true;
			}
			program.Runtime.UpdateFrequency = UpdateFrequency.Update1 | UpdateFrequency.Update100;
			return true;
		}
		private bool commandCycle()
		{
			if (context.flags.Contains("help"))
			{
				program.WriteText(
					"Command usage: cycle {airlock index}\n" + 
					"Available flags: --queue, --force\n\n" + 
					"Cycles the given airlock. By default it blocks if the airlock is already cycling.\nCycles in the direction opposite of the direction it previously went to"
				);
				return true;
			}
			Airlock airlock;
			if (!TryGetAirlock(out airlock))
				return false;
			bool queue = context.flags.Contains("queue");
			Airlock.Goals goal;
			if (airlock.prev_goal == Airlock.Goals.Pressurize ||
				airlock.inner.door.Status == DoorStatus.Open
			)
			{
				goal = Airlock.Goals.Depressurize;
			}
			else if (
				airlock.prev_goal == Airlock.Goals.Depressurize ||
				airlock.outer.door.Status == DoorStatus.Open
			)
			{
				goal = Airlock.Goals.Pressurize;
			}
			else
			{
				airlock.error("Could not determine which direction to cycle. Tell directly by using 'in' and 'out', or open the door in the opposite direction");
				return true;
			}
			if (queue && airlock.isActive)
			{
				airlock.queueCommand(goal);
				return true;
			}
			if (!airlock.StartCycle(goal))
			{
				if (airlock.isActive)
				{
					airlock.info("Airlock is currently cycling. Please wait until it finishes");
					return true;
				}
				airlock.error("Failed to start the cycle");
				return true;
			}
			program.Runtime.UpdateFrequency = UpdateFrequency.Update1 | UpdateFrequency.Update100;
			return true;
		}
		private bool commandStop()
		{
			if (context.flags.Contains("help"))
			{
				program.WriteText(
					"Command usage: stop {airlock index}\n\n" + 
					"Stops the currently running cycle of the airlock"
				);
				return true;
			}
			Airlock airlock;
			if (!TryGetAirlock(out airlock))
				return false;
			airlock.warning("Stopping cycle");
			airlock.StopCycle();
			return true;
		}
		private bool commandBypass()
		{
			if (context.flags.Contains("help"))
			{
				program.WriteText(
					"Command usage: bypass {airlock index}\n\n" + 
					"Enables the 'bypass' mode of the airlock, where it opens both doors"
				);
				return true;
			}
			Airlock airlock;
			if (!TryGetAirlock(out airlock))
				return false;
				
			airlock.inner.door.Enabled = true;
			airlock.inner.door.OpenDoor();
			airlock.outer.door.Enabled = true;
			airlock.outer.door.OpenDoor();
			return true;
		}
		private bool commandLogs()
		{
			if (context.flags.Contains("help"))
			{
				program.WriteText(
					"Command usage: logs {airlock index}\n" + 
					"Available options: --level\n\n" + 
					$"Available option values: \n level\n{string.Join("\n  ", new string[] {"DEBUG","INFO","WARNING","ERROR"})}" + 
					"Gets the logs of the airlock, at the given level. If a level is not provided, it displays the level that is set in the PB"
				);
				return true;
			}
			Airlock airlock;
			if (!TryGetAirlock(out airlock))
				return false;
			
			Program.levels level;
			if (context.options.ContainsKey("level"))
			{
				string lvlInput = context.options["level"];
				switch (lvlInput)
				{
					case "info":
						level = Program.levels.Info;
						break;
					case "debug":
						level = Program.levels.Debug;
						break;
					case "warning":
						level = Program.levels.Warning;
						break;
					case "error":
						level = Program.levels.Error;
						break;
					default:
						throw new InvalidInput($"Invalid logging level '{lvlInput}' selected");
				}
			}
			else
			{
				level = program.desiredLogLevel;
			}
			string[] logs = airlock.getLogs(true, level);
			program.WriteText(
				$"<===|{airlock.group.Name}|===>\n\n" + 
				string.Join("\n", logs) 
			);
			return true;
		}
		private bool commandDequeue()
		{
			if (context.flags.Contains("help"))
			{
				program.WriteText(
					"Command usage: dequeue {airlock index} {direction}\n" +
					"- Direction can be either 'in' or 'out'" +  
					"Available flags: --disable\n\n" + 
					"Dequeues the first goal in the direction given. \nShould be set up for a sensor's 'player no longer detected' action, so that airlocks don't cycle for no reason after the player is gone"
				);
				return true;
			}
			Airlock airlock;
			if (!TryGetAirlock(out airlock))
				return false;
			if (context.positionals.Count < 2)
			{
				airlock.error("No direction given");
				return false;
			}

			Airlock.Goals goal;
			if (context.positionals[1].ToLower() == "in")
				goal = Airlock.Goals.Pressurize;
			else if (context.positionals[1].ToLower() == "out")
				goal = Airlock.Goals.Depressurize;
			else
				throw new Exception($"Invalid direction given: '{context.positionals[1]}'");

			if (airlock.dequeue(goal) != null)
				airlock.debug($"Successfully dequeued goal '{goal}'");
			else
				airlock.debug("There was no goal to dequeue");
			return true;
		}
		private bool commandHelp()
		{
			program.WriteText($"<===| Airlock Control Script Help |===>\nAvailable commands:");
			program.WriteText(string.Join("\n", available_commands.Select(i => $"- {i}")), true);
			program.WriteText("\nYou can run all of these commands with the flag '--help' to get help for that specific command", true);
			return true;
		}
		private bool commandStatus()
		{

			if (context.flags.Contains("help"))
			{
				program.WriteText(
					"Command usage: status {airlock index}\n" + 
					"Mostly used for debugging, gets the status of an airlock, with values like the current goal"
				);
				return true;
			}
			Airlock airlock;
			if (!TryGetAirlock(out airlock))
				return false;
			airlock.info($"Airlock status check\n- Is Active: {(airlock.isActive ? "Yes": "No")}\n- Active alarm: {(airlock.getAlarmStatus() ? "Yes": "No")}\n- Goal: {airlock.goal}\n- Index: {airlock.index}\n- Maintenance mode: {(airlock.maintenance ? "enabled" : "disabled")}");
			commandLogs();
			return true;
		}
		private bool commandMaintenance()
		{
			if (context.flags.Contains("help"))
			{
				program.WriteText(
					"Command usage: maintenance {airlock index}\n" +
					"- Multiple airlock indexes can be given at once" + 
					"Available flags: --disable\n\n" + 
					"Enables the maintenance mode on the given airlocks, disables if the 'disable' flag is given.\nAirlocks in maintenance mode do not sound their alarms during an air leak, and close both their doors, even if the configurations are not set up for that, so air doesn't leak out. Airlock remains usable"
				);
				return true;
			}
			Dictionary<int, bool> airlock_indexes = new Dictionary<int, bool>();
			if (context.positionals.Count == 0) 
				return false;
			bool maintenance = !context.flags.Contains("disable");
			for (int i = 0; i < context.positionals.Count; i++)
			{
				Airlock airlock;
				if (!TryGetAirlock(out airlock, i))
					return false;
				
				airlock.setMaintenance(maintenance);
				airlock_indexes.Add(airlock.index, maintenance);
			}
			program.WriteText($"Successfully changed maintenance mode for the following airlocks:\n- {string.Join("\n- ", airlock_indexes.Select(kvp => $"{kvp.Key}: {(kvp.Value ? "enabled" : "disabled")}"))}");
			return true;
		}
		private void commandSetuphelp()
		{
			program.WriteText(
				"<=== Airlock Setup Help ===>\n\n" + 
				"-= Blocks usable in airlocks =-\n" + 
				"- An airlock requires:\n  - " + string.Join("\n  - ", new string[] {"2 doors","Vents inside","1-1 vent on the 2 sides","Some way of activating the airlock (sensors, buttons, etc.)","At least one oxygen tank (can be shared between airlocks)"}) + "\n" +
				"- Optional blocks:\n  - " + string.Join("\n  - ", new string[] {"1 or more Sound blocks (for alarm)", "1 or more LCD Panels (for logs)", "1 or more lighting blocks (for status signaling)","1 or more status lights (per-side, must have side distinction)","1 or more Directional status lights (lights with the 'innerContains' or 'outerContains' text in their names)"}) + "\n\n" + 
				"- These blocks have to be added into the airlock's group to be recognized\n\n" + 
				"- The script will rename blocks in the airlock's group with a prefix, to indicate which airlock a block belongs to. These can stack, for example on oxygen tanks\n\n" + 
				"-= Airlock construction tips =-\n" + 
				"It is highly advised to have a separate conveyor system from your O2/H2 system, with separate oxygen tanks and vents\n" + 
				"It is highly advised to check out the configs at the following blocks' custom data:\n- " + string.Join("\n- ", new string[] {"Programmable block (global)","'Main' vent (per-airlock)","inner and outer doors (per-airlock)","LCD panels (per-lcd)"}) + "\n\n" + 
				"-= Setting up =- \n" + 
				"- Every airlock requires an indicated 'inner' and 'outer' side door as well as a vent on both sides of the airlock and at least 1 inside it, the location of the block is indicated by the block's name. the searched prefix can be customized in the programmable block custom data.\n" + 
				"- Set the button/sensor action to run the programmable block with the 'in #', 'out #' and 'cycle' with the # representing the airlock's id/number. The commands 'in #' (inner) and 'out #' (outer) refer to which door opens after the command runs.\n" +
				"- Put the vents, doors, buttons/sensors and oxygen tanks (as well as any other optional blocks) in a group containing 'Airlock #' .\n" +
				"-= Extra customizations =-\n" +
				"- You can queue commands by using the '--queue' flag, otherwise the script will block any commands sent during a cycle\n"
			);
		}
		private void commandClear() 
		{
			if (context.flags.Contains("help"))
			{
				program.WriteText("This command clears the output of the PB (a.k.a. where you are reading this). This is not required to clear it, as output get autocleared after a while");
				return;
			}
			program.ResetOutput();
		}
		private CommandContext Parse(string[] arguments)
		{
			CommandContext result = new CommandContext();

			if (arguments == null || arguments.Length == 0)
			{
				result.name = "help";
				return result;
			}

			result.name = arguments[0].ToLowerInvariant();

			for (int i = 1; i < arguments.Length; i++)
			{
				string argument = arguments[i];

				if (argument.StartsWith("--"))
				{
					string option = argument.Substring(2);
					int separator = option.IndexOf('=');

					if (separator >= 0)
					{
						string key = option.Substring(0, separator);
						string value = option.Substring(separator + 1);

						result.options[key] = value;
					}
					else
					{
						result.flags.Add(option);
					}

					continue;
				}

				result.positionals.Add(argument);
			}

			return result;
		}

	}
}