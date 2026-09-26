using System;
using System.Linq;
using System.Reflection;

class Program
{
    static void Main()
    {
        var asm = Assembly.LoadFrom(@"C:\Users\LENOVO\.nuget\packages\prowl.paper\3.5.0\lib\net10.0\Paper.dll");
        foreach (var type in asm.GetTypes().Where(t => t.Name.Contains("Paper") || t.IsPublic))
        {
            foreach (var method in type.GetMethods().Where(m => m.IsPublic && (m.Name.Contains("Focus") || m.Name.Contains("Clear"))))
            {
                Console.WriteLine($"{type.FullName}::{method.Name}({string.Join(",", method.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))}) -> {method.ReturnType.Name}");
            }
        }
    }
}
