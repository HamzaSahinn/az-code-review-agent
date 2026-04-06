namespace AzDoReviewAgent.Diff;

public sealed class DiffResult
{
    public List<FileChange> FileChanges { get; set; } = [];

    public sealed class FileChange
    {
        public string FilePath { get; set; } = string.Empty;
        public string ChangeType { get; set; } = string.Empty;
        public List<DiffHunk> Hunks { get; set; } = [];
    }

    public sealed class DiffHunk
    {
        public int StartLineOld { get; set; }
        public int LineCountOld { get; set; }
        public int StartLineNew { get; set; }
        public int LineCountNew { get; set; }
        public List<DiffLine> Lines { get; set; } = [];
    }

    public sealed class DiffLine
    {
        public DiffLineType Type { get; set; }
        public int? LineNumberOld { get; set; }
        public int? LineNumberNew { get; set; }
        public string Content { get; set; } = string.Empty;
    }
}

public enum DiffLineType
{
    Context,
    Added,
    Removed
}
