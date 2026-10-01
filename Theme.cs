using System;
using System.Drawing;

namespace DgusPlus
{
    // Палитра надстройки. Применение темы к окнам DGUS — ThemeApply.cs.
    static partial class Theme
    {
        public static Color Bg, Panel, PanelAlt, Border, Input, Text, TextDim, Accent,
                            Selection, Hover, Warn, Shared, InputMark, OutputMark, Canvas, Button, ButtonHover;

        public static bool Dark { get { return Plus.Cfg.Theme == "dark"; } }

        static Theme() { LoadPalette(); }

        public static void LoadPalette()
        {
            if (Plus.Cfg != null && Plus.Cfg.Theme == "dark")
            {
                Bg = C(0x1E1F22); Panel = C(0x2B2D30); PanelAlt = C(0x25272A); Border = C(0x3C3F43);
                Input = C(0x1E1F22); Text = C(0xDFE1E5); TextDim = C(0x9A9DA5); Accent = C(0x3D7EFF);
                Selection = C(0x2E4470); Hover = C(0x35383C); Warn = C(0xFF5F6D); Shared = C(0xE3A33B);
                InputMark = C(0x5DBB75); OutputMark = C(0x5C93F7); Canvas = C(0x1B1C1F);
                Button = C(0x3A3D42); ButtonHover = C(0x474B51);
            }
            else
            {
                Bg = C(0xE9EBEF); Panel = C(0xF7F8FA); PanelAlt = C(0xEEF0F3); Border = C(0xD5D9E0);
                Input = C(0xFFFFFF); Text = C(0x1F2329); TextDim = C(0x6B7280); Accent = C(0x2F6FEB);
                Selection = C(0xD3E2FF); Hover = C(0xE8EEFB); Warn = C(0xD92D3A); Shared = C(0xB7791F);
                InputMark = C(0x2E9E4F); OutputMark = C(0x2F6FEB); Canvas = C(0xDADDE3);
                Button = C(0xFFFFFF); ButtonHover = C(0xEEF2FA);
            }
            LoadRibbon();
        }

        static Color C(int rgb) { return Color.FromArgb(255, Color.FromArgb(rgb)); }
    }
}
