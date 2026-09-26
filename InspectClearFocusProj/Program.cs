using System;
using System.Linq;
using System.Reflection;

class Program
{
    static void Main()
    {
        var path = @"C:\Users\LENOVO\.nuget\packages\prowl.paper\3.5.0\lib\net10.0\Paper.dll";
        try
        {
            var asm = Assembly.LoadFrom(path);
            foreach (var type in asm.GetTypes().Where(t => t.IsPublic))
            {
                foreach (var method in type.GetMethods().Where(m => m.IsPublic && (m.Name.Contains("Clear") || m.Name.Contains("Focus"))))
                {
                    var ps = string.Join(",", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    Console.WriteLine($"{type.Name}::{method.Name}({ps}) -> {method.ReturnType.Name}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }
    }
}
