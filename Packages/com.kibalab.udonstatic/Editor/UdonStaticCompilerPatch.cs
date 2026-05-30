using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace K13A.UdonStatic.Editor
{
    [InitializeOnLoad]
    internal static class UdonStaticCompilerPatch
    {
        private const string HarmonyId = "K13A.UdonStatic.CompilerPatch";
        private static readonly Harmony Harmony = new Harmony(HarmonyId);
        private static readonly object SourceLock = new object();
        private static Dictionary<string, string> _preLoweredSources = new Dictionary<string, string>();
        private static StaticFieldCatalog _activeCatalog;
        private static bool _patched;

        static UdonStaticCompilerPatch()
        {
            Patch();
            EditorApplication.delayCall += PatchOnce;
            EditorApplication.update += PatchUntilReady;
        }

        [InitializeOnLoadMethod]
        private static void InitializePatch()
        {
            Patch();
        }

        [DidReloadScripts(-10000)]
        private static void PatchAfterScriptsReload()
        {
            Patch();
        }

        private static void PatchOnce()
        {
            Patch();
        }

        private static void PatchUntilReady()
        {
            if (Patch())
                EditorApplication.update -= PatchUntilReady;
        }

        private static bool Patch()
        {
            if (_patched)
                return true;

            Type compilationContextType = AccessTools.TypeByName("UdonSharp.Compiler.CompilationContext");
            if (compilationContextType == null)
            {
                return false;
            }

            MethodInfo target = AccessTools.Method(
                compilationContextType,
                "LoadSyntaxTreesAndCreateModules",
                new[] { typeof(System.Collections.Generic.IEnumerable<string>), typeof(string[]) });

            if (target == null)
            {
                Debug.LogWarning("[UdonStatic] Could not find UdonSharp syntax-tree loader.");
                return false;
            }

            Harmony.UnpatchAll(HarmonyId);
            Harmony.Patch(
                target,
                prefix: new HarmonyMethod(typeof(UdonStaticCompilerPatch), nameof(ReplaceLoadSyntaxTrees)),
                postfix: new HarmonyMethod(typeof(UdonStaticCompilerPatch), nameof(PostLoadSyntaxTrees)));

            Type utilsType = AccessTools.TypeByName("UdonSharp.UdonSharpUtils");
            MethodInfo readFileText = AccessTools.Method(utilsType, "ReadFileTextSync", new[] { typeof(string), typeof(float) });

            if (readFileText != null)
                Harmony.Patch(readFileText, postfix: new HarmonyMethod(typeof(UdonStaticCompilerPatch), nameof(PostReadFileTextSync)));

            Type fieldSymbolType = AccessTools.TypeByName("UdonSharp.Compiler.Symbols.FieldSymbol");
            MethodInfo fieldBind = AccessTools.Method(fieldSymbolType, "Bind");
            if (fieldBind != null)
                Harmony.Patch(fieldBind, finalizer: new HarmonyMethod(typeof(UdonStaticCompilerPatch), nameof(FieldSymbolBindFinalizer)));

            Type compilerType = AccessTools.TypeByName("UdonSharp.Compiler.UdonSharpCompilerV1");
            MethodInfo compile = AccessTools.Method(compilerType, "Compile", new[] { AccessTools.TypeByName("UdonSharp.Compiler.UdonSharpCompileOptions") });
            MethodInfo compileSync = AccessTools.Method(compilerType, "CompileSync", new[] { AccessTools.TypeByName("UdonSharp.Compiler.UdonSharpCompileOptions") });

            if (compile != null)
                Harmony.Patch(compile, prefix: new HarmonyMethod(typeof(UdonStaticCompilerPatch), nameof(EnsurePatchBeforeCompile)));

            if (compileSync != null)
                Harmony.Patch(compileSync, prefix: new HarmonyMethod(typeof(UdonStaticCompilerPatch), nameof(EnsurePatchBeforeCompile)));

            _patched = true;
            Debug.Log("[UdonStatic] UdonSharp compiler patches installed.");
            return true;
        }

        private static void EnsurePatchBeforeCompile()
        {
            Patch();
        }

        private static Exception FieldSymbolBindFinalizer(object __instance, Exception __exception)
        {
            if (__exception == null ||
                __exception.Message != "Static fields are not yet supported on user defined types")
            {
                return __exception;
            }

            if (!IsUdonStaticFieldSymbol(__instance))
                return __exception;

            FieldInfo resolvedField = AccessTools.Field(__instance.GetType(), "_resolved");
            resolvedField?.SetValue(__instance, true);
            return null;
        }

        private static bool IsUdonStaticFieldSymbol(object fieldSymbol)
        {
            try
            {
                object roslynSymbol = AccessTools.Property(fieldSymbol.GetType(), "RoslynSymbol")?.GetValue(fieldSymbol);
                if (roslynSymbol == null)
                    return false;

                Type symbolType = roslynSymbol.GetType();
                bool isStatic = (bool)(symbolType.GetProperty("IsStatic")?.GetValue(roslynSymbol) ?? false);
                bool isConst = (bool)(symbolType.GetProperty("IsConst")?.GetValue(roslynSymbol) ?? false);

                if (!isStatic || isConst)
                    return false;

                object containingType = symbolType.GetProperty("ContainingType")?.GetValue(roslynSymbol);
                string typeName = containingType?.ToString() ?? string.Empty;
                return !string.IsNullOrEmpty(typeName);
            }
            catch
            {
                return false;
            }
        }

        private static bool ReplaceLoadSyntaxTrees(
            object __instance,
            IEnumerable<string> sourcePaths,
            string[] scriptingDefines,
            ref object __result)
        {
            if (sourcePaths == null)
                return true;

            List<UdonStaticSource> sources = new List<UdonStaticSource>();

            foreach (string sourcePath in sourcePaths)
            {
                if (string.IsNullOrEmpty(sourcePath) || !sourcePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    continue;

                string source;
                try
                {
                    source = File.ReadAllText(sourcePath);
                }
                catch
                {
                    continue;
                }

                sources.Add(new UdonStaticSource(source, sourcePath));
            }

            StaticFieldCatalog catalog = StaticFieldCatalog.Collect(sources);
            Dictionary<string, string> loweredSources = new Dictionary<string, string>();
            Type moduleBindingType = AccessTools.TypeByName("UdonSharp.Compiler.ModuleBinding");

            if (moduleBindingType == null)
                return true;

            Array bindings = Array.CreateInstance(moduleBindingType, sources.Count);

            for (int i = 0; i < sources.Count; i++)
            {
                UdonStaticSource source = sources[i];
                UdonStaticLoweringResult lowered = UdonStaticLowerer.LowerSource(source.Source, catalog);
                string sourceText = lowered.Changed ? lowered.Source : source.Source;

                if (lowered.Changed)
                    loweredSources[NormalizePath(source.FilePath)] = sourceText;

                SyntaxTree tree = CSharpSyntaxTree.ParseText(
                    sourceText,
                    CSharpParseOptions.Default
                        .WithDocumentationMode(DocumentationMode.None)
                        .WithPreprocessorSymbols(scriptingDefines ?? Array.Empty<string>())
                        .WithLanguageVersion(LanguageVersion.CSharp7_3));

                object binding = Activator.CreateInstance(moduleBindingType);
                AccessTools.Field(moduleBindingType, "tree")?.SetValue(binding, tree);
                AccessTools.Field(moduleBindingType, "filePath")?.SetValue(binding, source.FilePath);
                AccessTools.Field(moduleBindingType, "sourceText")?.SetValue(binding, sourceText);
                bindings.SetValue(binding, i);
            }

            lock (SourceLock)
            {
                _activeCatalog = catalog;
                _preLoweredSources = loweredSources;
            }

            AccessTools.Property(__instance.GetType(), "ModuleBindings")?.SetValue(__instance, bindings);
            __result = bindings;
            return false;
        }

        private static void PostReadFileTextSync(string filePath, ref string __result)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(__result))
                return;

            lock (SourceLock)
            {
                if (_preLoweredSources.TryGetValue(NormalizePath(filePath), out string lowered))
                    __result = lowered;
            }
        }

        private static void PostLoadSyntaxTrees(object[] __result)
        {
            if (__result == null)
                return;

            StaticFieldCatalog catalog;
            lock (SourceLock)
            {
                catalog = _activeCatalog ?? StaticFieldCatalog.Collect(__result.Select(ReadSource).Where(source => source != null));
                _preLoweredSources.Clear();
            }

            UdonStaticSceneStoreUtility.QueueSyncStore(catalog);

            foreach (object binding in __result)
                TransformBinding(binding, catalog);
        }

        private static UdonStaticSource ReadSource(object binding)
        {
            Type bindingType = binding.GetType();
            FieldInfo sourceTextField = AccessTools.Field(bindingType, "sourceText");
            FieldInfo filePathField = AccessTools.Field(bindingType, "filePath");

            string sourceText = sourceTextField?.GetValue(binding) as string;
            string filePath = filePathField?.GetValue(binding) as string;

            return string.IsNullOrEmpty(sourceText) ? null : new UdonStaticSource(sourceText, filePath);
        }

        private static void TransformBinding(object binding, StaticFieldCatalog catalog)
        {
            Type bindingType = binding.GetType();
            FieldInfo sourceTextField = AccessTools.Field(bindingType, "sourceText");
            FieldInfo treeField = AccessTools.Field(bindingType, "tree");
            FieldInfo filePathField = AccessTools.Field(bindingType, "filePath");

            string sourceText = sourceTextField?.GetValue(binding) as string;
            SyntaxTree oldTree = treeField?.GetValue(binding) as SyntaxTree;
            string filePath = filePathField?.GetValue(binding) as string;

            if (string.IsNullOrEmpty(sourceText) || oldTree == null)
                return;

            UdonStaticLoweringResult lowered = UdonStaticLowerer.LowerSource(sourceText, catalog);
            if (!lowered.Changed)
                return;

            SyntaxTree newTree = CSharpSyntaxTree.ParseText(
                lowered.Source,
                (CSharpParseOptions)oldTree.Options,
                filePath ?? oldTree.FilePath,
                System.Text.Encoding.UTF8);

            sourceTextField.SetValue(binding, lowered.Source);
            treeField.SetValue(binding, newTree);
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path).Replace('\\', '/');
            }
            catch
            {
                return path.Replace('\\', '/');
            }
        }
    }
}
