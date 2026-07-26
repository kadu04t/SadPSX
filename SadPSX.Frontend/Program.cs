using SadPSX.Frontend.App;

namespace SadPSX.Frontend;

internal static class Program
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        return FrontendApplication.Run(arguments);
    }
}
