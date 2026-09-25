using System;
using Clipper2Lib;

namespace Clipper2.WebDemo.Demo;

/// <summary>The animations the page can play.</summary>
public enum Scenario { MovingClip, ClipWindow, GrowingOffset, Simplifying, GrowingUnion }

/// <summary>One frame of an animation: the inputs for that instant.</summary>
public readonly record struct FrameInputs(Paths64 Subject, Paths64 Clip, double Delta, double Epsilon, Rect64? Rect);

/// <summary>
/// The scenarios behind the "Animation" section. Each one animates exactly one
/// input of one operation, so what the operation does is visible frame by frame
/// instead of being described: the shape that moves, the width that grows, the
/// vertices that disappear, the paths that get merged.
/// </summary>
public static class Scenarios
{
  public static readonly Scenario[] All =
  {
    Scenario.MovingClip, Scenario.ClipWindow, Scenario.GrowingOffset,
    Scenario.Simplifying, Scenario.GrowingUnion
  };

  /// <summary>How long one cycle of a scenario takes.</summary>
  public const double PeriodMilliseconds = 3600;

  public static OpKind OpFor(Scenario scenario) => scenario switch
  {
    Scenario.MovingClip => OpKind.Intersect,
    Scenario.ClipWindow => OpKind.RectClip,
    Scenario.GrowingOffset => OpKind.Offset,
    Scenario.Simplifying => OpKind.Simplify,
    _ => OpKind.Union
  };

  /// <summary>The shapes a scenario starts from (deterministic).</summary>
  public static (Paths64 Subject, Paths64 Clip) Setup(Scenario scenario, int seed)
  {
    Point64 centre = Shapes.Centre;
    return scenario switch
    {
      Scenario.MovingClip => (
        new Paths64 { Shapes.Star(centre, 200, 88, 8, 12) },
        new Paths64 { Shapes.Blob(new Random(seed), centre, 150, 14) }),

      Scenario.ClipWindow => (
        Shapes.RandomPaths(seed, 9, 22, 130, Shapes.WorldWidth, Shapes.WorldHeight),
        new Paths64()),

      Scenario.GrowingOffset => (
        new Paths64 { Shapes.Blob(new Random(seed), centre, 150, 20) },
        new Paths64()),

      // a dense outline: simplification has something to remove
      Scenario.Simplifying => (
        new Paths64 { Shapes.Blob(new Random(seed), centre, 230, 420) },
        new Paths64()),

      _ => (Shapes.RandomPaths(seed, 12, 16, 70, Shapes.WorldWidth, Shapes.WorldHeight),
        new Paths64())
    };
  }

  /// <summary>The inputs for <paramref name="phase"/> of the cycle, in [0, 1).</summary>
  public static FrameInputs Frame(Scenario scenario, double phase, Paths64 subject, Paths64 clip)
  {
    double turn = phase * 2 * Math.PI;
    switch (scenario)
    {
      case Scenario.MovingClip:
      {
        // the clip shape orbits the subject and turns while it does so - the
        // intersection is recomputed for every frame
        long cx = Shapes.WorldWidth / 2 + (long) (300 * Math.Sin(turn));
        long cy = Shapes.WorldHeight / 2 + (long) (170 * Math.Sin(2 * turn));
        Rect64 clipBounds = Clipper.GetBounds(clip);
        Point64 clipCentre = clipBounds.MidPoint();
        Paths64 moved = Clipper.TranslatePaths(clip, cx - clipCentre.X, cy - clipCentre.Y);
        return new FrameInputs(subject, Rotate(moved, phase * 180, new Point64(cx, cy)), 0, 0, null);
      }

      case Scenario.ClipWindow:
      {
        // a window sliding over the drawing: everything outside it disappears
        long width = 460, height = 300;
        long left = 20 + (long) ((Shapes.WorldWidth - width - 40) * phase);
        long top = Shapes.WorldHeight / 2 - height / 2 + (long) (70 * Math.Sin(3 * turn));
        return new FrameInputs(subject, clip, 0, 0, new Rect64(left, top, left + width, top + height));
      }

      case Scenario.GrowingOffset:
      {
        // grows and shrinks: the same shape, a fixed distance away, with joins
        double delta = -35 + 150 * phase;
        return new FrameInputs(subject, clip, delta, 0, null);
      }

      case Scenario.Simplifying:
      {
        // epsilon goes from "keep every vertex" to "keep the outline only"
        double epsilon = 0.5 + 55 * phase;
        return new FrameInputs(subject, clip, 0, epsilon, null);
      }

      default:
      {
        // the shapes arrive one by one and the union absorbs them
        int visible = 1 + (int) (phase * (subject.Count - 1) + 0.5);
        if (visible > subject.Count) visible = subject.Count;
        Paths64 partial = new(visible);
        for (int i = 0; i < visible; i++) partial.Add(subject[i]);
        return new FrameInputs(partial, clip, 0, 0, null);
      }
    }
  }

  /// <summary>Turns a shape around a centre - the library has translate but no rotate.</summary>
  private static Paths64 Rotate(Paths64 paths, double degrees, Point64 centre)
  {
    double radians = degrees * Math.PI / 180.0;
    double cos = Math.Cos(radians), sin = Math.Sin(radians);
    Paths64 rotated = new(paths.Count);
    foreach (Path64 path in paths)
    {
      Path64 turn = new(path.Count);
      foreach (Point64 point in path)
      {
        double dx = point.X - centre.X;
        double dy = point.Y - centre.Y;
        turn.Add(new Point64(centre.X + (long) Math.Round(dx * cos - dy * sin),
                             centre.Y + (long) Math.Round(dx * sin + dy * cos)));
      }
      rotated.Add(turn);
    }
    return rotated;
  }
}
