using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;
using VRageRender;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {

        List<IMyTimerBlock> doneTimers = new List<IMyTimerBlock>();
        List<IMyTimerBlock> initTimers = new List<IMyTimerBlock>();
        IMyProjector projector = null;
        bool finished = false;
        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
            if (string.IsNullOrEmpty(Me.CustomData))
            {
                Me.CustomData = "projectorName=Projector\n" +
                "# Initialization Timers (When projector's remaining blocks jumps up from 0)\n"+
                "initTimerGroupName=initProjectorTimers\n"+
                "initTimerName=initProjectorTimer\n"+
                "# Done Timers (When projector's remaining blocks is equal to 0)\n"+
                "doneTimerGroupName=doneProjectorTimers\n"+
                "doneTimerName=doneProjectorTimer";
            }

            Dictionary<string, string> config = new Dictionary<string, string>();
            foreach (string line in Me.CustomData.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Where(l => !l.TrimStart().StartsWith("#"))) // Ignore comment lines
            {
                string[] parts = line.Split(new[] { '=' }, 2);
                if (parts.Length == 2)
                {
                    config[parts[0].Trim()] = parts[1].Trim();
                }
            }
            
            FillList(ref initTimers, config.GetValueOrDefault("initTimerGroupName"), config.GetValueOrDefault("initTimerName"));
            FillList(ref doneTimers, config.GetValueOrDefault("doneTimerGroupName"), config.GetValueOrDefault("doneTimerName"));

            projector = GridTerminalSystem.GetBlockWithName(config["projectorName"]) as IMyProjector;
            if (projector == null)
            {
                Echo($"Projector not found. Please ensure a projector named '{config["projectorName"]}' exists.");
            }
            else
            {
                Echo("Projector found successfully.");
            }
        }

        private void FillList<T>(ref List<T> obj, string groupName, string directName) where T : class
        {
            if (!string.IsNullOrEmpty(groupName))
            {
                GridTerminalSystem.GetBlockGroupWithName(groupName)?.GetBlocksOfType(obj);
                if (obj.Count == 0)
                {
                    Echo($"No blocks of type {typeof(T).Name} found in group '{groupName}'.");
                }
                else
                {
                    Echo($"Found {obj.Count} blocks of type {typeof(T).Name} in group '{groupName}'.");
                }
            }
            if (!string.IsNullOrEmpty(directName))
            {
                var block = GridTerminalSystem.GetBlockWithName(directName) as T;
                if (block != null)
                {
                    obj.Add(block);
                    Echo($"Added block '{directName}' of type {typeof(T).Name} to the list.");
                }
                else
                {
                    Echo($"Block named '{directName}' of type {typeof(T).Name} not found.");
                }
            }
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (projector == null || doneTimers.Count == 0)
            {
                Echo("Cannot run script: Projector or timers not properly initialized.");
                return;
            }
            if (projector.RemainingBlocks > 0)
            {
                Echo($"Projector is still projecting. Remaining blocks: {projector.RemainingBlocks}");
                finished = false;
                return;
            }
            else if (projector.RemainingBlocks == 0 && !finished)
            {
                Echo("Projection complete. Triggering timers...");

                foreach (var timer in doneTimers)
                {
                    timer.Trigger();
                }

                finished = true;
            }
            else
            {
                Echo("Projection already completed and timers triggered.");
            }
        }
    }
}
