using NUnit.Framework;
using K13A.UdonStatic.Runtime;
using UnityEngine;

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
        public void CatalogKeepsClassFieldAndTypeForInspector()
        {
            const string source = @"
using UdonSharp;

namespace Example
{
    public class TestBehaviour : UdonSharpBehaviour
    {
        public static int Score = 5;
        private static bool Enabled = true;
    }
}";

            StaticFieldCatalog catalog = StaticFieldCatalog.Collect(new[]
            {
                new UdonStaticSource(source, "TestBehaviour.cs")
            });

            Assert.That(catalog.Count, Is.EqualTo(2));
            Assert.That(catalog.Fields[0].FullClassName, Is.EqualTo("Example.TestBehaviour"));
            Assert.That(catalog.Fields[0].Name, Is.EqualTo("Enabled"));
            Assert.That(catalog.Fields[0].TypeName, Is.EqualTo("bool"));
            Assert.That(catalog.Fields[1].FullClassName, Is.EqualTo("Example.TestBehaviour"));
            Assert.That(catalog.Fields[1].Name, Is.EqualTo("Score"));
            Assert.That(catalog.Fields[1].TypeName, Is.EqualTo("int"));
        }

        [Test]
        public void ColumnLayoutUsesSameConfiguredWidthsForHeaderAndRows()
        {
            var widths = new[] { 320f, 140f, 100f };
            var header = new Rect(10f, 20f, 600f, 18f);
            var row = new Rect(10f, 42f, 600f, 18f);

            Rect headerColumn = UdonStaticColumnLayout.GetColumnRect(header, widths, 1);
            Rect rowColumn = UdonStaticColumnLayout.GetColumnRect(row, widths, 1);

            Assert.That(headerColumn.x, Is.EqualTo(rowColumn.x).Within(0.001f));
            Assert.That(headerColumn.width, Is.EqualTo(140f).Within(0.001f));
            Assert.That(rowColumn.width, Is.EqualTo(140f).Within(0.001f));
        }

        [Test]
        public void ColumnResizeClampsToMinimumWidth()
        {
            var widths = new[] { 100f, 100f };

            UdonStaticColumnLayout.ApplyResize(widths, 0, -1000f);

            Assert.That(widths[0], Is.EqualTo(UdonStaticColumnLayout.MinColumnWidth).Within(0.001f));
        }

        [Test]
        public void ColumnLayoutExpandsLastColumnToFillAvailableWidth()
        {
            var widths = new[] { 100f, 50f };
            float availableWidth = UdonStaticColumnLayout.GetTotalWidth(widths) + 80f;

            float[] drawWidths = UdonStaticColumnLayout.GetDrawWidths(widths, availableWidth);

            Assert.That(drawWidths[0], Is.EqualTo(100f).Within(0.001f));
            Assert.That(drawWidths[1], Is.EqualTo(130f).Within(0.001f));
            Assert.That(UdonStaticColumnLayout.GetTotalWidth(drawWidths), Is.EqualTo(availableWidth).Within(0.001f));
        }

        [Test]
        public void StoreFieldRowSplitsQualifiedNameForInspector()
        {
            UdonStaticStoreFieldRow row = UdonStaticStoreFieldRow.FromQualifiedName(
                "Example.Nested.TestBehaviour.Score",
                "int",
                "IntData",
                3);

            Assert.That(row.FullClassName, Is.EqualTo("Example.Nested.TestBehaviour"));
            Assert.That(row.Name, Is.EqualTo("Score"));
            Assert.That(row.TypeName, Is.EqualTo("int"));
            Assert.That(row.StorageName, Is.EqualTo("IntData"));
            Assert.That(row.Slot, Is.EqualTo(3));
        }

        [Test]
        public void GlobalStoreSerializedStorageFieldsAreHiddenInInspector()
        {
            var fields = typeof(UdonStaticGlobalStore).GetFields();

            foreach (var field in fields)
            {
                if (!field.Name.EndsWith("Keys") && !field.Name.EndsWith("Data"))
                    continue;

                Assert.That(
                    field.GetCustomAttributes(typeof(HideInInspector), false),
                    Is.Not.Empty,
                    field.Name + " must not expose serialized defaults in the inspector");
            }
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
