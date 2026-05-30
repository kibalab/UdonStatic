using System.Linq;
using K13A.UdonStatic.Runtime;
using UnityEditor;
using UnityEngine;

namespace K13A.UdonStatic.Editor
{
    [CustomEditor(typeof(UdonStaticGlobalStore))]
    internal sealed class UdonStaticGlobalStoreEditor : UnityEditor.Editor
    {
        private Vector2 _scroll;
        private StaticFieldCatalog _catalog;

        private void OnEnable()
        {
            RefreshCatalog();
        }

        public override void OnInspectorGUI()
        {
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

            DrawHeader();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            foreach (var field in _catalog.Fields.OrderBy(static field => field.QualifiedName))
            {
                DrawFieldRow(field);
            }

            EditorGUILayout.EndScrollView();
        }

        private void RefreshCatalog()
        {
            _catalog = UdonStaticSceneStoreUtility.CollectProjectCatalog();
            Repaint();
        }

        private static void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Class", EditorStyles.toolbarButton, GUILayout.MinWidth(180));
                GUILayout.Label("Field", EditorStyles.toolbarButton, GUILayout.MinWidth(120));
                GUILayout.Label("Type", EditorStyles.toolbarButton, GUILayout.MinWidth(100));
                GUILayout.Label("Storage", EditorStyles.toolbarButton, GUILayout.Width(110));
                GUILayout.Label("Slot", EditorStyles.toolbarButton, GUILayout.Width(48));
            }
        }

        private static void DrawFieldRow(StaticFieldInfo field)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.SelectableLabel(field.FullClassName, GUILayout.Height(EditorGUIUtility.singleLineHeight), GUILayout.MinWidth(180));
                EditorGUILayout.SelectableLabel(field.Name, GUILayout.Height(EditorGUIUtility.singleLineHeight), GUILayout.MinWidth(120));
                EditorGUILayout.SelectableLabel(field.TypeName, GUILayout.Height(EditorGUIUtility.singleLineHeight), GUILayout.MinWidth(100));
                EditorGUILayout.SelectableLabel(field.Storage.ArrayName, GUILayout.Height(EditorGUIUtility.singleLineHeight), GUILayout.Width(110));
                EditorGUILayout.SelectableLabel(field.Slot.ToString(), GUILayout.Height(EditorGUIUtility.singleLineHeight), GUILayout.Width(48));
            }
        }
    }
}
