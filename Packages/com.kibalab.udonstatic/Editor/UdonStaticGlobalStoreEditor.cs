using System;
using System.Globalization;
using System.IO;
using System.Linq;
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
        private const string WidthPrefsKey = "K13A.UdonStatic.GlobalStoreEditor.ColumnWidths";

        private static readonly string[] ColumnNames = { "Class", "Field", "Type", "Storage", "Slot" };
        private static readonly float[] DefaultColumnWidths = { 360f, 140f, 120f, 110f, 48f };

        private Vector2 _scroll;
        private StaticFieldCatalog _catalog;
        private float[] _columnWidths;
        private int _resizingColumn = -1;
        private float _resizeStartX;
        private float _resizeStartWidth;

        private void OnEnable()
        {
            _columnWidths = LoadColumnWidths();
            RefreshCatalog();
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
                    RefreshCatalog();
                }

                if (GUILayout.Button("Refresh"))
                {
                    RefreshCatalog();
                }
            }

            EditorGUILayout.Space();
            _catalog ??= UdonStaticSceneStoreUtility.CollectProjectCatalog();
            EditorGUILayout.LabelField($"Declared Static Fields ({_catalog.Count})", EditorStyles.boldLabel);

            if (_catalog.Count == 0)
            {
                EditorGUILayout.HelpBox("No UdonStatic fields were found in UdonSharpBehaviour sources.", MessageType.None);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll, true, true);
            float tableWidth = UdonStaticColumnLayout.GetTotalWidth(_columnWidths);

            Rect headerRect = GUILayoutUtility.GetRect(tableWidth, HeaderHeight, GUILayout.ExpandWidth(false));
            DrawHeader(headerRect);

            foreach (var field in _catalog.Fields.OrderBy(static field => field.QualifiedName))
            {
                Rect rowRect = GUILayoutUtility.GetRect(tableWidth, RowHeight, GUILayout.ExpandWidth(false));
                DrawFieldRow(rowRect, field);
            }

            EditorGUILayout.EndScrollView();
        }

        private void RefreshCatalog()
        {
            _catalog = UdonStaticSceneStoreUtility.CollectProjectCatalog();
            Repaint();
        }

        private void DrawHeader(Rect rowRect)
        {
            for (var i = 0; i < ColumnNames.Length; i++)
            {
                Rect cellRect = UdonStaticColumnLayout.GetColumnRect(rowRect, _columnWidths, i);
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
            Rect splitterRect = UdonStaticColumnLayout.GetSplitterRect(rowRect, _columnWidths, column);
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

        private void DrawFieldRow(Rect rowRect, StaticFieldInfo field)
        {
            if (Event.current.type == EventType.Repaint && rowRect.Contains(Event.current.mousePosition))
            {
                EditorGUI.DrawRect(rowRect, new Color(0.22f, 0.36f, 0.58f, 0.18f));
            }

            DrawClickableCell(UdonStaticColumnLayout.GetColumnRect(rowRect, _columnWidths, 0), field.FullClassName, field);
            DrawClickableCell(UdonStaticColumnLayout.GetColumnRect(rowRect, _columnWidths, 1), field.Name, field);
            DrawClickableCell(UdonStaticColumnLayout.GetColumnRect(rowRect, _columnWidths, 2), field.TypeName, field);
            DrawClickableCell(UdonStaticColumnLayout.GetColumnRect(rowRect, _columnWidths, 3), field.Storage.ArrayName, field);
            DrawClickableCell(UdonStaticColumnLayout.GetColumnRect(rowRect, _columnWidths, 4), field.Slot.ToString(), field);
        }

        private static void DrawClickableCell(Rect cellRect, string text, StaticFieldInfo field)
        {
            EditorGUIUtility.AddCursorRect(cellRect, MouseCursor.Link);

            var content = new GUIContent(text, text + "\nClick to select the declaring script.");
            EditorGUI.LabelField(Pad(cellRect), content);

            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && cellRect.Contains(current.mousePosition))
            {
                SelectScript(field);
                current.Use();
            }
        }

        private static Rect Pad(Rect rect)
        {
            return new Rect(rect.x + 4f, rect.y, Mathf.Max(0f, rect.width - 8f), rect.height);
        }

        private static void SelectScript(StaticFieldInfo field)
        {
            UnityEngine.Object script = LoadScriptAsset(field.FilePath);
            if (script == null)
            {
                Debug.LogWarning("UdonStatic source script not found: " + field.FilePath);
                return;
            }

            Selection.activeObject = script;
            EditorGUIUtility.PingObject(script);
        }

        private static UnityEngine.Object LoadScriptAsset(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return null;

            string assetPath = NormalizeAssetPath(filePath);
            return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
        }

        private static string NormalizeAssetPath(string filePath)
        {
            string path = filePath.Replace('\\', '/');
            if (!Path.IsPathRooted(path))
                return path;

            string projectRoot = Directory.GetCurrentDirectory().Replace('\\', '/');
            if (path.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
                return path.Substring(projectRoot.Length + 1);

            return path;
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

        public static float GetTotalWidth(float[] columnWidths)
        {
            return columnWidths.Sum() + SplitterWidth * (columnWidths.Length - 1);
        }
    }
}
