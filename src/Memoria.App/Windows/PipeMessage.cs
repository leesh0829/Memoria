// src/Memoria.App/Windows/PipeMessage.cs
using System;

namespace Memoria.App.Windows;

public enum PipeCommand
{
    NewNote,
    Open,
}

public static class PipeMessage
{
    public static string Serialize(PipeCommand command) => command switch
    {
        PipeCommand.NewNote => "new-note",
        PipeCommand.Open => "open",
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
    };

    public static bool TryParse(string? line, out PipeCommand command)
    {
        command = default;
        switch (line?.Trim().ToLowerInvariant())
        {
            case "new-note":
                command = PipeCommand.NewNote;
                return true;
            case "open":
                command = PipeCommand.Open;
                return true;
            default:
                return false;
        }
    }
}
