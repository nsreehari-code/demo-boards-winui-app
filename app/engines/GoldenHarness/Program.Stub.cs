using System;

namespace GoldenHarness;

internal static class Program
{
    private static int Main()
    {
        Console.Error.WriteLine("GoldenHarness is disabled by default because it depends on deprecated.DemoBoards.RuntimeHost.");
        Console.Error.WriteLine("Set EnableDeprecatedRuntimeHostHarnesses=true to build and run the deprecated runtime harnesses.");
        return 1;
    }
}