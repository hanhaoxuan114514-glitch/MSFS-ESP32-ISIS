using System;
using A350Isis;

internal static class IdentityTests
{
    private static int failures;
    private static void Check(bool value, string name)
    {
        if (!value) { Console.WriteLine("FAIL " + name); failures++; }
    }
    public static int Main()
    {
        string root = @"C:\Instrument Project";
        DateTime started = new DateTime(2026,11,8,12,0,0,DateTimeKind.Utc);
        string script = root + @"\windows-bridge\bridge.py";
        string python = root + @"\tools\python311\tools\python.exe";
        string command = "\"" + python + "\" -u \"" + script + "\" --port COM6";
        Check(ProcessIdentity.IsProjectBridge(root, started, started, python, command), "project process accepted");
        Check(!ProcessIdentity.IsProjectBridge(root, started, started.AddSeconds(1), python, command), "reused PID rejected");
        Check(!ProcessIdentity.IsProjectBridge(root, started, started, @"C:\Other\python.exe", command), "foreign interpreter rejected");
        Check(!ProcessIdentity.IsProjectBridge(root, started, started, python, "python.exe unrelated.py --label \"" + script + ".bak\""), "substring is not script argument");
        Check(ProcessIdentity.IsProjectBridge(root, started, started, @"W:\tools\espressif\python_env\idf5.3_py3.11_env\Scripts\python.exe", @"python.exe -u W:\windows-bridge\bridge.py --port COM6"), "legacy mapped project accepted");
        Check(!ProcessIdentity.ValidPort("COM6 --other"), "invalid port rejected");
        Check(ProcessIdentity.ValidPort("COM6"), "normal port accepted");
        Console.WriteLine(failures == 0 ? "All identity tests passed." : "Identity tests failed.");
        return failures == 0 ? 0 : 1;
    }
}
