namespace RoseMod;

public static class RoseModEntrypoint
{
    public static int StartFromNative(IntPtr gameRoot)
    {
        Start(System.Runtime.InteropServices.Marshal.PtrToStringUni(gameRoot) ?? Environment.CurrentDirectory);
        return 0;
    }

    public static int StartFromNativeMono()
    {
        Start();
        return 0;
    }

    public static void Start()
    {
        RoseModRuntime.Start(RoseModStartupOptions.AutoDetect());
    }

    public static void Start(string gameRoot)
    {
        RoseModRuntime.Start(RoseModStartupOptions.AutoDetect(gameRoot));
    }
}
