/*******************************************************************************
* Purpose   :  Boolean operations split over independent clusters of paths     *
* License   :  https://www.boost.org/LICENSE_1_0.txt                           *
*                                                                              *
* Paths whose bounding boxes do not overlap, even transitively, can never      *
* interact in the sweep: none of their edges intersect, and a closed path      *
* contributes nothing to the winding count of any point outside its bounding   *
* box. So the result of a boolean operation over such clusters is the union of *
* the results of the clusters, each of which can be clipped on its own thread  *
* (docs/optimization-plan.md 5.4).                                             *
*                                                                              *
* The regions produced are those of the single operation up to the engine's   *
* integer rounding (about 1 ppm of a path's area on the test data): the other  *
* clusters' scanlines split a cluster's scanbeams differently, and an          *
* intersection that rounds outside its scanbeam is snapped to the beam's edge. *
* The order of the paths and their start vertices can differ too. That is why *
* this is a separate, opt-in entry point: Clipper.BooleanOp stays bit-identical*
* to the C++.                                                                  *
*                                                                              *
* The gain is more than the thread count: one sweep walks the active edges of  *
* every cluster sharing a band of Y, so its cost grows faster than linearly    *
* with the number of clusters (400 clusters of 8 ellipses: 1775 ms as one      *
* operation, 33 ms split into clusters, 12 threads).                           *
*******************************************************************************/

#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

#if USINGZ
namespace Clipper2ZLib
#else
namespace Clipper2Lib
#endif
{
  public static partial class Clipper
  {
    /// <summary>
    /// A boolean operation on closed paths that clips independent clusters of
    /// paths (groups whose bounding boxes do not overlap) in parallel. The result
    /// covers the regions <see cref="BooleanOp(ClipType, FillRule, Paths64, Paths64?)"/>
    /// returns up to the engine's integer rounding (a vertex can differ by a unit
    /// where an intersection was snapped to a scanbeam edge); the clusters appear
    /// in the order of their first input path, but the order of paths inside a
    /// cluster and their start vertices may differ.
    /// The output is deterministic (it does not depend on the thread count).
    /// </summary>
    public static Paths64 BooleanOpParallel(ClipType clipType, FillRule fillRule,
      Paths64 subject, Paths64? clip = null)
    {
      List<(Paths64 subject, Paths64 clip)>? clusters = FindClusters(subject, clip);
      if (clusters == null)
        return BooleanOp(clipType, fillRule, subject, clip);

      Paths64[] parts = new Paths64[clusters.Count];
      Parallel.For(0, clusters.Count, c =>
        parts[c] = BooleanOp(clipType, fillRule, clusters[c].subject, clusters[c].clip));

      int total = 0;
      foreach (Paths64 part in parts) total += part.Count;
      Paths64 result = new Paths64(total);
      foreach (Paths64 part in parts) result.AddRange(part);
      return result;
    }

    /// <summary>
    /// <see cref="BooleanOpParallel(ClipType, FillRule, Paths64, Paths64?)"/> with a
    /// polytree result: every cluster's outer polygons become children of the
    /// root in cluster order (a cluster's holes and islands stay nested under
    /// its own polygons, because clusters cannot contain one another).
    /// </summary>
    public static void BooleanOpParallel(ClipType clipType, FillRule fillRule,
      Paths64 subject, Paths64? clip, PolyTree64 polytree)
    {
      List<(Paths64 subject, Paths64 clip)>? clusters = FindClusters(subject, clip);
      if (clusters == null)
      {
        BooleanOp(clipType, fillRule, subject, clip, polytree);
        return;
      }
      PolyTree64[] parts = new PolyTree64[clusters.Count];
      Parallel.For(0, clusters.Count, c =>
      {
        PolyTree64 t = new PolyTree64();
        BooleanOp(clipType, fillRule, clusters[c].subject, clusters[c].clip, t);
        parts[c] = t;
      });
      polytree.Clear();
      foreach (PolyTree64 part in parts) AdoptChildren(polytree, part);
    }

    /// <summary>
    /// The PathsD form: the paths are scaled exactly as <see cref="ClipperD"/>
    /// scales them (a power of two at or above 10^precision), so each cluster is
    /// clipped as a ClipperD with that precision would clip it.
    /// </summary>
    public static PathsD BooleanOpParallel(ClipType clipType, FillRule fillRule,
      PathsD subject, PathsD? clip = null, int precision = 2)
    {
      double scale = ClipperDScale(ref precision);
      Paths64 result = BooleanOpParallel(clipType, fillRule,
        ScaleIn(subject, scale), clip == null ? null : ScaleIn(clip, scale));
      return ScalePathsD(result, 1 / scale);
    }

    /// <summary>The PathsD form with a polytree result.</summary>
    public static void BooleanOpParallel(ClipType clipType, FillRule fillRule,
      PathsD subject, PathsD? clip, PolyTreeD polytree, int precision = 2)
    {
      double scale = ClipperDScale(ref precision);
      PolyTree64 tree = new PolyTree64();
      BooleanOpParallel(clipType, fillRule,
        ScaleIn(subject, scale), clip == null ? null : ScaleIn(clip, scale), tree);
      polytree.Clear();
      polytree.Scale = 1 / scale;
      CopyTree(tree, polytree);
    }

    /// <summary>Union of independent clusters in parallel (see BooleanOpParallel).</summary>
    public static Paths64 UnionParallel(Paths64 subject, FillRule fillRule)
    {
      return BooleanOpParallel(ClipType.Union, fillRule, subject, null);
    }

    /// <summary>Union of independent clusters in parallel (see BooleanOpParallel).</summary>
    public static PathsD UnionParallel(PathsD subject, FillRule fillRule, int precision = 2)
    {
      return BooleanOpParallel(ClipType.Union, fillRule, subject, null, precision);
    }

    // helpers ---------------------------------------------------------------

    /// <summary>
    /// Splits subject and clip paths into clusters (union-find over bounding
    /// box overlap, found with a sweep over the boxes sorted by their left
    /// edge), each listed in input order and numbered by its first path.
    /// Returns null when splitting cannot pay off (a small input, or a single
    /// cluster), in which case the caller runs the plain operation.
    /// </summary>
    private static List<(Paths64 subject, Paths64 clip)>? FindClusters(Paths64 subject, Paths64? clip)
    {
      int ns = subject.Count, nc = clip?.Count ?? 0, n = ns + nc;
      long totalPoints = 0;
      for (int i = 0; i < ns; i++) totalPoints += subject[i].Count;
      for (int i = 0; i < nc; i++) totalPoints += clip![i].Count;
      if (n < 2 || !BulkOps.ShouldParallelize(totalPoints)) return null;

      Path64 PathAt(int i) => i < ns ? subject[i] : clip![i - ns];

      Rect64[] box = new Rect64[n];
      int[] order = new int[n];
      int valid = 0;
      for (int i = 0; i < n; i++)
      {
        Path64 p = PathAt(i);
        if (p.Count == 0) continue; // an empty path contributes nothing
        box[i] = InternalClipper.GetBounds(p);
        order[valid++] = i;
      }
      int[] parent = new int[n];
      for (int i = 0; i < n; i++) parent[i] = i;
      int Find(int x)
      {
        while (parent[x] != x) x = parent[x] = parent[parent[x]];
        return x;
      }
      Array.Sort(order, 0, valid, Comparer<int>.Create((a, b) =>
        box[a].left != box[b].left ? box[a].left.CompareTo(box[b].left) : a.CompareTo(b)));
      List<int> active = new List<int>();
      for (int k = 0; k < valid; k++)
      {
        int i = order[k];
        Rect64 r = box[i];
        int w = 0;
        for (int j = 0; j < active.Count; j++)
        {
          int a = active[j];
          if (box[a].right < r.left) continue; // can no longer overlap anything
          active[w++] = a;
          if (box[a].top <= r.bottom && r.top <= box[a].bottom)
            parent[Find(a)] = Find(i);
        }
        active.RemoveRange(w, active.Count - w);
        active.Add(i);
      }

      Dictionary<int, int> clusterOf = new Dictionary<int, int>();
      List<(Paths64 subject, Paths64 clip)> clusters = new List<(Paths64, Paths64)>();
      for (int i = 0; i < n; i++)
      {
        Path64 p = PathAt(i);
        if (p.Count == 0) continue;
        int root = Find(i);
        if (!clusterOf.TryGetValue(root, out int c))
        {
          c = clusters.Count;
          clusterOf[root] = c;
          clusters.Add((new Paths64(), new Paths64()));
        }
        if (i < ns) clusters[c].subject.Add(p); else clusters[c].clip.Add(p);
      }
      return clusters.Count > 1 ? clusters : null;
    }

    private static void AdoptChildren(PolyPathBase target, PolyPathBase source)
    {
      foreach (PolyPathBase child in source._childs)
      {
        child._parent = target;
        target._childs.Add(child);
      }
      source._childs.Clear();
    }

    private static void CopyTree(PolyPath64 source, PolyPathD target)
    {
      for (int i = 0; i < source.Count; i++)
      {
        PolyPath64 child = source[i];
        PolyPathD copy = (PolyPathD) target.AddChild(child.Polygon!);
        CopyTree(child, copy);
      }
    }

    /// <summary>The scale ClipperD(precision) works with (a power of two, #25).</summary>
    private static double ClipperDScale(ref int precision)
    {
      InternalClipper.CheckPrecisionRange(ref precision);
      return Math.Pow(2.0, Math.ILogB(Math.Pow(10, precision)) + 1);
    }

    private static Paths64 ScaleIn(PathsD paths, double scale)
    {
      int errorCode = 0;
      return InternalClipper.ScalePaths(paths, scale, ref errorCode);
    }
  }
}
