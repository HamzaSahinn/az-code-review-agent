using System.Text;

namespace AzDoReviewAgent.Diff;

public sealed class DiffService : IDiffService
{
    private const int ContextLines = 3;

    public DiffResult ComputeDiff(string? baseContent, string headContent, string filePath, string changeType)
    {
        var result = new DiffResult();
        var fileChange = new DiffResult.FileChange
        {
            FilePath = filePath,
            ChangeType = changeType
        };

        var headLines = SplitLines(headContent);

        if (changeType.Equals("add", StringComparison.OrdinalIgnoreCase))
        {
            fileChange.Hunks = BuildAddedHunk(headLines);
        }
        else if (changeType.Equals("delete", StringComparison.OrdinalIgnoreCase))
        {
            var baseLines = SplitLines(baseContent ?? string.Empty);
            fileChange.Hunks = BuildRemovedHunk(baseLines);
        }
        else // edit
        {
            var baseLines = SplitLines(baseContent ?? string.Empty);
            fileChange.Hunks = ComputeEditHunks(baseLines, headLines);
        }

        result.FileChanges.Add(fileChange);
        return result;
    }

    public string FormatDiffForReview(DiffResult.FileChange fileChange, string headContent)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"File: {fileChange.FilePath} ({fileChange.ChangeType})");
        sb.AppendLine();

        foreach (var hunk in fileChange.Hunks)
        {
            sb.AppendLine($"@@ -{hunk.StartLineOld},{hunk.LineCountOld} +{hunk.StartLineNew},{hunk.LineCountNew} @@");

            foreach (var line in hunk.Lines)
            {
                char prefix = line.Type switch
                {
                    DiffLineType.Added => '+',
                    DiffLineType.Removed => '-',
                    _ => ' '
                };

                // Use new line number for added/context, old for removed
                int lineNum = line.Type == DiffLineType.Removed
                    ? (line.LineNumberOld ?? 0)
                    : (line.LineNumberNew ?? 0);

                sb.AppendLine($"{prefix}{lineNum,6}: {line.Content}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static string[] SplitLines(string content)
    {
        if (string.IsNullOrEmpty(content))
            return [];

        return content.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .ToArray();
    }

    private static List<DiffResult.DiffHunk> BuildAddedHunk(string[] lines)
    {
        if (lines.Length == 0)
            return [];

        var hunk = new DiffResult.DiffHunk
        {
            StartLineOld = 0,
            LineCountOld = 0,
            StartLineNew = 1,
            LineCountNew = lines.Length
        };

        for (int i = 0; i < lines.Length; i++)
        {
            hunk.Lines.Add(new DiffResult.DiffLine
            {
                Type = DiffLineType.Added,
                LineNumberOld = null,
                LineNumberNew = i + 1,
                Content = lines[i]
            });
        }

        return [hunk];
    }

    private static List<DiffResult.DiffHunk> BuildRemovedHunk(string[] lines)
    {
        if (lines.Length == 0)
            return [];

        var hunk = new DiffResult.DiffHunk
        {
            StartLineOld = 1,
            LineCountOld = lines.Length,
            StartLineNew = 0,
            LineCountNew = 0
        };

        for (int i = 0; i < lines.Length; i++)
        {
            hunk.Lines.Add(new DiffResult.DiffLine
            {
                Type = DiffLineType.Removed,
                LineNumberOld = i + 1,
                LineNumberNew = null,
                Content = lines[i]
            });
        }

        return [hunk];
    }

    private static List<DiffResult.DiffHunk> ComputeEditHunks(string[] baseLines, string[] headLines)
    {
        // Compute LCS edit script
        var editScript = ComputeLcsEditScript(baseLines, headLines);

        // Group changed regions with context
        return BuildHunksFromEditScript(editScript, baseLines, headLines);
    }

    /// <summary>
    /// Represents the type of operation in an edit script entry.
    /// </summary>
    private enum EditOp { Keep, Insert, Delete }

    private record EditEntry(EditOp Op, int OldIndex, int NewIndex);

    private static List<EditEntry> ComputeLcsEditScript(string[] baseLines, string[] headLines)
    {
        int m = baseLines.Length;
        int n = headLines.Length;

        // Build LCS length table
        var dp = new int[m + 1, n + 1];
        for (int i = m - 1; i >= 0; i--)
        {
            for (int j = n - 1; j >= 0; j--)
            {
                if (baseLines[i] == headLines[j])
                    dp[i, j] = dp[i + 1, j + 1] + 1;
                else
                    dp[i, j] = Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        // Trace back to build edit script
        var script = new List<EditEntry>();
        int oi = 0, ni = 0;
        while (oi < m && ni < n)
        {
            if (baseLines[oi] == headLines[ni])
            {
                script.Add(new EditEntry(EditOp.Keep, oi, ni));
                oi++;
                ni++;
            }
            else if (dp[oi + 1, ni] >= dp[oi, ni + 1])
            {
                script.Add(new EditEntry(EditOp.Delete, oi, -1));
                oi++;
            }
            else
            {
                script.Add(new EditEntry(EditOp.Insert, -1, ni));
                ni++;
            }
        }

        while (oi < m)
        {
            script.Add(new EditEntry(EditOp.Delete, oi, -1));
            oi++;
        }

        while (ni < n)
        {
            script.Add(new EditEntry(EditOp.Insert, -1, ni));
            ni++;
        }

        return script;
    }

    private static List<DiffResult.DiffHunk> BuildHunksFromEditScript(
        List<EditEntry> script,
        string[] baseLines,
        string[] headLines)
    {
        // Mark changed positions
        var changedEntries = new HashSet<int>(
            script.Select((e, idx) => (e, idx))
                  .Where(x => x.e.Op != EditOp.Keep)
                  .Select(x => x.idx));

        if (changedEntries.Count == 0)
            return [];

        // Compute ranges [start..end] of entries that should appear in hunks (including context)
        var ranges = new List<(int start, int end)>();
        foreach (int ci in changedEntries)
        {
            int start = Math.Max(0, ci - ContextLines);
            int end = Math.Min(script.Count - 1, ci + ContextLines);
            ranges.Add((start, end));
        }

        // Merge overlapping ranges
        ranges.Sort((a, b) => a.start.CompareTo(b.start));
        var merged = new List<(int start, int end)>();
        foreach (var (s, e) in ranges)
        {
            if (merged.Count > 0 && s <= merged[^1].end + 1)
                merged[^1] = (merged[^1].start, Math.Max(merged[^1].end, e));
            else
                merged.Add((s, e));
        }

        var hunks = new List<DiffResult.DiffHunk>();
        foreach (var (rangeStart, rangeEnd) in merged)
        {
            var hunk = new DiffResult.DiffHunk();
            bool firstLine = true;

            int oldLineNum = 0;
            int newLineNum = 0;
            int hunkOldCount = 0;
            int hunkNewCount = 0;

            for (int i = rangeStart; i <= rangeEnd; i++)
            {
                var entry = script[i];

                if (firstLine)
                {
                    // Determine starting line numbers from the entry context
                    oldLineNum = entry.Op == EditOp.Insert ? GetPrevOldLine(script, i) : entry.OldIndex + 1;
                    newLineNum = entry.Op == EditOp.Delete ? GetPrevNewLine(script, i) : entry.NewIndex + 1;
                    hunk.StartLineOld = oldLineNum;
                    hunk.StartLineNew = newLineNum;
                    firstLine = false;
                }

                switch (entry.Op)
                {
                    case EditOp.Keep:
                        hunk.Lines.Add(new DiffResult.DiffLine
                        {
                            Type = DiffLineType.Context,
                            LineNumberOld = entry.OldIndex + 1,
                            LineNumberNew = entry.NewIndex + 1,
                            Content = baseLines[entry.OldIndex]
                        });
                        hunkOldCount++;
                        hunkNewCount++;
                        break;

                    case EditOp.Delete:
                        hunk.Lines.Add(new DiffResult.DiffLine
                        {
                            Type = DiffLineType.Removed,
                            LineNumberOld = entry.OldIndex + 1,
                            LineNumberNew = null,
                            Content = baseLines[entry.OldIndex]
                        });
                        hunkOldCount++;
                        break;

                    case EditOp.Insert:
                        hunk.Lines.Add(new DiffResult.DiffLine
                        {
                            Type = DiffLineType.Added,
                            LineNumberOld = null,
                            LineNumberNew = entry.NewIndex + 1,
                            Content = headLines[entry.NewIndex]
                        });
                        hunkNewCount++;
                        break;
                }
            }

            hunk.LineCountOld = hunkOldCount;
            hunk.LineCountNew = hunkNewCount;
            hunks.Add(hunk);
        }

        return hunks;
    }

    private static int GetPrevOldLine(List<EditEntry> script, int idx)
    {
        for (int i = idx - 1; i >= 0; i--)
        {
            if (script[i].OldIndex >= 0)
                return script[i].OldIndex + 2; // next line after last known old
        }
        return 1;
    }

    private static int GetPrevNewLine(List<EditEntry> script, int idx)
    {
        for (int i = idx - 1; i >= 0; i--)
        {
            if (script[i].NewIndex >= 0)
                return script[i].NewIndex + 2;
        }
        return 1;
    }
}
