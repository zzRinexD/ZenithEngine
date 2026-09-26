using System;
using System.Linq;
using System.Reflection;

class Inspect
{
    static void Main()
    {
        var path = @"C:\Users\LENOVO\.nuget\packages\prowl.origami\3.5.0\lib\net10.0\Origami.dll";
        var asm = Assembly.LoadFrom(path);
        foreach (var t in asm.GetTypes().OrderBy(t => t.Name))
        {
            if (t.Name.Contains("Button") || t.Name.Contains("Origami") || t.Name.Contains("Focus") || t.Name.Contains("Paper"))
            {
                Console.WriteLine($"{t.Namespace}.{t.Name}");
            }
        }
    }
}
