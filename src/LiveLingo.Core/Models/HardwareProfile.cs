using System.Runtime.InteropServices;

namespace LiveLingo.Core.Models;

/// <summary>Effective local compute backend available to the active inference runtime.</summary>
public enum ComputeBackend
{
    /// <summary>Apple Silicon unified memory with Metal GPU offload (<c>osx-arm64</c>).</summary>
    AppleMetal,

    /// <summary>Dedicated GPU accelerator with isolated video memory.</summary>
    DiscreteGpu,

    /// <summary>CPU-only inference (either no supported GPU or a CPU-compiled llama-server runtime).</summary>
    CpuOnly
}

/// <summary>
/// Immutable snapshot of host compute and memory capabilities, together with physically grounded residency budgets
/// for running LiveLingo alongside an active OS, IDE and browser without swap thrashing.
/// </summary>
public sealed record HardwareProfile(
    string DeviceName,
    ComputeBackend Backend,
    long TotalMemoryBytes,
    int LogicalCores,
    long? DedicatedVideoMemoryBytes = null)
{
    internal const long MiB = 1024L * 1024;
    internal const long GiB = 1024L * 1024 * 1024;

    /// <summary>
    /// Maximum combined resident footprint (Speech + Cleaner + Translator) that may stay warm concurrently
    /// (45% of physical RAM, leaving ≥55% for OS wired pages and foreground applications).
    /// </summary>
    public const double CoResidentMemoryRatio = 0.45;

    /// <summary>
    /// Maximum single-session resident footprint (minimum companion + one active stage model) before memory
    /// pressure degrades foreground interactivity (41.5% of physical RAM).
    /// </summary>
    public const double SingleRoleCeilingRatio = 0.415;

    /// <summary>VRAM headroom reserved for the desktop compositor and foreground GPU clients (1.2 GiB hard / 1.5 GiB co-resident).</summary>
    internal const long VramSingleReserveBytes = 1_228 * MiB;
    internal const long VramCoResidentReserveBytes = 1_536 * MiB;

    public long CoResidentBudgetBytes => (long)(TotalMemoryBytes * CoResidentMemoryRatio);

    public long SingleRoleCeilingBytes => (long)(TotalMemoryBytes * SingleRoleCeilingRatio);

    public long? VramCoResidentBudgetBytes =>
        Backend == ComputeBackend.DiscreteGpu && DedicatedVideoMemoryBytes is { } vram
            ? Math.Max(0, vram - VramCoResidentReserveBytes)
            : null;

    public long? VramSingleRoleCeilingBytes =>
        Backend == ComputeBackend.DiscreteGpu && DedicatedVideoMemoryBytes is { } vram
            ? Math.Max(0, vram - VramSingleReserveBytes)
            : null;

    /// <summary>
    /// Maximum quantized GGUF weight size that runs interactively on CPU without approaching the 8 s slow-processing banner,
    /// derived from sustained dual-channel DDR4/DDR5 AVX2 bandwidth (~35–50 GB/s).
    /// </summary>
    public long CpuInteractiveWeightCeilingBytes =>
        LogicalCores >= 12 && TotalMemoryBytes >= 22 * GiB ? 3_000 * MiB : 1_800 * MiB;

    public int TotalMemoryGb => Math.Max(1, (int)Math.Round(TotalMemoryBytes / (double)GiB));

    public int? DedicatedVideoMemoryGb =>
        DedicatedVideoMemoryBytes is { } vram and > 0
            ? Math.Max(1, (int)Math.Round(vram / (double)GiB))
            : null;

    /// <summary>Product validation baseline (Apple M2+ / 16 GB unified memory), used as deterministic fallback in headless tests.</summary>
    public static HardwareProfile Baseline16GbAppleMetal { get; } =
        new("Apple Silicon", ComputeBackend.AppleMetal, 16 * GiB, 8);
}

public interface IHardwareProbe
{
    HardwareProfile Detect();
}

/// <summary>Detects host memory, logical cores and the effective acceleration backend of the pinned runtime.</summary>
public sealed class SystemHardwareProbe : IHardwareProbe
{
    public HardwareProfile Detect()
    {
        var memory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        if (memory <= 0)
            memory = 16 * HardwareProfile.GiB;
        var cores = Math.Max(1, Environment.ProcessorCount);
        var brand = OperatingSystem.IsMacOS() ? ReadMacSysctlString("machdep.cpu.brand_string") : null;
        return FromFacts(RuntimeInformation.RuntimeIdentifier, brand, memory, cores);
    }

    public static HardwareProfile FromFacts(
        string runtimeIdentifier,
        string? processorBrand,
        long totalMemoryBytes,
        int logicalCores,
        long? dedicatedVideoMemoryBytes = null)
    {
        var clampedMemory = Math.Max(HardwareProfile.GiB, totalMemoryBytes);
        var clampedCores = Math.Max(1, logicalCores);
        var cleanBrand = string.IsNullOrWhiteSpace(processorBrand) ? null : processorBrand.Trim();

        if (string.Equals(runtimeIdentifier, "osx-arm64", StringComparison.OrdinalIgnoreCase))
        {
            return new HardwareProfile(
                cleanBrand ?? "Apple Silicon",
                ComputeBackend.AppleMetal,
                clampedMemory,
                clampedCores);
        }

        if (dedicatedVideoMemoryBytes is > 0)
        {
            return new HardwareProfile(
                cleanBrand ?? $"{clampedCores}-core PC",
                ComputeBackend.DiscreteGpu,
                clampedMemory,
                clampedCores,
                dedicatedVideoMemoryBytes);
        }

        return new HardwareProfile(
            cleanBrand ?? $"{clampedCores}-core CPU",
            ComputeBackend.CpuOnly,
            clampedMemory,
            clampedCores);
    }

    private static string? ReadMacSysctlString(string name)
    {
        try
        {
            nuint length = 0;
            if (SysctlByName(name, IntPtr.Zero, ref length, IntPtr.Zero, 0) != 0 || length <= 1)
                return null;

            var buffer = Marshal.AllocHGlobal((int)length);
            try
            {
                if (SysctlByName(name, buffer, ref length, IntPtr.Zero, 0) != 0)
                    return null;
                return Marshal.PtrToStringUTF8(buffer)?.TrimEnd('\0').Trim();
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return null;
        }
    }

    [DllImport("libc", EntryPoint = "sysctlbyname")]
    private static extern int SysctlByName([MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr oldp, ref nuint oldlenp, IntPtr newp, nuint newlen);
}
