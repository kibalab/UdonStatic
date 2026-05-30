using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using K13A.UdonStatic.Runtime;
using UnityEditor;
using UnityEngine;

namespace K13A.UdonStatic.Editor
{
    [CustomEditor(typeof(UdonStaticGlobalStore))]
    internal sealed class UdonStaticGlobalStoreEditor : UnityEditor.Editor
    {
        private const float HeaderHeight = 20f;
        private const float RowHeight = 18f;
        private const float MinListHeight = 220f;
        private const string WidthPrefsKey = "K13A.UdonStatic.GlobalStoreEditor.ColumnWidths";

        private static readonly string[] ColumnNames = { "Class", "Field", "Type", "Storage", "Slot" };
        private static readonly float[] DefaultColumnWidths = { 360f, 140f, 120f, 110f, 48f };

        private IReadOnlyList<UdonStaticStoreFieldRow> _rows;
        private float[] _columnWidths;
        private float[] _drawColumnWidths;
        private int _resizingColumn = -1;
        private float _resizeStartX;
        private float _resizeStartWidth;

        private void OnEnable()
        {
            _columnWidths = LoadColumnWidths();
            RefreshRows();
        }

        private void OnDisable()
        {
            SaveColumnWidths();
        }

        public override void OnInspectorGUI()
        {
            EnsureColumnWidths();

            EditorGUILayout.LabelField("UdonStatic Global Store", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This component stores UdonStatic fields generated from source code. Serialized values are regenerated from static field declarations and cannot be edited here.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Sync From Project Sources"))
                {
                    UdonStaticSceneStoreUtility.SyncFromProjectSources();
                    RefreshRows();
                }

                if (GUILayout.Button("Refresh"))
                {
                    RefreshRows();
                }
            }

            EditorGUILayout.Space();
            _rows ??= UdonStaticStoreViewModel.BuildRows((UdonStaticGlobalStore)target);
            EditorGUILayout.LabelField($"Declared Static Fields ({_rows.Count})", EditorStyles.boldLabel);

            if (_rows.Count == 0)
            {
                EditorGUILayout.HelpBox("No UdonStatic fields were found in UdonSharpBehaviour sources.", MessageType.None);
                return;
            }

            Rect tableRect = EditorGUILayout.GetControlRect(false, HeaderHeight);
            _drawColumnWidths = UdonStaticColumnLayout.GetDrawWidths(_columnWidths, tableRect.width);

            DrawHeader(tableRect);

            foreach (var field in _rows.OrderBy(static field => field.QualifiedName))
            {
                Rect rowRect = EditorGUILayout.GetControlRect(false, RowHeight);
                DrawFieldRow(rowRect, field);
            }

            float usedHeight = HeaderHeight + _rows.Count * RowHeight;
            if (usedHeight < MinListHeight)
            {
                GUILayout.Space(MinListHeight - usedHeight);
            }
        }

        private void RefreshRows()
        {
            _rows = UdonStaticStoreViewModel.BuildRows((UdonStaticGlobalStore)target);
            Repaint();
        }

        private void DrawHeader(Rect rowRect)
        {
            for (var i = 0; i < ColumnNames.Length; i++)
            {
                Rect cellRect = UdonStaticColumnLayout.GetColumnRect(rowRect, _drawColumnWidths, i);
                GUI.Label(cellRect, ColumnNames[i], EditorStyles.toolbarButton);

                if (i < ColumnNames.Length - 1)
                {
                    DrawSplitter(rowRect, i);
                }
            }

            HandleActiveResize();
        }

        private void DrawSplitter(Rect rowRect, int column)
        {
            Rect splitterRect = UdonStaticColumnLayout.GetSplitterRect(rowRect, _drawColumnWidths, column);
            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);

            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && splitterRect.Contains(current.mousePosition))
            {
                _resizingColumn = column;
                _resizeStartX = current.mousePosition.x;
                _resizeStartWidth = _columnWidths[column];
                current.Use();
            }
        }

        private void HandleActiveResize()
        {
            if (_resizingColumn < 0)
                return;

            Event current = Event.current;
            if (current.type == EventType.MouseDrag)
            {
                _columnWidths[_resizingColumn] = _resizeStartWidth;
                UdonStaticColumnLayout.ApplyResize(_columnWidths, _resizingColumn, current.mousePosition.x - _resizeStartX);
                Repaint();
                current.Use();
            }
            else if (current.type == EventType.MouseUp || current.type == EventType.Ignore)
            {
                SaveColumnWidths();
                _resizingColumn = -1;
                current.Use();
            }
        }

        private void DrawFieldRow(Rect rowRect, UdonStaticStoreFieldRow field)
        {
            if (Event.current.type == EventType.Repaint && rowRect.Contains(Event.current.mousePosition))
            {
                EditorGUI.DrawRect(rowRect, new Color(0.22f, 0.36f, 0.58f, 0.18f));
            }

            DrawCell(UdonStaticColumnLayout.GetColumnRect(rowRect, _drawColumnWidths, 0), field.FullClassName);
            DrawCell(UdonStaticColumnLayout.GetColumnRect(rowRect, _drawColumnWidths, 1), field.Name);
            DrawCell(UdonStaticColumnLayout.GetColumnRect(rowRect, _drawColumnWidths, 2), field.TypeName);
            DrawCell(UdonStaticColumnLayout.GetColumnRect(rowRect, _drawColumnWidths, 3), field.StorageName);
            DrawCell(UdonStaticColumnLayout.GetColumnRect(rowRect, _drawColumnWidths, 4), field.Slot.ToString());
        }

        private static void DrawCell(Rect cellRect, string text)
        {
            var content = new GUIContent(text, text);
            EditorGUI.LabelField(Pad(cellRect), content);
        }

        private static Rect Pad(Rect rect)
        {
            return new Rect(rect.x + 4f, rect.y, Mathf.Max(0f, rect.width - 8f), rect.height);
        }

        private void EnsureColumnWidths()
        {
            if (_columnWidths != null && _columnWidths.Length == ColumnNames.Length)
                return;

            _columnWidths = DefaultColumnWidths.ToArray();
        }

        private static float[] LoadColumnWidths()
        {
            string saved = EditorPrefs.GetString(WidthPrefsKey, string.Empty);
            if (string.IsNullOrEmpty(saved))
                return DefaultColumnWidths.ToArray();

            string[] parts = saved.Split(',');
            if (parts.Length != DefaultColumnWidths.Length)
                return DefaultColumnWidths.ToArray();

            var widths = new float[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out widths[i]))
                    return DefaultColumnWidths.ToArray();

                widths[i] = Mathf.Max(UdonStaticColumnLayout.MinColumnWidth, widths[i]);
            }

            return widths;
        }

        private void SaveColumnWidths()
        {
            if (_columnWidths == null)
                return;

            string saved = string.Join(",", _columnWidths.Select(width => width.ToString(CultureInfo.InvariantCulture)).ToArray());
            EditorPrefs.SetString(WidthPrefsKey, saved);
        }
    }

    internal static class UdonStaticColumnLayout
    {
        public const float MinColumnWidth = 48f;
        public const float MinDrawColumnWidth = 12f;
        public const float SplitterWidth = 6f;

        public static Rect GetColumnRect(Rect rowRect, float[] columnWidths, int column)
        {
            float x = rowRect.x;
            for (var i = 0; i < column; i++)
            {
                x += columnWidths[i] + SplitterWidth;
            }

            return new Rect(x, rowRect.y, columnWidths[column], rowRect.height);
        }

        public static Rect GetSplitterRect(Rect rowRect, float[] columnWidths, int column)
        {
            Rect columnRect = GetColumnRect(rowRect, columnWidths, column);
            return new Rect(columnRect.xMax, rowRect.y, SplitterWidth, rowRect.height);
        }

        public static void ApplyResize(float[] columnWidths, int column, float delta)
        {
            columnWidths[column] = Mathf.Max(MinColumnWidth, columnWidths[column] + delta);
        }

        public static float[] GetDrawWidths(float[] columnWidths, float availableWidth)
        {
            float[] drawWidths = columnWidths.ToArray();
            if (drawWidths.Length == 0)
                return drawWidths;

            float splitterWidth = SplitterWidth * Mathf.Max(0, drawWidths.Length - 1);
            float availableColumnWidth = Mathf.Max(0.001f * drawWidths.Length, availableWidth - splitterWidth);
            float minimumDrawColumnWidth = Mathf.Min(MinDrawColumnWidth, availableColumnWidth / drawWidths.Length);
            float storedColumnWidth = drawWidths.Sum();

            if (storedColumnWidth > availableColumnWidth && storedColumnWidth > 0f)
            {
                float minimumTotalWidth = minimumDrawColumnWidth * drawWidths.Length;
                float flexibleWidth = Mathf.Max(0f, availableColumnWidth - minimumTotalWidth);
                float storedFlexibleWidth = drawWidths.Sum(width => Mathf.Max(0f, width - minimumDrawColumnWidth));

                for (var i = 0; i < drawWidths.Length; i++)
                {
                    float weight = storedFlexibleWidth > 0f
                        ? Mathf.Max(0f, drawWidths[i] - minimumDrawColumnWidth) / storedFlexibleWidth
                        : 1f / drawWidths.Length;

                    drawWidths[i] = minimumDrawColumnWidth + flexibleWidth * weight;
                }
            }

            float extraWidth = availableWidth - GetTotalWidth(drawWidths);
            if (extraWidth > 0f && drawWidths.Length > 0)
            {
                drawWidths[drawWidths.Length - 1] += extraWidth;
            }

            return drawWidths;
        }

        public static float GetTotalWidth(float[] columnWidths)
        {
            return columnWidths.Sum() + SplitterWidth * (columnWidths.Length - 1);
        }
    }

    internal static class UdonStaticStoreViewModel
    {
        public static IReadOnlyList<UdonStaticStoreFieldRow> BuildRows(UdonStaticGlobalStore store)
        {
            if (store == null)
                return new List<UdonStaticStoreFieldRow>();

            var rows = new List<UdonStaticStoreFieldRow>();
            FieldInfo[] fields = typeof(UdonStaticGlobalStore).GetFields(BindingFlags.Instance | BindingFlags.Public);

            foreach (FieldInfo dataField in fields.Where(static field => field.Name.EndsWith("Data")))
            {
                FieldInfo keyField = typeof(UdonStaticGlobalStore).GetField(dataField.Name.Replace("Data", "Keys"));
                if (keyField == null)
                    continue;

                var keys = keyField.GetValue(store) as string[];
                if (keys == null)
                    continue;

                string typeName = TypeNameForDataField(dataField);
                for (var i = 0; i < keys.Length; i++)
                {
                    if (string.IsNullOrEmpty(keys[i]))
                        continue;

                    rows.Add(UdonStaticStoreFieldRow.FromQualifiedName(keys[i], typeName, dataField.Name, i));
                }
            }

            return rows;
        }

        private static string TypeNameForDataField(FieldInfo dataField)
        {
            Type elementType = dataField.FieldType.IsArray
                ? dataField.FieldType.GetElementType()
                : dataField.FieldType;

            if (elementType == typeof(int)) return "int";
            if (elementType == typeof(float)) return "float";
            if (elementType == typeof(bool)) return "bool";
            if (elementType == typeof(string)) return "string";
            if (elementType == typeof(long)) return "long";
            if (elementType == typeof(double)) return "double";
            if (elementType == typeof(UnityEngine.Object)) return "Object";

            return elementType != null ? elementType.Name : dataField.Name.Replace("Data", string.Empty);
        }
    }

    internal sealed class UdonStaticStoreFieldRow
    {
        private UdonStaticStoreFieldRow(string fullClassName, string name, string qualifiedName, string typeName, string storageName, int slot)
        {
            FullClassName = fullClassName;
            Name = name;
            QualifiedName = qualifiedName;
            TypeName = typeName;
            StorageName = storageName;
            Slot = slot;
        }

        public string FullClassName { get; }
        public string Name { get; }
        public string QualifiedName { get; }
        public string TypeName { get; }
        public string StorageName { get; }
        public int Slot { get; }

        public static UdonStaticStoreFieldRow FromQualifiedName(string qualifiedName, string typeName, string storageName, int slot)
        {
            int lastDot = qualifiedName.LastIndexOf('.');
            if (lastDot < 0)
                return new UdonStaticStoreFieldRow(string.Empty, qualifiedName, qualifiedName, typeName, storageName, slot);

            return new UdonStaticStoreFieldRow(
                qualifiedName.Substring(0, lastDot),
                qualifiedName.Substring(lastDot + 1),
                qualifiedName,
                typeName,
                storageName,
                slot);
        }
    }
}
