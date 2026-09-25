using System;
using System.IO;

namespace Clipper2Lib.UnitTests
{
  internal static class TestData
  {
    /// <summary>
    /// Returns the full path of a test data file (copied to the output directory).
    /// </summary>
    public static string Path(string fileName)
    {
      return System.IO.Path.Combine(AppContext.BaseDirectory, "TestData", fileName);
    }
  }
}
