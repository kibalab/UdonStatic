using NUnit.Framework;

namespace K13A.UdonStatic.Editor.Tests
{
    public sealed class UdonStaticLowererTests
    {
        [Test]
        public void StaticFieldsLowerToGlobalStoreData()
        {
            const string source = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    private static int Counter = 10;
    private static float Elapsed;
    public int Visible;

    private void Update()
    {
        Counter++;
        Elapsed = Elapsed + 1f;
        Visible = Counter;
    }
}";

            UdonStaticLoweringResult result = UdonStaticLowerer.LowerSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Source, Does.Not.Contain("static int Counter"));
            Assert.That(result.Source, Does.Not.Contain("static float Elapsed"));
            Assert.That(result.Source, Does.Contain("private K13A.UdonStatic.Runtime.UdonStaticGlobalStore __udonStaticStore;"));
            Assert.That(result.Source, Does.Contain("private K13A.UdonStatic.Runtime.UdonStaticGlobalStore __UdonStatic_GetStore()"));
            Assert.That(result.Source, Does.Contain("__UdonStatic_GetStore().IntData[0]++;"));
            Assert.That(result.Source, Does.Contain("__UdonStatic_GetStore().FloatData[0] = __UdonStatic_GetStore().FloatData[0] + 1f;"));
            Assert.That(result.Source, Does.Contain("Visible = __UdonStatic_GetStore().IntData[0];"));
        }

        [Test]
        public void SameClassQualifiedAccessIsLoweredToGlobalStore()
        {
            const string source = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public static int Counter;

    private void ResetCounter()
    {
        TestBehaviour.Counter = 25;
    }
}";

            UdonStaticLoweringResult result = UdonStaticLowerer.LowerSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Source, Does.Contain("__UdonStatic_GetStore().IntData[0] = 25;"));
            Assert.That(result.Source, Does.Not.Contain("TestBehaviour.Counter"));
        }

        [Test]
        public void CrossClassQualifiedAccessIsLoweredWithSharedCatalog()
        {
            const string first = @"
using UdonSharp;

public class FirstBehaviour : UdonSharpBehaviour
{
    public static int Counter = 5;
}";

            const string second = @"
using UdonSharp;

public class SecondBehaviour : UdonSharpBehaviour
{
    public int Visible;

    private void Update()
    {
        Visible = FirstBehaviour.Counter;
    }
}";

            StaticFieldCatalog catalog = StaticFieldCatalog.Collect(new[]
            {
                new UdonStaticSource(first, "FirstBehaviour.cs"),
                new UdonStaticSource(second, "SecondBehaviour.cs")
            });

            UdonStaticLoweringResult result = UdonStaticLowerer.LowerSource(second, catalog);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Source, Does.Contain("Visible = __UdonStatic_GetStore().IntData[0];"));
        }

        [Test]
        public void LocalVariablesCanShadowStaticFields()
        {
            const string source = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    private static int Counter;
    public int Visible;

    private void Run()
    {
        int Counter = 5;
        Visible = Counter;
    }
}";

            UdonStaticLoweringResult result = UdonStaticLowerer.LowerSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Source, Does.Contain("int Counter = 5;"));
            Assert.That(result.Source, Does.Contain("Visible = Counter;"));
        }

        [Test]
        public void NonUdonSharpBehaviourClassesAreIgnored()
        {
            const string source = @"
public class PlainClass
{
    private static int Counter;
}";

            UdonStaticLoweringResult result = UdonStaticLowerer.LowerSource(source);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Source, Is.EqualTo(source));
        }
    }
}
