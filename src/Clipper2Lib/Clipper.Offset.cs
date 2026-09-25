/*******************************************************************************
* Author    :  Angus Johnson                                                   *
* Date      :  11 October 2025                                                 *
* Website   :  https://www.angusj.com                                          *
* Copyright :  Angus Johnson 2010-2025                                         *
* Purpose   :  Path Offset (Inflate/Shrink)                                    *
* License   :  https://www.boost.org/LICENSE_1_0.txt                           *
*                                                                              *
* C# port of clipper2/clipper.offset.h + src/clipper.offset.cpp (ver. 2.0.1)   *
*******************************************************************************/

#nullable enable
using System;
using System.Collections.Generic;

#if USINGZ
namespace Clipper2ZLib
#else
namespace Clipper2Lib
#endif
{
  public enum JoinType
  {
    Square,  // Joins are 'squared' at exactly the offset distance (more complex code)
    Bevel,   // Similar to Square, but the offset distance varies with angle (simple code & faster)
    Round,
    Miter
  }

  public enum EndType
  {
    Polygon, // offsets only one side of a closed path
    Joined,  // offsets both sides of a path, with joined ends
    Butt,    // offsets both sides of a path, with square blunt ends
    Square,  // offsets both sides of a path, with square extended ends
    Round    // offsets both sides of a path, with round extended ends
  }

  public class ClipperOffset
  {
    private class Group
    {
      internal readonly Paths64 pathsIn;
      internal int lowestPathIdx = -1;
      internal bool isReversed;
      internal readonly JoinType joinType;
      internal readonly EndType endType;

      public Group(Paths64 paths, JoinType joinType, EndType endType = EndType.Polygon)
      {
        this.joinType = joinType;
        this.endType = endType;

        bool isJoined =
          (endType == EndType.Polygon) ||
          (endType == EndType.Joined);
        pathsIn = new Paths64(paths.Count);
        foreach (Path64 p in paths)
          pathsIn.Add(Clipper.StripDuplicates(p, isJoined));

        if (endType == EndType.Polygon)
        {
          bool isNegArea;
          GetLowestClosedPathInfo(pathsIn, out lowestPathIdx, out isNegArea);
          // the lowermost path must be an outer path, so if its orientation is negative,
          // then flag the whole group is 'reversed' (will negate delta etc.)
          // as this is much more efficient than reversing every path.
          isReversed = (lowestPathIdx >= 0) && isNegArea;
        }
        else
        {
          lowestPathIdx = -1;
          isReversed = false;
        }
      }
    }

    private const double floatingPointTolerance = 1e-12;

    // Clipper2 approximates arcs by using series of relatively short straight
    // line segments. And logically, shorter line segments will produce better arc
    // approximations. But very short segments can degrade performance, usually
    // with little or no discernable improvement in curve quality. Very short
    // segments can even detract from curve quality, due to the effects of integer
    // rounding. Since there isn't an optimal number of line segments for any given
    // arc radius (that perfectly balances curve approximation with performance),
    // arc tolerance is user defined. Nevertheless, when the user doesn't define
    // an arc tolerance (ie leaves alone the 0 default value), the calculated
    // default arc tolerance (offset_radius / 500) generally produces good (smooth)
    // arc approximations without producing excessively small segment lengths.
    // See also: https://www.angusj.com/clipper2/Docs/Trigonometry.htm
    private const double arcConst = 0.002; // <-- 1/500

    private int _errorCode;
    private double _delta = 0.0;
    private double _groupDelta = 0.0;
    private double _tempLim = 0.0;
    private double _stepsPerRad = 0.0;
    private double _stepSin = 0.0;
    private double _stepCos = 0.0;
    private readonly PathD _norms = new PathD();
    private readonly Path64 _pathOut = new Path64();
    private Paths64? _solution;
    private PolyTree64? _solutionTree;
    private readonly List<Group> _groups = new List<Group>();
    private JoinType _joinType = JoinType.Bevel;
    private EndType _endType = EndType.Polygon;
    private double _miterLimit = 0.0;
    private double _arcTolerance = 0.0;
    private bool _preserveCollinear = false;
    private bool _reverseSolution = false;

    public double MiterLimit
    {
      get => _miterLimit;
      set => _miterLimit = value;
    }

    public double ArcTolerance
    {
      get => _arcTolerance;
      set => _arcTolerance = value;
    }

    public bool PreserveCollinear
    {
      get => _preserveCollinear;
      set => _preserveCollinear = value;
    }

    public bool ReverseSolution
    {
      get => _reverseSolution;
      set => _reverseSolution = value;
    }

    public delegate double DeltaCallback64(Path64 path,
      PathD pathNormals, int currPt, int prevPt);

    public DeltaCallback64? DeltaCallback { get; set; }

#if USINGZ
    public ClipperBase.ZCallback64? ZCallback { get; set; }

    internal void ZCB(Point64 bot1, Point64 top1,
      Point64 bot2, Point64 top2, ref Point64 ip)
    {
      if (bot1.Z != 0 &&
        ((bot1.Z == bot2.Z) || (bot1.Z == top2.Z))) ip.Z = bot1.Z;
      else if (bot2.Z != 0 && bot2.Z == top1.Z) ip.Z = bot2.Z;
      else if (top1.Z != 0 && top1.Z == top2.Z) ip.Z = top1.Z;
      else if (ZCallback != null) ZCallback(bot1, top1, bot2, top2, ref ip);
    }
#endif

    public ClipperOffset(double miterLimit = 2.0,
      double arcTolerance = 0.0,
      bool preserveCollinear = false,
      bool reverseSolution = false)
    {
      _miterLimit = miterLimit;
      _arcTolerance = arcTolerance;
      _preserveCollinear = preserveCollinear;
      _reverseSolution = reverseSolution;
    }

    public int ErrorCode() => _errorCode;

    public void Clear()
    {
      _groups.Clear();
      _norms.Clear();
    }

    public void AddPath(Path64 path, JoinType joinType, EndType endType)
    {
      _groups.Add(new Group(new Paths64(1) { path }, joinType, endType));
    }

    public void AddPaths(Paths64 paths, JoinType joinType, EndType endType)
    {
      if (paths.Count == 0) return;
      _groups.Add(new Group(paths, joinType, endType));
    }

    // Miscellaneous methods -----------------------------------------------------

    internal static void GetLowestClosedPathInfo(Paths64 paths, out int idx, out bool isNegArea)
    {
      idx = -1;
      isNegArea = false;
      Point64 botPt = new Point64(long.MaxValue, long.MinValue);
      for (int i = 0; i < paths.Count; ++i)
      {
        double a = double.MaxValue;
        foreach (Point64 pt in paths[i])
        {
          if ((pt.Y < botPt.Y) ||
            ((pt.Y == botPt.Y) && (pt.X >= botPt.X))) continue;
          if (a == double.MaxValue)
          {
            a = Clipper.Area(paths[i]);
            if (a == 0) break; // invalid closed path, so break from inner loop
            isNegArea = a < 0;
          }
          idx = i;
          botPt.X = pt.X;
          botPt.Y = pt.Y;
        }
      }
    }

    internal static double Hypot(double x, double y)
    {
      // given that this is an internal function, and given the x and y parameters
      // will always be coordinate values (or the difference between coordinate values),
      // x and y should always be within INT64_MIN to INT64_MAX. Consequently,
      // there should be no risk that the following computation will overflow
      // see https://stackoverflow.com/a/32436148/359538
      return Math.Sqrt(x * x + y * y);
    }

    internal static PointD GetUnitNormal(Point64 pt1, Point64 pt2)
    {
      if (pt1 == pt2) return new PointD(0.0, 0.0);
      double dx = (double) (pt2.X - pt1.X);
      double dy = (double) (pt2.Y - pt1.Y);
      double inverseHypot = 1.0 / Hypot(dx, dy);
      dx *= inverseHypot;
      dy *= inverseHypot;
      return new PointD(dy, -dx);
    }

    internal static bool AlmostZero(double value, double epsilon = 0.001)
    {
      return Math.Abs(value) < epsilon;
    }

    internal static PointD NormalizeVector(PointD vec)
    {
      double h = Hypot(vec.x, vec.y);
      if (AlmostZero(h)) return new PointD(0, 0);
      double inverseHypot = 1 / h;
      return new PointD(vec.x * inverseHypot, vec.y * inverseHypot);
    }

    internal static PointD GetAvgUnitVector(PointD vec1, PointD vec2)
    {
      return NormalizeVector(new PointD(vec1.x + vec2.x, vec1.y + vec2.y));
    }

    internal static bool IsClosedPath(EndType et)
    {
      return et == EndType.Polygon || et == EndType.Joined;
    }

    private static Point64 GetPerpendic(Point64 pt, PointD norm, double delta)
    {
#if USINGZ
      return new Point64(pt.X + norm.x * delta, pt.Y + norm.y * delta, pt.Z);
#else
      return new Point64(pt.X + norm.x * delta, pt.Y + norm.y * delta);
#endif
    }

    private static PointD GetPerpendicD(Point64 pt, PointD norm, double delta)
    {
#if USINGZ
      return new PointD(pt.X + norm.x * delta, pt.Y + norm.y * delta, pt.Z);
#else
      return new PointD(pt.X + norm.x * delta, pt.Y + norm.y * delta);
#endif
    }

    private static void NegatePath(PathD path)
    {
      for (int i = 0; i < path.Count; i++)
      {
        path[i] = new PointD(-path[i].x, -path[i].y
#if USINGZ
          , path[i].z
#endif
        );
      }
    }

    // ClipperOffset methods -----------------------------------------------------

    private void BuildNormals(Path64 path)
    {
      _norms.Clear();
      int cnt = path.Count;
      if (cnt == 0) return;
      _norms.Capacity = cnt;
      for (int i = 0; i < cnt - 1; i++)
        _norms.Add(GetUnitNormal(path[i], path[i + 1]));
      _norms.Add(GetUnitNormal(path[cnt - 1], path[0]));
    }

    private void DoBevel(Path64 path, int j, int k)
    {
      PointD pt1, pt2;
      if (j == k)
      {
        double absDelta = Math.Abs(_groupDelta);
#if USINGZ
        pt1 = new PointD(path[j].X - absDelta * _norms[j].x, path[j].Y - absDelta * _norms[j].y, path[j].Z);
        pt2 = new PointD(path[j].X + absDelta * _norms[j].x, path[j].Y + absDelta * _norms[j].y, path[j].Z);
#else
        pt1 = new PointD(path[j].X - absDelta * _norms[j].x, path[j].Y - absDelta * _norms[j].y);
        pt2 = new PointD(path[j].X + absDelta * _norms[j].x, path[j].Y + absDelta * _norms[j].y);
#endif
      }
      else
      {
#if USINGZ
        pt1 = new PointD(path[j].X + _groupDelta * _norms[k].x, path[j].Y + _groupDelta * _norms[k].y, path[j].Z);
        pt2 = new PointD(path[j].X + _groupDelta * _norms[j].x, path[j].Y + _groupDelta * _norms[j].y, path[j].Z);
#else
        pt1 = new PointD(path[j].X + _groupDelta * _norms[k].x, path[j].Y + _groupDelta * _norms[k].y);
        pt2 = new PointD(path[j].X + _groupDelta * _norms[j].x, path[j].Y + _groupDelta * _norms[j].y);
#endif
      }
      _pathOut.Add(new Point64(pt1));
      _pathOut.Add(new Point64(pt2));
    }

    private void DoSquare(Path64 path, int j, int k)
    {
      PointD vec;
      if (j == k)
        vec = new PointD(_norms[j].y, -_norms[j].x);
      else
        vec = GetAvgUnitVector(
          new PointD(-_norms[k].y, _norms[k].x),
          new PointD(_norms[j].y, -_norms[j].x));

      double absDelta = Math.Abs(_groupDelta);

      // now offset the original vertex delta units along unit vector
      PointD ptQ = new PointD(path[j]);
      ptQ = InternalClipper.TranslatePoint(ptQ, absDelta * vec.x, absDelta * vec.y);
      // get perpendicular vertices
      PointD pt1 = InternalClipper.TranslatePoint(ptQ, _groupDelta * vec.y, _groupDelta * -vec.x);
      PointD pt2 = InternalClipper.TranslatePoint(ptQ, _groupDelta * -vec.y, _groupDelta * vec.x);
      // get 2 vertices along one edge offset
      PointD pt3 = GetPerpendicD(path[k], _norms[k], _groupDelta);
      if (j == k)
      {
        PointD pt4 = new PointD(pt3.x + vec.x * _groupDelta, pt3.y + vec.y * _groupDelta);
        PointD pt = ptQ;
        InternalClipper.GetLineIntersectPt(pt1, pt2, pt3, pt4, out pt);
        // get the second intersect point through reflecion
        _pathOut.Add(new Point64(InternalClipper.ReflectPoint(pt, ptQ)));
        _pathOut.Add(new Point64(pt));
      }
      else
      {
        PointD pt4 = GetPerpendicD(path[j], _norms[k], _groupDelta);
        PointD pt = ptQ;
        InternalClipper.GetLineIntersectPt(pt1, pt2, pt3, pt4, out pt);
        _pathOut.Add(new Point64(pt));
        // get the second intersect point through reflecion
        _pathOut.Add(new Point64(InternalClipper.ReflectPoint(pt, ptQ)));
      }
    }

    private void DoMiter(Path64 path, int j, int k, double cosA)
    {
      double q = _groupDelta / (cosA + 1);
#if USINGZ
      _pathOut.Add(new Point64(
        path[j].X + (_norms[k].x + _norms[j].x) * q,
        path[j].Y + (_norms[k].y + _norms[j].y) * q,
        path[j].Z));
#else
      _pathOut.Add(new Point64(
        path[j].X + (_norms[k].x + _norms[j].x) * q,
        path[j].Y + (_norms[k].y + _norms[j].y) * q));
#endif
    }

    private void DoRound(Path64 path, int j, int k, double angle)
    {
      if (DeltaCallback != null)
      {
        // when DeltaCallback is assigned, _groupDelta won't be constant,
        // so we'll need to do the following calculations for *every* vertex.
        double absDelta = Math.Abs(_groupDelta);
        double arcTol = (_arcTolerance > floatingPointTolerance ?
          Math.Min(absDelta, _arcTolerance) : absDelta * arcConst);
        double stepsPer360 = Math.Min(InternalClipper.PI / Math.Acos(1 - arcTol / absDelta),
          absDelta * InternalClipper.PI);
        _stepSin = Math.Sin(2 * InternalClipper.PI / stepsPer360);
        _stepCos = Math.Cos(2 * InternalClipper.PI / stepsPer360);
        if (_groupDelta < 0.0) _stepSin = -_stepSin;
        _stepsPerRad = stepsPer360 / (2 * InternalClipper.PI);
      }

      Point64 pt = path[j];
      PointD offsetVec = new PointD(_norms[k].x * _groupDelta, _norms[k].y * _groupDelta);

      if (j == k) offsetVec.Negate();
#if USINGZ
      _pathOut.Add(new Point64(pt.X + offsetVec.x, pt.Y + offsetVec.y, pt.Z));
#else
      _pathOut.Add(new Point64(pt.X + offsetVec.x, pt.Y + offsetVec.y));
#endif
      int steps = (int) Math.Ceiling(_stepsPerRad * Math.Abs(angle)); // #448, #456
      for (int i = 1; i < steps; ++i) // ie 1 less than steps
      {
        offsetVec = new PointD(offsetVec.x * _stepCos - _stepSin * offsetVec.y,
          offsetVec.x * _stepSin + offsetVec.y * _stepCos);
#if USINGZ
        _pathOut.Add(new Point64(pt.X + offsetVec.x, pt.Y + offsetVec.y, pt.Z));
#else
        _pathOut.Add(new Point64(pt.X + offsetVec.x, pt.Y + offsetVec.y));
#endif
      }
      _pathOut.Add(GetPerpendic(path[j], _norms[j], _groupDelta));
    }

    private void OffsetPoint(Group group, Path64 path, int j, int k)
    {
      // Let A = change in angle where edges join
      // A == 0: ie no change in angle (flat join)
      // A == PI: edges 'spike'
      // sin(A) < 0: right turning
      // cos(A) < 0: change in angle is more than 90 degree

      if (path[j] == path[k]) return;

      double sinA = InternalClipper.CrossProduct(_norms[j], _norms[k]);
      double cosA = InternalClipper.DotProduct(_norms[j], _norms[k]);
      if (sinA > 1.0) sinA = 1.0;
      else if (sinA < -1.0) sinA = -1.0;

      if (DeltaCallback != null)
      {
        _groupDelta = DeltaCallback(path, _norms, j, k);
        if (group.isReversed) _groupDelta = -_groupDelta;
      }
      if (Math.Abs(_groupDelta) <= floatingPointTolerance)
      {
        _pathOut.Add(path[j]);
        return;
      }

      if (cosA > -0.999 && (sinA * _groupDelta < 0)) // test for concavity first (#593)
      {
        // is concave
        // by far the simplest way to construct concave joins, especially those joining very
        // short segments, is to insert 3 points that produce negative regions. These regions
        // will be removed later by the finishing union operation. This is also the best way
        // to ensure that path reversals (ie over-shrunk paths) are removed.
#if USINGZ
        _pathOut.Add(new Point64(GetPerpendic(path[j], _norms[k], _groupDelta), path[j].Z));
        _pathOut.Add(path[j]); // (#405, #873, #916)
        _pathOut.Add(new Point64(GetPerpendic(path[j], _norms[j], _groupDelta), path[j].Z));
#else
        _pathOut.Add(GetPerpendic(path[j], _norms[k], _groupDelta));
        _pathOut.Add(path[j]); // (#405, #873, #916)
        _pathOut.Add(GetPerpendic(path[j], _norms[j], _groupDelta));
#endif
      }
      else if (cosA > 0.999 && _joinType != JoinType.Round)
      {
        // almost straight - less than 2.5 degree (#424, #482, #526 & #724)
        DoMiter(path, j, k, cosA);
      }
      else if (_joinType == JoinType.Miter)
      {
        // miter unless the angle is sufficiently acute to exceed ML
        if (cosA > _tempLim - 1) DoMiter(path, j, k, cosA);
        else DoSquare(path, j, k);
      }
      else if (_joinType == JoinType.Round)
        DoRound(path, j, k, Math.Atan2(sinA, cosA));
      else if (_joinType == JoinType.Bevel)
        DoBevel(path, j, k);
      else
        DoSquare(path, j, k);
    }

    private void OffsetPolygon(Group group, Path64 path)
    {
      _pathOut.Clear();
      int cnt = path.Count;
      for (int j = 0, k = cnt - 1; j < cnt; k = j, ++j)
        OffsetPoint(group, path, j, k);
      // nb: the C++ emplace_back() copies 'path_out', so a copy is required here too
      _solution!.Add(new Path64(_pathOut));
    }

    private void OffsetOpenJoined(Group group, Path64 path)
    {
      OffsetPolygon(group, path);
      Path64 reversePath = new Path64(path);
      reversePath.Reverse();

      // rebuild normals
      _norms.Reverse();
      _norms.Add(_norms[0]);
      _norms.RemoveAt(0);
      NegatePath(_norms);

      OffsetPolygon(group, reversePath);
    }

    private void OffsetOpenPath(Group group, Path64 path)
    {
      // do the line start cap
      if (DeltaCallback != null) _groupDelta = DeltaCallback(path, _norms, 0, 0);

      if (Math.Abs(_groupDelta) <= floatingPointTolerance)
        _pathOut.Add(path[0]);
      else
      {
        switch (_endType)
        {
          case EndType.Butt:
            DoBevel(path, 0, 0);
            break;
          case EndType.Round:
            DoRound(path, 0, 0, InternalClipper.PI);
            break;
          default:
            DoSquare(path, 0, 0);
            break;
        }
      }

      int highI = path.Count - 1;
      // offset the left side going forward
      for (int j = 1, k = 0; j < highI; k = j, ++j)
        OffsetPoint(group, path, j, k);

      // reverse normals
      for (int i = highI; i > 0; --i)
        _norms[i] = new PointD(-_norms[i - 1].x, -_norms[i - 1].y);
      _norms[0] = _norms[highI];

      // do the line end cap
      if (DeltaCallback != null)
        _groupDelta = DeltaCallback(path, _norms, highI, highI);

      if (Math.Abs(_groupDelta) <= floatingPointTolerance)
        _pathOut.Add(path[highI]);
      else
      {
        switch (_endType)
        {
          case EndType.Butt:
            DoBevel(path, highI, highI);
            break;
          case EndType.Round:
            DoRound(path, highI, highI, InternalClipper.PI);
            break;
          default:
            DoSquare(path, highI, highI);
            break;
        }
      }

      for (int j = highI - 1, k = highI; j > 0; k = j, --j)
        OffsetPoint(group, path, j, k);
      // nb: the C++ emplace_back() copies 'path_out', so a copy is required here too
      _solution!.Add(new Path64(_pathOut));
    }

    private void DoGroupOffset(Group group)
    {
      if (group.endType == EndType.Polygon)
      {
        // a straight path (2 points) can now also be 'polygon' offset
        // where the ends will be treated as (180 deg.) joins
        if (group.lowestPathIdx < 0) _delta = Math.Abs(_delta);
        _groupDelta = (group.isReversed) ? -_delta : _delta;
      }
      else
        _groupDelta = Math.Abs(_delta);

      double absDelta = Math.Abs(_groupDelta);
      _joinType = group.joinType;
      _endType = group.endType;

      if (group.joinType == JoinType.Round || group.endType == EndType.Round)
      {
        // calculate the number of steps required to approximate a circle
        // (see https://www.angusj.com/clipper2/Docs/Trigonometry.htm)
        // arcTol - when _arcTolerance is undefined (0) then curve imprecision
        // will be relative to the size of the offset (delta). Obviously very
        // large offsets will almost always require much less precision.
        double arcTol = (_arcTolerance > floatingPointTolerance) ?
          Math.Min(absDelta, _arcTolerance) : absDelta * arcConst;

        double stepsPer360 = Math.Min(InternalClipper.PI / Math.Acos(1 - arcTol / absDelta),
          absDelta * InternalClipper.PI);
        _stepSin = Math.Sin(2 * InternalClipper.PI / stepsPer360);
        _stepCos = Math.Cos(2 * InternalClipper.PI / stepsPer360);
        if (_groupDelta < 0.0) _stepSin = -_stepSin;
        _stepsPerRad = stepsPer360 / (2 * InternalClipper.PI);
      }

      foreach (Path64 pathIn in group.pathsIn)
      {
        int pathLen = pathIn.Count;
        _pathOut.Clear();

        if (pathLen == 1) // single point
        {
          if (DeltaCallback != null)
          {
            _groupDelta = DeltaCallback(pathIn, _norms, 0, 0);
            if (group.isReversed) _groupDelta = -_groupDelta;
            absDelta = Math.Abs(_groupDelta);
          }

          if (_groupDelta < 1) continue;
          Point64 pt = pathIn[0];
          // single vertex so build a circle or square ...
          if (group.joinType == JoinType.Round)
          {
            double radius = absDelta;
            int steps = _stepsPerRad > 0 ? (int) Math.Ceiling(_stepsPerRad * 2 * InternalClipper.PI) : 0; //#617
            Path64 ellipse = Clipper.Ellipse(pt, radius, radius, steps);
            _pathOut.Clear();
            _pathOut.AddRange(ellipse);
#if USINGZ
            for (int i = 0; i < _pathOut.Count; i++)
              _pathOut[i] = new Point64(_pathOut[i].X, _pathOut[i].Y, pt.Z);
#endif
          }
          else
          {
            long d = (long) Math.Ceiling(absDelta);
            Rect64 r = new Rect64(pt.X - d, pt.Y - d, pt.X + d, pt.Y + d);
            Path64 asPath = r.AsPath();
            _pathOut.Clear();
            _pathOut.AddRange(asPath);
#if USINGZ
            for (int i = 0; i < _pathOut.Count; i++)
              _pathOut[i] = new Point64(_pathOut[i].X, _pathOut[i].Y, pt.Z);
#endif
          }

          _solution!.Add(new Path64(_pathOut));
          continue;
        } // end of offsetting a single point

        if ((pathLen == 2) && (group.endType == EndType.Joined))
          _endType = (group.joinType == JoinType.Round) ?
            EndType.Round :
            EndType.Square;

        BuildNormals(pathIn);
        if (_endType == EndType.Polygon) OffsetPolygon(group, pathIn);
        else if (_endType == EndType.Joined) OffsetOpenJoined(group, pathIn);
        else OffsetOpenPath(group, pathIn);
      }
    }

    private long CalcSolutionCapacity()
    {
      long result = 0;
      foreach (Group g in _groups)
        result += (g.endType == EndType.Joined) ? (long) g.pathsIn.Count * 2 : g.pathsIn.Count;
      return result;
    }

    private bool CheckReverseOrientation()
    {
      // nb: this assumes there's consistency in orientation between groups
      bool isReversedOrientation = false;
      foreach (Group g in _groups)
        if (g.endType == EndType.Polygon)
        {
          isReversedOrientation = g.isReversed;
          break;
        }
      return isReversedOrientation;
    }

    private void ExecuteInternal(double delta)
    {
      _errorCode = 0;
      if (_groups.Count == 0) return;

      if (Math.Abs(delta) < 0.5) // ie: offset is insignificant
      {
        foreach (Group group in _groups)
          foreach (Path64 path in group.pathsIn)
            _solution!.Add(path);
      }
      else
      {
        long totalPoints = 0;
        foreach (Group g in _groups)
          foreach (Path64 p in g.pathsIn) totalPoints += p.Count;

        if (_groups.Count > 1 && BulkOps.ShouldParallelize(totalPoints))
        {
          // nb: each group is completely independent, so the groups are offset
          // on separate workers and merged in the original group order
          Paths64[] parts = OffsetGroupsInParallel(delta);
          foreach (Paths64 part in parts) _solution!.AddRange(part);
        }
        else
        {
          _tempLim = (_miterLimit <= 1) ?
            2.0 :
            2.0 / (_miterLimit * _miterLimit);

          _delta = delta;
          foreach (Group group in _groups)
          {
            DoGroupOffset(group);
            if (_errorCode == 0) continue; // all OK
            _solution!.Clear();
          }
        }
      }

      if (_solution!.Count == 0) return;

      bool pathsReversed = CheckReverseOrientation();
      // clean up self-intersections ...
      Clipper64 c = new Clipper64();
      c.PreserveCollinear = _preserveCollinear;
      // the solution should retain the orientation of the input
      c.ReverseSolution = _reverseSolution != pathsReversed;
#if USINGZ
      c.SetZCallback(ZCB);
#endif
      c.AddSubject(_solution);
      if (_solutionTree != null)
      {
        if (pathsReversed)
          c.Execute(ClipType.Union, FillRule.Negative, _solutionTree);
        else
          c.Execute(ClipType.Union, FillRule.Positive, _solutionTree);
      }
      else
      {
        if (pathsReversed)
          c.Execute(ClipType.Union, FillRule.Negative, _solution);
        else
          c.Execute(ClipType.Union, FillRule.Positive, _solution);
      }
    }

    /// <summary>
    /// Offsets every group on its own worker. The per group state (_pathOut,
    /// _norms, _groupDelta, arc steps ...) is private to each worker, and the
    /// outputs are written at stable indices so the merged result is identical
    /// to the single threaded one.
    /// </summary>
    private Paths64[] OffsetGroupsInParallel(double delta)
    {
      int count = _groups.Count;
      Paths64[] parts = new Paths64[count];
      System.Threading.Tasks.Parallel.For(0, count, i =>
      {
        Group g = _groups[i];
        ClipperOffset worker = new ClipperOffset(_miterLimit, _arcTolerance,
          _preserveCollinear, _reverseSolution);
        worker.DeltaCallback = DeltaCallback;
#if USINGZ
        worker.ZCallback = ZCallback;
#endif
        Paths64 input = new Paths64(g.pathsIn.Count);
        for (int p = 0; p < g.pathsIn.Count; p++) input.Add(g.pathsIn[p]);
        worker.AddPaths(input, g.joinType, g.endType);
        Paths64 part = new Paths64();
        worker.OffsetGroupsOnly(delta, part);
        parts[i] = part;
      });
      return parts;
    }

    /// <summary>Runs the group offsets into the given solution (no final union).</summary>
    private void OffsetGroupsOnly(double delta, Paths64 solution)
    {
      _tempLim = (_miterLimit <= 1) ?
        2.0 :
        2.0 / (_miterLimit * _miterLimit);
      _delta = delta;
      Paths64? saved = _solution;
      _solution = solution;
      foreach (Group group in _groups)
      {
        DoGroupOffset(group);
        if (_errorCode != 0) solution.Clear();
      }
      _solution = saved;
    }

    public void Execute(double delta, Paths64 solution)
    {
      solution.Clear();
      _solution = solution;
      _solutionTree = null;
      ExecuteInternal(delta);
    }

    public void Execute(double delta, PolyTree64 solutionTree)
    {
      solutionTree.Clear();
      _solutionTree = solutionTree;
      _solution = new Paths64();
      ExecuteInternal(delta);
      _solution = null;
    }

    public void Execute(DeltaCallback64 deltaCallback, Paths64 solution)
    {
      DeltaCallback = deltaCallback;
      Execute(1.0, solution);
    }
  }
}
