namespace ThreadLogViewer.UiTests;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 3 || args[0] != "--ui-review")
        {
            Console.Error.WriteLine("UI scenarios: dotnet test. Gallery: --ui-review <published-exe> <output-directory>");
            return 1;
        }
        string exe = args[1];
        string output = args[2];
        try
        {
            UiSta.Run(() => UiReview.Run(exe, output), TimeSpan.FromMinutes(30));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
