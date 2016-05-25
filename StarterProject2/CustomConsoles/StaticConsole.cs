﻿namespace StarterProject.CustomConsoles
{
    using System;
    using SadConsole;
    using SadConsole.Consoles;
    using Console = SadConsole.Consoles.Console;
    using Microsoft.Xna.Framework;

    /// <summary>
    /// Demonstrates a console that doesn't accept keyboard input, doesn't show the cursor (both defaults for a console), and just displays some custom text.
    /// </summary>
    class StaticConsole: Console
    {
        public StaticConsole()
            : base(80, 25)
        {
            // Prints a string using the cellData default foreground and background.
            _textSurface.Print(2, 1, "Text written with the default foreground and background");

            // Prints a string using the ColoredString class.
            _textSurface.Print(2, 2, new ColoredString("Text using a colored string", Color.LightBlue, Color.Transparent, null));

            // Creates a new ColoredString from an existing string and applies a gradient color to each character.
            ColoredString colorString = "Text using a colored string gradient".CreateGradient(Color.DarkGreen, Color.LightGreen, null);
            _textSurface.Print(2, 3, colorString);

            // Appends a new ColoredString to the existing ColoredString with a new color gradient.
            colorString += " with another gradient applied".CreateGradient(Color.DarkBlue, Color.LightBlue, null);
            _textSurface.Print(2, 4, colorString);

            // Prints a string, then changes the foreground of a single cell.
            _textSurface.Print(2, 5, "Text written with defaults, then the foreground changed");
            _textSurface.SetForeground(17, 5, Color.Yellow);

            // Prints a string, then changes the background of a single cell.
            _textSurface.Print(2, 6, "Text written with defaults, then the background changed");
            _textSurface.SetBackground(17, 6, Color.DarkGray);

            // Prints a string, then changes the foreground and background of a single cell.
            _textSurface.Print(2, 7, "Text written with defaults, then the foreground and background changed");
            _textSurface.SetBackground(17, 7, Color.White);
            _textSurface.SetForeground(17, 7, Color.Black);

            // Prints a string, then changes the effect of the cell to a Blink effect.
            _textSurface.Print(2, 8, "Text written with defaults, then the blink effect applied");
            _textSurface.SetEffect(17, 8, new SadConsole.Effects.Blink() { BlinkSpeed = 0.5f });

            IsVisible = false;
        }
    }
}