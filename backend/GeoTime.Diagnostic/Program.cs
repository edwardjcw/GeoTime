using GeoTime.Core.Compute;
using System;

Console.WriteLine("GeoTime GPU Diagnostic Utility");
Console.WriteLine("------------------------------");

try
{
    using var service = new GpuComputeService();
    var info = service.Info;

    Console.WriteLine($"Mode: {info.Mode}");
    Console.WriteLine($"Device Name: {info.DeviceName}");
    Console.WriteLine($"Accelerator Type: {info.AcceleratorType}");
    Console.WriteLine($"Memory (MB): {info.MemoryMb}");
    Console.WriteLine($"Is GPU Active: {service.IsGpuActive}");
}
catch (Exception ex)
{
    Console.WriteLine($"FATAL ERROR during GpuComputeService initialization: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
