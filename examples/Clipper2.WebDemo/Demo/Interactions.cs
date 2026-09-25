namespace Clipper2.WebDemo.Demo;

/// <summary>A dragged vertex of the subject, in world coordinates.</summary>
public readonly record struct VertexMove(int PathIndex, int VertexIndex, long X, long Y);

/// <summary>A drag of the whole clip shape, as a world-space delta.</summary>
public readonly record struct ClipMove(long Dx, long Dy);
