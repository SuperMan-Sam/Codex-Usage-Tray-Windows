namespace CodexUsageTray.Core.Services.TokenMeter;

internal sealed record TokenFileFingerprint(long Length, DateTime LastWriteTimeUtc)
{
    public static TokenFileFingerprint FromFile(FileInfo file)
    {
        file.Refresh();
        return new TokenFileFingerprint(file.Exists ? file.Length : 0, file.Exists ? file.LastWriteTimeUtc : DateTime.MinValue);
    }
}
