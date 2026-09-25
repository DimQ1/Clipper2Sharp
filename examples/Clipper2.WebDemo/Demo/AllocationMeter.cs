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
///
/// One operation is shorter than the counter's granularity, which matters for the
/// animation: there each frame performs exactly one operation. The meter therefore
/// keeps the last few single-operation deltas and reports their average, so the
/// animation shows a real per-operation figure that updates a few times a second.
/// The deltas are read around the operation calls only, so the page's own
/// rendering never lands in the measurement.
/// </summary>
public static class AllocationMeter
{
  private const int WindowSize = 8;
  private static readonly long[] Window = new long[WindowSize];
  private static readonly int[] WindowOps = new int[WindowSize];
  private static int _filled;
  private static int _next;

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

  /// <summary>Adds the bytes one operation (or a few of them) allocated.</summary>
  public static void Add(long bytes, int operations)
  {
    if (bytes < 0 || operations <= 0) return;
    Window[_next] = bytes;
    WindowOps[_next] = operations;
    _next = (_next + 1) % WindowSize;
    if (_filled < WindowSize) _filled++;
  }

  /// <summary>The average cost per operation over the last few single-operation calls.</summary>
  public static bool TryAveragePerOp(out double bytesPerOp)
  {
    bytesPerOp = 0;
    if (_filled == 0) return false;

    long bytes = 0;
    int ops = 0;
    for (int i = 0; i < _filled; i++)
    {
      bytes += Window[i];
      ops += WindowOps[i];
    }
    if (ops == 0) return false;

    bytesPerOp = bytes / (double) ops;
    return true;
  }

  public static void Reset()
  {
    _filled = 0;
    _next = 0;
    Array.Clear(Window);
    Array.Clear(WindowOps);
  }
}
