namespace WorkLogger.Core.Model;

public sealed record CommitInfo(
    string Repo,
    string Sha,
    string Branch,
    string Message,
    DateTimeOffset Timestamp,
    int FilesChanged = 0,
    int Insertions = 0,
    int Deletions = 0)
{
    public DateOnly Day => DateOnly.FromDateTime(Timestamp.LocalDateTime);
}
