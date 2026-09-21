using LFSDriftTools.InSim;
using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static LFSDriftTools.DriftEngine;
using static LFSDriftTools.IndicatorManager;
using System.Reflection;

namespace LFSDriftTools
{
    public static class ApplePalette
    {

        public static readonly Color Blue =
            Color.FromArgb(0, 122, 255);

        public static readonly Color Green =
            Color.FromArgb(52, 199, 89);

        public static readonly Color Orange =
            Color.FromArgb(255, 159, 10);

        public static readonly Color Red =
            Color.FromArgb(255, 69, 58);

        public static readonly Color Purple =
            Color.FromArgb(175, 82, 222);

        public static Color Background { get; private set; } = Color.FromArgb(245, 245, 247);
        public static Color Card { get; private set; } = Color.White;
        public static Color Border { get; private set; } = Color.FromArgb(224, 224, 229);
        public static Color Title { get; private set; } = Color.FromArgb(28, 28, 30);
        public static Color Text { get; private set; } = Color.FromArgb(58, 58, 60);
        public static Color Secondary { get; private set; } = Color.FromArgb(120, 120, 128);

        public static Color StatusOk { get; private set; } = Color.FromArgb(60, 220, 100);
        public static Color StatusWarn { get; private set; } = Color.FromArgb(230, 170, 60);
        public static Color StatusError { get; private set; } = Color.FromArgb(220, 80, 80);
        public static Color StatusIdle { get; private set; } = Color.FromArgb(130, 130, 165);
        public static Color TextBlue { get; private set; } = Blue;
        public static Color TextGreen { get; private set; } = Green;
        public static Color TextOrange { get; private set; } = Orange;
        public static Color TextRed { get; private set; } = Red;
        public static Color TextPurple { get; private set; } = Purple;

        public static bool IsDark { get; private set; } = false;

        public static void SetDark(bool dark)
        {
            IsDark = dark;

            if (dark)
            {
                Background = Color.FromArgb(28, 28, 30);
                Card = Color.FromArgb(40, 40, 43);
                Border = Color.FromArgb(60, 60, 64);
                Title = Color.FromArgb(245, 245, 247);
                Text = Color.FromArgb(220, 220, 225);
                Secondary = Color.FromArgb(150, 150, 160);

                StatusOk = Color.FromArgb(60, 220, 100);
                StatusWarn = Color.FromArgb(230, 170, 60);
                StatusError = Color.FromArgb(220, 80, 80);
                StatusIdle = Color.FromArgb(130, 130, 165);
                TextBlue = Blue;
                TextGreen = Green;
                TextOrange = Orange;
                TextRed = Red;
                TextPurple = Purple;
            }
            else
            {
                Background = Color.FromArgb(245, 245, 247);
                Card = Color.White;
                Border = Color.FromArgb(224, 224, 229);
                Title = Color.FromArgb(28, 28, 30);
                Text = Color.FromArgb(58, 58, 60);
                Secondary = Color.FromArgb(120, 120, 128);

                StatusOk = Color.FromArgb(30, 140, 60);
                StatusWarn = Color.FromArgb(170, 110, 20);
                StatusError = Color.FromArgb(190, 40, 40);
                StatusIdle = Color.FromArgb(90, 90, 130);
                TextBlue = Color.FromArgb(0, 90, 200);
                TextGreen = Color.FromArgb(30, 140, 60);
                TextOrange = Color.FromArgb(200, 110, 0);
                TextRed = Color.FromArgb(200, 40, 30);
                TextPurple = Color.FromArgb(130, 50, 180);
            }
        }
    }
}
