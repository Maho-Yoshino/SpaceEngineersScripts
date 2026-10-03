// <mdk sortorder="995" />
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

namespace IngameScript
{
    public class CentralMonitors
    {
        // TODO: Finish Central monitor
        private readonly Display[] monitors;
        private readonly Program program;
        public CentralMonitors(Program program)
        {
            this.program = program;
            List<IMyTextPanel> panels = new List<IMyTextPanel>();
            program.GridTerminalSystem.GetBlocksOfType(panels, p => p.CustomName.Trim().StartsWith(program.statusTextPanelPrefix));
            monitors = panels.Select(p => new Display(program, p)).ToArray(); 
            UpdateLogs();
        }
        public void UpdateLogs()
        {
            List<Airlock.logEntry> logEntriesConcatenated = new List<Airlock.logEntry>();
            foreach (Airlock airlock in program.airlocks)
            {
                logEntriesConcatenated.AddRange(airlock.getLogEntries());
            }
            logEntriesConcatenated.Sort((a, b) => a.timestamp.CompareTo(b.timestamp));

            string[] logs = logEntriesConcatenated.Where(e => e.level >= program.statusPanelLogLevel).Select(e => $"[{e.airlock.group.Name}] {e.ToString()}").ToArray();
            foreach (Display panel in monitors)
            {
                int allowedChars = panel.getLineWidth();
                string header = "Central airlock monitoring panel";
                allowedChars -= header.Length;
                string prefix = "-", suffix = " | ";

                for (int i = 0; i < allowedChars / 2 - 4; i++)
                {
                    prefix += "=";
                    suffix += "=";
                }
                prefix += " | ";
                suffix += "-";
                header = prefix + header + suffix;
                panel.WriteText(logs, header);
            }
        }
    }
}