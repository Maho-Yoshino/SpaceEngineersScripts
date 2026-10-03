// <mdk sortorder="997" />
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
    public class Display
    {
        private readonly IMyTextPanel panel;
        private readonly int lineWidth;
        private readonly int maxLines;
        public Display(Program program, IMyTextPanel panel)
        {
            this.panel = panel;

            MyIni _ini = program.ReadCustomData(panel);
            lineWidth = program.GetIniVal("display", "lineWidth", 35, ref _ini, panel);
            maxLines = program.GetIniVal("display", "maxLines", 20, ref _ini, panel);
            panel.ContentType = ContentType.TEXT_AND_IMAGE;
        }
        public int getLineWidth() => lineWidth;
        public void WriteText(IEnumerable<string> lines, string header = null)
        {
            int currentLines = 0;
            lines = TextUtils.WordWrap(lines.ToArray(), lineWidth).ToArray();
            lines = lines.Reverse();
            List<string> output = new List<string>();
            for (int i = 0; i < Math.Min(maxLines, lines.Count()); i++)
            {
                if (currentLines >= maxLines)
                    break;
                string line = lines.ElementAt(i);
                currentLines += TextUtils.LinesUsed(line);
                output.Add(line);
            }
            if (header != null)
            {
                output.RemoveAt(output.Count-1);
                output.Add(header);
            }
            output.Reverse();
            panel.WriteText(string.Join("\n", output));
        }
        public void WriteText(string line) => WriteText(new string[] {line});
    }
    public class TextUtils
    {
        public static string levelPrefix(Program program, Program.levels level)
        {
            return colorText($"| {program.levelToString[level]} |", program.levelColors[level]); 
        }
        public static string colorText(string text, string hex_value)
        {
            return $"[color=#FF{hex_value}]{text}[/color]";
        }
        public static int LinesUsed(string text)
        {
            int linesUsed = 1;
            linesUsed += text.Trim().Count(c => c == '\n');
            return linesUsed;
        }
        public static string WordWrap(string text, int line_length)
        {
            if (text.Length <= line_length)
                return text;
            string[] lines = text.Split('\n');
            List<string> output = new List<string>();	
            foreach (string line in lines)
            {
                if (line.Length <= line_length)
                {
                    output.Add(line);
                    continue;
                }
                StringBuilder final_output = new StringBuilder();
                int visible_length = 0;
                

                for (int i = 0; i < line.Length;)
                {
                    // Copy formatting tags without counting them as visible characters.
                    if (line[i] == '[')
                    {
                        int tag_end = line.IndexOf(']', i);

                        if (tag_end != -1)
                        {
                            final_output.Append(line, i, tag_end - i + 1);
                            i = tag_end + 1;
                            continue;
                        }
                    }

                    if (visible_length >= line_length)
                    {
                        final_output.Append("\n");

                        visible_length = 0;
                    }

                    final_output.Append(line[i]);
                    visible_length++;
                    i++;
                }
                output.Add(final_output.ToString());
            }
            return string.Join("\n", output);
        }
        public static string[] WordWrap(string[] lines, int line_length)
        {
            string[] output = new string[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                output[i] = string.Join("\n", WordWrap(lines[i], line_length));
            }
            return output;
        }
    }
}