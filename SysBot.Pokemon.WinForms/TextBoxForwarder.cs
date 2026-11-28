using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using SysBot.Base;

namespace SysBot.Pokemon.WinForms;

/// <summary>
/// Forward logs to a TextBox with optional per-game filtering.
/// </summary>
public sealed class TextBoxForwarder(TextBoxBase Box, Func<string, GameMode?>? ResolveMode = null) : ILogForwarder
{
    private const int MaxEntries = 500;

    /// <summary>
    /// Synchronize access to the buffer and TextBox. Only the GUI thread should be writing to the control.
    /// </summary>
    private readonly Lock _logLock = new();
    private readonly Queue<(GameMode? Mode, string Line)> _entries = new();
    private GameMode? _filter;

    public void Forward(string message, string identity)
    {
        var mode = ResolveMode?.Invoke(identity);
        var tag = mode is { } m ? $"[{m}] " : string.Empty;
        var line = $"[{DateTime.Now:HH:mm:ss}] - {tag}{identity}: {message}{Environment.NewLine}";

        bool shouldAppend;
        lock (_logLock)
        {
            _entries.Enqueue((mode, line));
            while (_entries.Count > MaxEntries)
                _entries.Dequeue();

            shouldAppend = _filter is null || mode == _filter;
        }

        if (!shouldAppend || Box.IsDisposed)
            return;

        if (Box.InvokeRequired)
            Box.BeginInvoke((MethodInvoker)(() => UpdateLog(line)));
        else
            UpdateLog(line);
    }

    public void SetFilter(GameMode? mode)
    {
        string snapshot;
        lock (_logLock)
        {
            _filter = mode;
            var sb = new StringBuilder();
            foreach (var (entryMode, line) in _entries)
            {
                if (mode is null || entryMode == mode)
                    sb.Append(line);
            }
            snapshot = sb.ToString();
        }

        if (Box.IsDisposed)
            return;

        if (Box.InvokeRequired)
            Box.BeginInvoke((MethodInvoker)(() => RebuildLog(snapshot)));
        else
            RebuildLog(snapshot);
    }

    private void RebuildLog(string text)
    {
        if (Box.IsDisposed)
            return;

        Box.Clear();
        if (text.Length == 0)
            return;

        var max = Box.MaxLength;
        if (max > 0 && text.Length >= max)
            text = text[^(max - 1)..];

        Box.AppendText(text);
    }

    private void UpdateLog(string line)
    {
        if (Box.IsDisposed)
            return;

        // If we exceed the MaxLength, remove the top 1/4 of the lines.
        // Don't change .Text directly; truncating to the middle of a line distorts the log formatting.
        var text = Box.Text;
        var max = Box.MaxLength;
        if (text.Length + line.Length + 2 >= max)
        {
            var lines = Box.Lines;
            Box.Lines = lines[(lines.Length / 4)..];
            Box.Refresh();
        }

        Box.AppendText(line);
    }
}
