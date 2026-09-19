using System;
using System.Reflection;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Xunit;

namespace OmniPadServer.Tests;

public class ViGEmDualShock4Tests
{
    [Fact]
    public void InspectMethods()
    {
        foreach (var m in typeof(IDualShock4Controller).GetMethods())
        {
            var pList = string.Join(", ", Array.ConvertAll(m.GetParameters(), p => $"{p.ParameterType.Name} {p.Name}"));
            Console.WriteLine($"{m.ReturnType.Name} {m.Name}({pList})");
        }
    }
}
