namespace ThreadLogViewer.Core;

// Original physical line indexes, zero-based and inclusive at both ends.
public readonly record struct LogLineRange(int FirstLineIndex, int LastLineIndex)
{
    public bool Contains(int sourceLineIndex) => sourceLineIndex >= FirstLineIndex && sourceLineIndex <= LastLineIndex;

    public void Validate(LogData source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (FirstLineIndex < 0 || LastLineIndex < FirstLineIndex || LastLineIndex >= source.Lines.Count)
            throw new ArgumentOutOfRangeException(nameof(LogLineRange), "범위는 원본의 실제 줄 안에서 시작 줄부터 끝 줄까지 지정해야 합니다.");
    }
}
