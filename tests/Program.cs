using System;
using KitchenChaos.Multiplayer;
public static class Program {
    public static int Main() {
        try { RegressionSuite.Run(Console.WriteLine);return 0; }
        catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
    }
}
