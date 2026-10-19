using System.IO;
using System.Text;

namespace ThreadLogViewer.Tests;

internal static class SyntheticBenchmarkLog
{
    public static void Write(string path, int mib)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 65536);
        long total = 0, target = mib * 1024L * 1024;
        for (int i = 0; total < target; i++)
        {
            string line = i % 113 == 0 ? "DE AD BE EF 00 01 02 03 — synthetic dump without thread" : i % 227 == 0 ? "" :
                $"[{i / 3600:000}:{(i / 60) % 60:00}:{i % 60:00}] [T {i % 16}] {(i % 7 == 0 ? "합성 읽기 완료" : "Synthetic Write completed")} LBA={i:000000000} status=OK (testcase.cpp:{100 + i % 300})";
            writer.Write(line); writer.Write('\n');
            total += Encoding.UTF8.GetByteCount(line) + 1;
        }
    }
}
