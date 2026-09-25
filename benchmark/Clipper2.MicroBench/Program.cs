// Micro benchmarks behind docs/optimization-plan.md 2.2 - the measurements the
// optimisation decisions rest on, kept so they can be re-run on other machines:
//
//   * orientation predicates (sign of a*b - c*d): Int128, Math.BigMul, the
//     64 bit tier for factors below 2^31, and a floating point filter (rejected:
//     slower than the exact integer path on integer input);
//   * GC write barriers: the same AEL/SEL list surgery on objects linked by
//     references and on a struct array linked by int indices;
//   * the intersection list sort on nodes that hold references vs plain data,
//     and a branchless comparer (rejected: no faster).
//
//   dotnet run -c Release --project benchmark/Clipper2.MicroBench
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

static class Program
{
  // ---------------------------------------------------------------- predicates
  const double U5 = 5.551115123125783e-16; // 5 * 2^-53: static filter bound (inputs may be rounded doubles)

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  static int Int128Sign(long a, long b, long c, long d)
  {
    Int128 ab = (Int128) a * b; Int128 cd = (Int128) c * d;
    if (ab > cd) return 1; if (ab < cd) return -1; return 0;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  static int BigMulSign(long a, long b, long c, long d)
  {
    long hi1 = Math.BigMul(a, b, out long lo1);
    long hi2 = Math.BigMul(c, d, out long lo2);
    if (hi1 != hi2) return hi1 > hi2 ? 1 : -1;
    ulong u1 = (ulong) lo1, u2 = (ulong) lo2;
    return u1 > u2 ? 1 : (u1 < u2 ? -1 : 0);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  static int Tier1Sign(long a, long b, long c, long d) // 64-bit exact when all |v| <= 2^31, else BigMul
  {
    long m = (a ^ (a >> 63)) | (b ^ (b >> 63)) | (c ^ (c >> 63)) | (d ^ (d >> 63));
    if ((ulong) m < (1UL << 31))
    {
      long ab = a * b, cd = c * d;
      return ab > cd ? 1 : (ab < cd ? -1 : 0);
    }
    return BigMulSign(a, b, c, d);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  static int TieredSign(long a, long b, long c, long d) // 64-bit exact -> double filter -> BigMul exact
  {
    long m = (a ^ (a >> 63)) | (b ^ (b >> 63)) | (c ^ (c >> 63)) | (d ^ (d >> 63));
    if ((ulong) m < (1UL << 31))
    {
      long ab = a * b, cd = c * d;
      return ab > cd ? 1 : (ab < cd ? -1 : 0);
    }
    double dab = (double) a * (double) b, dcd = (double) c * (double) d;
    double det = dab - dcd;
    double bound = (Math.Abs(dab) + Math.Abs(dcd)) * U5;
    if (det > bound) return 1;
    if (det < -bound) return -1;
    return BigMulSign(a, b, c, d);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  static bool Int128Equal(long a, long b, long c, long d) => (Int128) a * b == (Int128) c * d;

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  static bool BigMulEqual(long a, long b, long c, long d)
  {
    long hi1 = Math.BigMul(a, b, out long lo1);
    long hi2 = Math.BigMul(c, d, out long lo2);
    return hi1 == hi2 && lo1 == lo2;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  static bool TieredEqual(long a, long b, long c, long d)
  {
    long m = (a ^ (a >> 63)) | (b ^ (b >> 63)) | (c ^ (c >> 63)) | (d ^ (d >> 63));
    if ((ulong) m < (1UL << 31)) return a * b == c * d;
    return BigMulEqual(a, b, c, d);
  }

  static long[] Gen(int n, long range, int seed, bool nearCollinear)
  {
    Random r = new Random(seed);
    long[] q = new long[n * 4];
    for (int i = 0; i < n; i++)
    {
      long a = (long) ((r.NextDouble() * 2 - 1) * range);
      long b = (long) ((r.NextDouble() * 2 - 1) * range);
      long c = (long) ((r.NextDouble() * 2 - 1) * range);
      long d;
      if (nearCollinear)
      {
        if (c == 0) c = 1;
        d = (long) ((double) a * b / c) + (r.Next(3) - 1);
      }
      else d = (long) ((r.NextDouble() * 2 - 1) * range);
      q[i * 4] = a; q[i * 4 + 1] = b; q[i * 4 + 2] = c; q[i * 4 + 3] = d;
    }
    return q;
  }

  static double TimeIt(string name, long[] q, Func<long[], long> body)
  {
    double best = double.MaxValue; long chk = 0;
    for (int rep = 0; rep < 7; rep++)
    {
      Stopwatch sw = Stopwatch.StartNew();
      chk = body(q);
      sw.Stop();
      best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
    }
    int n = q.Length / 4;
    Console.WriteLine($"  {name,-24} {best * 1e6 / n,7:0.00} ns/call   (chk {chk})");
    return best;
  }

  static void RunPredicates()
  {
    const int N = 4_000_000;
    (string, long[])[] sets =
    {
      ("small |v|<5e6 (test data)", Gen(N, 5_000_000, 1, false)),
      ("small near-collinear", Gen(N, 5_000_000, 2, true)),
      ("large |v|<5e11", Gen(N, 500_000_000_000, 3, false)),
      ("large near-collinear", Gen(N, 500_000_000_000, 4, true)),
    };
    foreach ((string label, long[] q) in sets)
    {
      int n = q.Length / 4; int bad = 0, badEq = 0;
      for (int i = 0; i < n; i++)
      {
        long a = q[i * 4], b = q[i * 4 + 1], c = q[i * 4 + 2], d = q[i * 4 + 3];
        int s = Int128Sign(a, b, c, d);
        if (BigMulSign(a, b, c, d) != s || Tier1Sign(a, b, c, d) != s || TieredSign(a, b, c, d) != s) bad++;
        bool e = Int128Equal(a, b, c, d);
        if (BigMulEqual(a, b, c, d) != e || TieredEqual(a, b, c, d) != e) badEq++;
      }
      Console.WriteLine($"{label}: disagreements with Int128: sign={bad} equal={badEq}");
      TimeIt("Int128 (port today)", q, qq => { long s = 0; for (int i = 0; i < qq.Length; i += 4) s += Int128Sign(qq[i], qq[i + 1], qq[i + 2], qq[i + 3]); return s; });
      TimeIt("BigMul exact", q, qq => { long s = 0; for (int i = 0; i < qq.Length; i += 4) s += BigMulSign(qq[i], qq[i + 1], qq[i + 2], qq[i + 3]); return s; });
      TimeIt("64-bit tier + BigMul", q, qq => { long s = 0; for (int i = 0; i < qq.Length; i += 4) s += Tier1Sign(qq[i], qq[i + 1], qq[i + 2], qq[i + 3]); return s; });
      TimeIt("64-bit+filter+BigMul", q, qq => { long s = 0; for (int i = 0; i < qq.Length; i += 4) s += TieredSign(qq[i], qq[i + 1], qq[i + 2], qq[i + 3]); return s; });
      TimeIt("Int128 equality", q, qq => { long s = 0; for (int i = 0; i < qq.Length; i += 4) s += Int128Equal(qq[i], qq[i + 1], qq[i + 2], qq[i + 3]) ? 1 : 0; return s; });
      TimeIt("BigMul equality", q, qq => { long s = 0; for (int i = 0; i < qq.Length; i += 4) s += BigMulEqual(qq[i], qq[i + 1], qq[i + 2], qq[i + 3]) ? 1 : 0; return s; });
      TimeIt("tiered equality", q, qq => { long s = 0; for (int i = 0; i < qq.Length; i += 4) s += TieredEqual(qq[i], qq[i + 1], qq[i + 2], qq[i + 3]) ? 1 : 0; return s; });
      Console.WriteLine();
    }
  }

  // ---------------------------------------------------------------- linked lists: class refs vs struct arena
  sealed class Node
  {
    public long currX; public double dx;
    public Node? prevAel, nextAel, prevSel, nextSel, jump;
    public object? outrec;
  }

  struct SNode
  {
    public long currX; public double dx;
    public int prevAel, nextAel, prevSel, nextSel, jump; public int outrec;
  }

  static void RunLists()
  {
    const int N = 400, PASSES = 4000, SWAPS = 8_000_000;
    Random r = new Random(7);
    int[] picks = new int[SWAPS];
    for (int i = 0; i < SWAPS; i++) picks[i] = r.Next(N - 1);

    Node[] nodes = new Node[N];
    for (int i = 0; i < N; i++) nodes[i] = new Node { dx = r.NextDouble() };
    for (int i = 0; i < N; i++) { nodes[i].prevAel = i > 0 ? nodes[i - 1] : null; nodes[i].nextAel = i < N - 1 ? nodes[i + 1] : null; }
    Node head = nodes[0];

    SNode[] arena = new SNode[N];
    for (int i = 0; i < N; i++) { arena[i].dx = nodes[i].dx; arena[i].prevAel = i > 0 ? i - 1 : -1; arena[i].nextAel = i < N - 1 ? i + 1 : -1; }
    int headIdx = 0;

    double bestC = double.MaxValue, bestS = double.MaxValue, bestCs = double.MaxValue, bestSs = double.MaxValue;
    for (int rep = 0; rep < 5; rep++)
    {
      Stopwatch sw = Stopwatch.StartNew();
      for (int p = 0; p < PASSES; p++)
      {
        Node? e = head;
        while (e != null)
        {
          e.prevSel = e.prevAel; e.nextSel = e.nextAel; e.jump = e.nextSel;
          e.currX = (long) Math.Round(e.dx * (p + 1));
          e = e.nextAel;
        }
      }
      sw.Stop(); bestC = Math.Min(bestC, sw.Elapsed.TotalMilliseconds);

      sw.Restart();
      for (int p = 0; p < PASSES; p++)
      {
        int e = headIdx;
        while (e >= 0)
        {
          ref SNode n = ref arena[e];
          n.prevSel = n.prevAel; n.nextSel = n.nextAel; n.jump = n.nextSel;
          n.currX = (long) Math.Round(n.dx * (p + 1));
          e = n.nextAel;
        }
      }
      sw.Stop(); bestS = Math.Min(bestS, sw.Elapsed.TotalMilliseconds);

      sw.Restart();
      for (int i = 0; i < SWAPS; i++)
      {
        Node e1 = nodes[picks[i]]; Node? e2 = e1.nextAel; if (e2 == null) continue;
        Node? next = e2.nextAel; if (next != null) next.prevAel = e1;
        Node? prev = e1.prevAel; if (prev != null) prev.nextAel = e2;
        e2.prevAel = prev; e2.nextAel = e1; e1.prevAel = e2; e1.nextAel = next;
        if (e2.prevAel == null) head = e2;
      }
      sw.Stop(); bestCs = Math.Min(bestCs, sw.Elapsed.TotalMilliseconds);

      sw.Restart();
      for (int i = 0; i < SWAPS; i++)
      {
        int i1 = picks[i]; ref SNode e1 = ref arena[i1]; int i2 = e1.nextAel; if (i2 < 0) continue;
        ref SNode e2 = ref arena[i2];
        int next = e2.nextAel; if (next >= 0) arena[next].prevAel = i1;
        int prev = e1.prevAel; if (prev >= 0) arena[prev].nextAel = i2;
        e2.prevAel = prev; e2.nextAel = i1; e1.prevAel = i2; e1.nextAel = next;
        if (e2.prevAel < 0) headIdx = i2;
      }
      sw.Stop(); bestSs = Math.Min(bestSs, sw.Elapsed.TotalMilliseconds);
    }
    Console.WriteLine("linked list plumbing (AEL/SEL), same operations on class objects vs a struct arena with int links:");
    Console.WriteLine($"  copy-to-SEL pass, class refs : {bestC * 1e6 / ((double) N * PASSES),6:0.00} ns/node");
    Console.WriteLine($"  copy-to-SEL pass, struct idx : {bestS * 1e6 / ((double) N * PASSES),6:0.00} ns/node   ({bestC / bestS:0.00}x)");
    Console.WriteLine($"  adjacent swap, class refs    : {bestCs * 1e6 / SWAPS,6:0.00} ns/swap");
    Console.WriteLine($"  adjacent swap, struct idx    : {bestSs * 1e6 / SWAPS,6:0.00} ns/swap   ({bestCs / bestSs:0.00}x)");
    GC.KeepAlive(head); GC.KeepAlive(headIdx);
  }

  // ---------------------------------------------------------------- intersect-node sort: refs vs POD
  struct NodeRef { public long y, x; public object? e1, e2; }
  struct NodePod { public long y, x; public int e1, e2; }

  interface ILess<T> { bool Less(in T a, in T b); }
  readonly struct RefLess : ILess<NodeRef> { public bool Less(in NodeRef a, in NodeRef b) => a.y != b.y ? b.y < a.y : a.x < b.x; }
  readonly struct PodLess : ILess<NodePod> { public bool Less(in NodePod a, in NodePod b) => a.y != b.y ? b.y < a.y : a.x < b.x; }
  readonly struct PodLessBL : ILess<NodePod> { public bool Less(in NodePod a, in NodePod b) { long ay = a.y, by = b.y; return (by < ay) | ((ay == by) & (a.x < b.x)); } }

  // the port's introsort (Clipper.Sorts.cs), verbatim algorithm
  static class Intro
  {
    const int threshold = 16;
    public static void Sort<T, C>(Span<T> items, C c) where C : struct, ILess<T>
    {
      int n = items.Length; if (n < 2) return;
      int lg = 0; for (int m = n; m > 1; m >>= 1) lg++;
      Loop(items, 0, n, 2 * lg, c);
      if (n > threshold) { Ins(items, 0, threshold, c); for (int i = threshold; i < n; i++) LinIns(items, i, 0, c); }
      else Ins(items, 0, n, c);
    }
    static void Loop<T, C>(Span<T> items, int lo, int hi, int depth, C c) where C : struct, ILess<T>
    {
      while (hi - lo > threshold)
      {
        if (depth == 0) { Heap(items, lo, hi, c); return; }
        depth--;
        int mid = lo + (hi - lo) / 2;
        Med(items, lo, lo + 1, mid, hi - 1, c);
        int cut = Part(items, lo + 1, hi, lo, c);
        Loop(items, cut, hi, depth, c);
        hi = cut;
      }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Swap<T>(Span<T> s, int i, int j) { T t = s[i]; s[i] = s[j]; s[j] = t; }
    static void Med<T, C>(Span<T> s, int r, int a, int b, int cc, C c) where C : struct, ILess<T>
    {
      if (c.Less(s[a], s[b])) { if (c.Less(s[b], s[cc])) Swap(s, r, b); else if (c.Less(s[a], s[cc])) Swap(s, r, cc); else Swap(s, r, a); }
      else if (c.Less(s[a], s[cc])) Swap(s, r, a); else if (c.Less(s[b], s[cc])) Swap(s, r, cc); else Swap(s, r, b);
    }
    static int Part<T, C>(Span<T> s, int lo, int hi, int pivot, C c) where C : struct, ILess<T>
    {
      T p = s[pivot];
      while (true)
      {
        while (c.Less(s[lo], p)) lo++;
        hi--;
        while (c.Less(p, s[hi])) hi--;
        if (lo >= hi) return lo;
        Swap(s, lo, hi); lo++;
      }
    }
    static void Ins<T, C>(Span<T> s, int lo, int hi, C c) where C : struct, ILess<T>
    {
      if (lo == hi) return;
      for (int i = lo + 1; i < hi; i++)
      {
        if (c.Less(s[i], s[lo])) { T v = s[i]; for (int j = i; j > lo; j--) s[j] = s[j - 1]; s[lo] = v; }
        else LinIns(s, i, lo, c);
      }
    }
    static void LinIns<T, C>(Span<T> s, int pos, int guard, C c) where C : struct, ILess<T>
    {
      T v = s[pos]; int j = pos - 1;
      while (j >= guard && c.Less(v, s[j])) { s[j + 1] = s[j]; j--; }
      s[j + 1] = v;
    }
    static void Heap<T, C>(Span<T> s, int lo, int hi, C c) where C : struct, ILess<T>
    {
      int n = hi - lo;
      for (int i = n / 2; i > 0; i--) Sift(s, lo, i, n, c);
      for (int end = n - 1; end > 0; end--) { Swap(s, lo, lo + end); Sift(s, lo, 1, end, c); }
    }
    static void Sift<T, C>(Span<T> s, int lo, int start, int end, C c) where C : struct, ILess<T>
    {
      int root = start - 1, child = 2 * root + 1;
      while (child < end)
      {
        if (child + 1 < end && c.Less(s[lo + child], s[lo + child + 1])) child++;
        if (c.Less(s[lo + root], s[lo + child])) { Swap(s, lo + root, lo + child); root = child; child = 2 * root + 1; }
        else break;
      }
    }
  }

  static void RunSort()
  {
    const int N = 100_000, SORTS = 15;
    Random r = new Random(11);
    object[] edges = new object[2000];
    for (int i = 0; i < edges.Length; i++) edges[i] = new object();
    NodeRef[] src = new NodeRef[N]; NodePod[] srcP = new NodePod[N];
    for (int i = 0; i < N; i++)
    {
      long y = r.Next(2000), x = r.Next(1_000_000); int e1 = r.Next(2000), e2 = r.Next(2000);
      src[i] = new NodeRef { y = y, x = x, e1 = edges[e1], e2 = edges[e2] };
      srcP[i] = new NodePod { y = y, x = x, e1 = e1, e2 = e2 };
    }
    NodeRef[] work = new NodeRef[N]; NodePod[] workP = new NodePod[N];
    double bestR = double.MaxValue, bestP = double.MaxValue;
    for (int rep = 0; rep < 5; rep++)
    {
      Stopwatch sw = Stopwatch.StartNew();
      for (int s = 0; s < SORTS; s++) { Array.Copy(src, work, N); Intro.Sort<NodeRef, RefLess>(work, default); }
      sw.Stop(); bestR = Math.Min(bestR, sw.Elapsed.TotalMilliseconds / SORTS);
      sw.Restart();
      for (int s = 0; s < SORTS; s++) { Array.Copy(srcP, workP, N); Intro.Sort<NodePod, PodLess>(workP, default); }
      sw.Stop(); bestP = Math.Min(bestP, sw.Elapsed.TotalMilliseconds / SORTS);
    }
    double bestB = double.MaxValue; NodePod[] workB = new NodePod[N];
    for (int rep = 0; rep < 5; rep++)
    {
      Stopwatch sw = Stopwatch.StartNew();
      for (int s = 0; s < SORTS; s++) { Array.Copy(srcP, workB, N); Intro.Sort<NodePod, PodLessBL>(workB, default); }
      sw.Stop(); bestB = Math.Min(bestB, sw.Elapsed.TotalMilliseconds / SORTS);
    }
    for (int i = 0; i < N; i++) if (workB[i].e1 != workP[i].e1 || workB[i].x != workP[i].x) { Console.WriteLine("ORDER DIFFERS"); break; }
    Console.WriteLine($"  POD branchless comparer   : {bestB,7:0.00} ms/sort");
    Console.WriteLine($"introsort of {N} intersect nodes (Point64 + 2 fields), same keys:");
    Console.WriteLine($"  node holds 2 object refs : {bestR,7:0.00} ms/sort  ({bestR * 1e6 / N:0.0} ns/elem)");
    Console.WriteLine($"  node holds 2 int indices : {bestP,7:0.00} ms/sort  ({bestP * 1e6 / N:0.0} ns/elem)   ({bestR / bestP:0.00}x)");
  }

  static void Main()
  {
    Console.WriteLine($"runtime {Environment.Version}, x64={Environment.Is64BitProcess}");
    Console.WriteLine("=== orientation predicates (a*b vs c*d), ns per call, best of 7 ===");
    RunPredicates();
    Console.WriteLine("=== write barriers: AEL/SEL plumbing ===");
    RunLists();
    Console.WriteLine("=== write barriers: intersect list sort ===");
    RunSort();
  }
}
