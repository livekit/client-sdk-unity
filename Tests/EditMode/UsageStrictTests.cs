using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace LiveKit.EditModeTests
{
    public class UsageStrictTests
    {
        private readonly IReadOnlyList<string> ignoreFiles = new[] { "FFIClient.cs", "Ffi.cs" };
        private const string RuntimePath = "Assets/client-sdk-unity/Runtime/Scripts";

        //can be reworked with syntax tree, but current version is faster to implement
        [Test, Ignore("Testing on CI")]
        [TestCase("FfiResponse")]
        [TestCase("FfiRequest")]
        public void NoManualCreateNew(string className)
        {
            var files = Directory.GetFiles(RuntimePath, "*.cs", SearchOption.AllDirectories);
            var rejected = files.Where(
                f => ignoreFiles.Any(i => Path.GetFileName(f) == i) == false
                     && File.ReadAllText(f!).Contains($"new {className}(")
            ).ToList();
            Assert.IsEmpty(
                rejected,
                $"Forbidden manual creation \"new {className}\", use pools instead:\n{string.Join("\n", rejected)}"
            );
        }
    }
}