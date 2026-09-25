using System;

namespace Clipper2.WebDemo.Demo;

/// <summary>
/// The allocation counters behave differently per runtime. On .NET (CoreCLR) the
/// per thread counter is exact and cheap; the process wide counter with
/// precise: false only moves during a collection, so inside a WebAssembly tab it
/// can stay still while thousands of operations allocate. On Mono the per thread
/// counter may not be implemented at all.
///
/// So the demo asks for the per thread counter first (cheap, works per animation
/// frame), falls back to the precise process counter when it is missing, and says
/// "not measured" rather than printing a zero it cannot stand behind.
/// </summary>
public static class AllocationMeter
{
  private static bool _threadCounterUsable = true;
  private static bool _preciseUsable = true;

  /// <summary>-1 when the runtime cannot answer.</summary>
  public static long Read()
  {
    if (_threadCounterUsable)
    {
      try
      {
        return GC.GetAllocatedBytesForCurrentThread();
      }
      catch (Exception)
      {
        _threadCounterUsable = false;
      }
    }

    if (_preciseUsable)
    {
      try
      {
        // forces a collection, so only suitable for a single manual measurement
        return GC.GetTotalAllocatedBytes(true);
      }
      catch (Exception)
      {
        _preciseUsable = false;
      }
    }

    return -1;
  }

  /// <summary>True when a measurement pair came from a working counter.</summary>
  public static bool Measured(long before, long after) => before >= 0 && after >= 0 && after >= before;
}
