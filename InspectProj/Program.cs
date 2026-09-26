using System;
using System.Linq;
using System.Reflection;

class Program
{
    static void Main()
    {
        try {
            var paperPath = @"C:\Users\LENOVO\.nuget\packages\prowl.paper\3.5.0\lib\net10.0\Paper.dll";
            var paperAsm = Assembly.LoadFrom(paperPath);
            foreach (var t in paperAsm.GetTypes().OrderBy(t => t.FullName ?? t.Name))
            {
                var members = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Select(m => m.Name)
                    .Distinct()
                    .OrderBy(n => n);
                string memberStr = string.Join(", ", members);
                if (memberStr.Contains("Focus") || memberStr.Contains("Tab") || memberStr.Contains("Key"))
                    Console.WriteLine($"{t.FullName ?? t.Name}: {memberStr}");
            }
        } catch (Exception ex) {
            Console.WriteLine("Error: " + ex.Message);
        }
    }
}
