// Global using directives — replaces ImplicitUsings for .NET Framework 4.8
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;

// Polyfill: Required for C# 9+ record/init support on .NET Framework 4.8
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
