using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using K13A.UdonStatic.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnityEditor;
using UnityEngine;

namespace K13A.UdonStatic.Editor
{
    [InitializeOnLoad]
    internal static class UdonStaticSceneStoreUtility
    {
        private static readonly object PendingSyncLock = new object();
        private static StaticFieldCatalog _pendingCatalog;

        static UdonStaticSceneStoreUtility()
        {
            EditorApplication.delayCall += SyncFromProjectSources;
            EditorApplication.update += FlushPendingSync;
        }

        public static void SyncFromProjectSources()
        {
            var catalog = StaticFieldCatalog.Collect(FindProjectSources());
            SyncStore(catalog);
        }

        [MenuItem("K13A/UdonStatic/Sync Global Store")]
        private static void SyncFromMenu()
        {
            SyncFromProjectSources();
        }

        public static void SyncStore(StaticFieldCatalog catalog)
        {
            var store = EnsureSingleStore();
            if (store == null || catalog == null)
                return;

            SyncStorage(store, catalog, "IntData", typeof(int));
            SyncStorage(store, catalog, "FloatData", typeof(float));
            SyncStorage(store, catalog, "BoolData", typeof(bool));
            SyncStorage(store, catalog, "StringData", typeof(string));
            SyncStorage(store, catalog, "LongData", typeof(long));
            SyncStorage(store, catalog, "DoubleData", typeof(double));
            SyncStorage(store, catalog, "Vector2Data", typeof(Vector2));
            SyncStorage(store, catalog, "Vector3Data", typeof(Vector3));
            SyncStorage(store, catalog, "QuaternionData", typeof(Quaternion));
            SyncStorage(store, catalog, "ColorData", typeof(Color));
            SyncStorage(store, catalog, "GameObjectData", typeof(GameObject));
            SyncStorage(store, catalog, "TransformData", typeof(Transform));
            SyncStorage(store, catalog, "ObjectData", typeof(UnityEngine.Object));

            MarkDirty(store);
        }

        public static void QueueSyncStore(StaticFieldCatalog catalog)
        {
            if (catalog == null)
                return;

            lock (PendingSyncLock)
            {
                _pendingCatalog = catalog;
            }
        }

        private static void FlushPendingSync()
        {
            StaticFieldCatalog catalog;
            lock (PendingSyncLock)
            {
                catalog = _pendingCatalog;
                _pendingCatalog = null;
            }

            if (catalog != null)
                SyncStore(catalog);
        }

        private static IEnumerable<UdonStaticSource> FindProjectSources()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;

                string source;
                try
                {
                    source = System.IO.File.ReadAllText(path);
                }
                catch
                {
                    continue;
                }

                yield return new UdonStaticSource(source, path);
            }
        }

        private static UdonStaticGlobalStore EnsureSingleStore()
        {
            var stores = UnityEngine.Object.FindObjectsOfType<UdonStaticGlobalStore>(true);
            var store = stores.FirstOrDefault();

            if (store == null)
            {
                var gameObject = new GameObject(UdonStaticLowerer.StoreObjectName);
                store = gameObject.AddComponent<UdonStaticGlobalStore>();
            }

            store.gameObject.name = UdonStaticLowerer.StoreObjectName;

            foreach (var extra in stores.Skip(1))
            {
                if (extra != null)
                {
                    UnityEngine.Object.DestroyImmediate(extra.gameObject);
                }
            }

            return store;
        }

        private static void SyncStorage(UdonStaticGlobalStore store, StaticFieldCatalog catalog, string dataFieldName, Type elementType)
        {
            var keyFieldName = dataFieldName.Replace("Data", "Keys");
            var keyField = typeof(UdonStaticGlobalStore).GetField(keyFieldName);
            var dataField = typeof(UdonStaticGlobalStore).GetField(dataFieldName);

            if (keyField == null || dataField == null)
                return;

            var fields = catalog.Fields
                .Where(field => field.Storage.ArrayName == dataFieldName)
                .OrderBy(static field => field.Slot)
                .ToArray();

            var oldKeys = keyField.GetValue(store) as string[] ?? Array.Empty<string>();
            var oldData = dataField.GetValue(store) as Array;
            var existing = new Dictionary<string, object>();

            if (oldData != null)
            {
                for (var i = 0; i < oldKeys.Length && i < oldData.Length; i++)
                {
                    existing[oldKeys[i]] = oldData.GetValue(i);
                }
            }

            var keys = fields.Select(static field => field.QualifiedName).ToArray();
            var data = Array.CreateInstance(elementType, fields.Length);

            for (var i = 0; i < fields.Length; i++)
            {
                var value = existing.TryGetValue(fields[i].QualifiedName, out var existingValue)
                    ? existingValue
                    : EvaluateInitializer(fields[i], elementType);

                data.SetValue(value, i);
            }

            keyField.SetValue(store, keys);
            dataField.SetValue(store, data);
        }

        private static object EvaluateInitializer(StaticFieldInfo field, Type elementType)
        {
            if (field.Initializer == null)
                return DefaultValue(elementType);

            var expression = field.Initializer;

            switch (expression)
            {
                case LiteralExpressionSyntax literal:
                    return ConvertLiteral(literal, elementType);
                case PrefixUnaryExpressionSyntax prefix when
                    prefix.IsKind(SyntaxKind.UnaryMinusExpression) &&
                    prefix.Operand is LiteralExpressionSyntax operand:
                {
                    var value = ConvertLiteral(operand, elementType);
                    switch (value)
                    {
                        case int intValue:
                            return -intValue;
                        case float floatValue:
                            return -floatValue;
                        case long longValue:
                            return -longValue;
                        case double doubleValue:
                            return -doubleValue;
                    }

                    break;
                }
            }

            var text = expression.ToString();
            if (elementType == typeof(Vector2) && (text == "Vector2.zero" || text == "UnityEngine.Vector2.zero"))
            {
                return Vector2.zero;
            }

            if (elementType == typeof(Vector3) && (text == "Vector3.zero" || text == "UnityEngine.Vector3.zero"))
            {
                return Vector3.zero;
            }

            if (elementType == typeof(Quaternion) && (text == "Quaternion.identity" || text == "UnityEngine.Quaternion.identity"))
            {
                return Quaternion.identity;
            }

            if (elementType == typeof(Color) && (text == "Color.clear" || text == "UnityEngine.Color.clear"))
            {
                return Color.clear;
            }

            return DefaultValue(elementType);
        }

        private static object ConvertLiteral(LiteralExpressionSyntax literal, Type elementType)
        {
            var value = literal.Token.Value;
            if (value == null)
                return DefaultValue(elementType);

            if (elementType.IsInstanceOfType(value))
                return value;

            if (elementType == typeof(float))
                return Convert.ToSingle(value, CultureInfo.InvariantCulture);
            if (elementType == typeof(double))
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (elementType == typeof(int))
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            if (elementType == typeof(long))
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            if (elementType == typeof(bool))
                return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            if (elementType == typeof(string))
                return Convert.ToString(value, CultureInfo.InvariantCulture);

            return DefaultValue(elementType);
        }

        private static object DefaultValue(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        private static void MarkDirty(UdonStaticGlobalStore store)
        {
            var setDirty = typeof(EditorUtility)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .FirstOrDefault(static method => method.Name == "SetDirty" && method.GetParameters().Length == 1);

            setDirty?.Invoke(null, new object[] { store.gameObject });
        }
    }
}