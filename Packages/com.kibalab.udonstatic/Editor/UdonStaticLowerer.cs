using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace K13A.UdonStatic.Editor
{
    internal readonly struct UdonStaticLoweringResult
    {
        public UdonStaticLoweringResult(string source, bool changed)
        {
            Source = source;
            Changed = changed;
        }

        public string Source { get; }
        public bool Changed { get; }
    }

    internal sealed class UdonStaticSource
    {
        public UdonStaticSource(string source, string filePath = "")
        {
            Source = source;
            FilePath = filePath ?? string.Empty;
        }

        public string Source { get; }
        public string FilePath { get; }
    }

    internal static class UdonStaticLowerer
    {
        public const string StoreObjectName = "__UdonStaticGlobalStore";
        public const string StoreTypeName = "K13A.UdonStatic.Runtime.UdonStaticGlobalStore";
        private const string StoreFieldName = "__udonStaticStore";
        private const string StoreGetterName = "__UdonStatic_GetStore";

        public static UdonStaticLoweringResult LowerSource(string source)
        {
            var catalog = StaticFieldCatalog.Collect(new[] { new UdonStaticSource(source) });
            return LowerSource(source, catalog);
        }

        public static UdonStaticLoweringResult LowerSource(string source, StaticFieldCatalog catalog)
        {
            if (catalog == null || catalog.Count == 0 || !MayContainUdonSharpBehaviour(source))
                return new UdonStaticLoweringResult(source, false);

            var tree = Parse(source);
            var root = tree.GetRoot();
            var lowered = new StaticFieldSyntaxRewriter(catalog).Visit(root);
            var loweredSource = lowered.NormalizeWhitespace().ToFullString();
            return new UdonStaticLoweringResult(loweredSource, loweredSource != source);
        }

        public static SyntaxTree Parse(string source)
        {
            return CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp7_3));
        }

        private static bool MayContainUdonSharpBehaviour(string source)
        {
            return source.IndexOf("UdonSharpBehaviour", StringComparison.Ordinal) >= 0;
        }

        internal static string StoreFieldNameForGeneratedCode => StoreFieldName;
        internal static string StoreGetterNameForGeneratedCode => StoreGetterName;
    }

    internal sealed class StaticFieldCatalog
    {
        private readonly Dictionary<string, StaticClassInfo> _classes;
        private readonly Dictionary<string, StaticFieldInfo> _fieldsByQualifiedName;

        private StaticFieldCatalog(Dictionary<string, StaticClassInfo> classes, IReadOnlyList<StaticFieldInfo> fields)
        {
            _classes = classes;
            Fields = fields;
            _fieldsByQualifiedName = fields.ToDictionary(field => field.QualifiedName, field => field);
        }

        public int Count => Fields.Count;
        public IReadOnlyList<StaticFieldInfo> Fields { get; }

        public static StaticFieldCatalog Collect(IEnumerable<UdonStaticSource> sources)
        {
            var pendingFields = new List<PendingStaticField>();

            foreach (UdonStaticSource source in sources)
            {
                if (
                    source.Source.IndexOf("static", StringComparison.Ordinal) < 0 ||
                    source.Source.IndexOf("UdonSharpBehaviour", StringComparison.Ordinal) < 0
                )
                {
                    continue;
                }

                var root = UdonStaticLowerer.Parse(source.Source).GetRoot();
                foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (!IsDirectUdonSharpBehaviour(declaration)) continue;

                    var className = declaration.Identifier.ValueText;
                    var fullClassName = GetFullClassName(declaration);

                    foreach (var field in declaration.Members.OfType<FieldDeclarationSyntax>())
                    {
                        if (!IsSupportedStaticField(field))
                            continue;

                        var typeName = field.Declaration.Type.ToString();
                        var storage = StorageKind.FromType(typeName);

                        foreach (var variable in field.Declaration.Variables)
                        {
                            pendingFields.Add(
                                new PendingStaticField(
                                    className,
                                    fullClassName,
                                    variable.Identifier.ValueText,
                                    typeName,
                                    storage,
                                    source.FilePath,
                                    variable.Initializer?.Value
                                )
                            );
                        }
                    }
                }
            }

            var fields = AssignSlots(pendingFields);
            var classes = fields
                .GroupBy(static field => field.FullClassName)
                .ToDictionary(
                    static group => group.Key,
                    static group => new StaticClassInfo(group.First().ClassName, group.Key, group.ToArray())
                );

            return new StaticFieldCatalog(classes, fields);
        }

        public bool TryGetClass(string classNameOrFullName, out StaticClassInfo info)
        {
            if (_classes.TryGetValue(classNameOrFullName, out info)) return true;

            info = _classes.Values.FirstOrDefault(item => item.ClassName == classNameOrFullName);
            return info != null;
        }

        public bool TryGetField(string classNameOrFullName, string fieldName, out StaticFieldInfo field)
        {
            field = null;

            return TryGetClass(classNameOrFullName, out var info) && info.TryGetField(fieldName, out field);
        }

        private static List<StaticFieldInfo> AssignSlots(IReadOnlyList<PendingStaticField> pendingFields)
        {
            var counters = new Dictionary<string, int>();
            var fields = new List<StaticFieldInfo>();

            foreach (var pending in pendingFields.OrderBy(static field => field.QualifiedName, StringComparer.Ordinal))
            {
                var storageName = pending.Storage.ArrayName;
                counters.TryGetValue(storageName, out int slot);
                counters[storageName] = slot + 1;

                fields.Add(new StaticFieldInfo(
                    pending.ClassName,
                    pending.FullClassName,
                    pending.FieldName,
                    pending.QualifiedName,
                    pending.TypeName,
                    pending.Storage,
                    slot,
                    pending.Initializer));
            }

            return fields;
        }

        private static bool IsDirectUdonSharpBehaviour(ClassDeclarationSyntax declaration)
        {
            return declaration.BaseList != null &&
                   declaration.BaseList.Types.Any(static type =>
                   {
                       var name = type.Type.ToString();
                       return name == "UdonSharpBehaviour" || name.EndsWith(".UdonSharpBehaviour", StringComparison.Ordinal);
                   });
        }

        private static bool IsSupportedStaticField(FieldDeclarationSyntax field)
        {
            return field.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                   !field.Modifiers.Any(SyntaxKind.ConstKeyword);
        }

        private static string GetFullClassName(ClassDeclarationSyntax declaration)
        {
            var names = new Stack<string>();
            SyntaxNode current = declaration;

            while (current != null)
            {
                if (current is ClassDeclarationSyntax classDeclaration)
                    names.Push(classDeclaration.Identifier.ValueText);
                else if (current is NamespaceDeclarationSyntax namespaceDeclaration)
                    names.Push(namespaceDeclaration.Name.ToString());

                current = current.Parent;
            }

            return string.Join(".", names);
        }
    }

    internal sealed class StaticClassInfo
    {
        private readonly Dictionary<string, StaticFieldInfo> _fields;

        public StaticClassInfo(string className, string fullClassName, IReadOnlyList<StaticFieldInfo> fields)
        {
            ClassName = className;
            FullClassName = fullClassName;
            _fields = fields.ToDictionary(field => field.Name, field => field);
        }

        public string ClassName { get; }
        public string FullClassName { get; }

        public bool TryGetField(string name, out StaticFieldInfo field)
        {
            return _fields.TryGetValue(name, out field);
        }
    }

    internal sealed class StaticFieldInfo
    {
        public StaticFieldInfo(
            string className,
            string fullClassName,
            string name,
            string qualifiedName,
            string typeName,
            StorageKind storage,
            int slot,
            ExpressionSyntax initializer)
        {
            ClassName = className;
            FullClassName = fullClassName;
            Name = name;
            QualifiedName = qualifiedName;
            TypeName = typeName;
            Storage = storage;
            Slot = slot;
            Initializer = initializer;
        }

        public string ClassName { get; }
        public string FullClassName { get; }
        public string Name { get; }
        public string QualifiedName { get; }
        public string TypeName { get; }
        public StorageKind Storage { get; }
        public int Slot { get; }
        public ExpressionSyntax Initializer { get; }
    }

    internal readonly struct StorageKind
    {
        private StorageKind(string arrayName, string defaultExpression, bool needsCast)
        {
            ArrayName = arrayName;
            DefaultExpression = defaultExpression;
            NeedsCast = needsCast;
        }

        public string ArrayName { get; }
        public string DefaultExpression { get; }
        public bool NeedsCast { get; }

        public static StorageKind FromType(string typeName)
        {
            return Normalize(typeName) switch
            {
                "int" => new StorageKind("IntData", "0", false),
                "float" => new StorageKind("FloatData", "0f", false),
                "bool" => new StorageKind("BoolData", "false", false),
                "string" => new StorageKind("StringData", "null", false),
                "long" => new StorageKind("LongData", "0L", false),
                "double" => new StorageKind("DoubleData", "0d", false),
                "UnityEngine.Vector2" => new StorageKind("Vector2Data", "UnityEngine.Vector2.zero", false),
                "UnityEngine.Vector3" => new StorageKind("Vector3Data", "UnityEngine.Vector3.zero", false),
                "UnityEngine.Quaternion" => new StorageKind("QuaternionData", "UnityEngine.Quaternion.identity", false),
                "UnityEngine.Color" => new StorageKind("ColorData", "UnityEngine.Color.clear", false),
                "UnityEngine.GameObject" => new StorageKind("GameObjectData", "null", false),
                "UnityEngine.Transform" => new StorageKind("TransformData", "null", false),
                _ => new StorageKind("ObjectData", "null", true)
            };
        }

        private static string Normalize(string typeName)
        {
            return typeName switch
            {
                "System.Int32" => "int",
                "System.Single" => "float",
                "System.Boolean" => "bool",
                "System.String" => "string",
                "System.Int64" => "long",
                "System.Double" => "double",
                "Vector2" => "UnityEngine.Vector2",
                "Vector3" => "UnityEngine.Vector3",
                "Quaternion" => "UnityEngine.Quaternion",
                "Color" => "UnityEngine.Color",
                "GameObject" => "UnityEngine.GameObject",
                "Transform" => "UnityEngine.Transform",
                _ => typeName
            };
        }
    }

    internal sealed class PendingStaticField
    {
        public PendingStaticField(
            string className,
            string fullClassName,
            string fieldName,
            string typeName,
            StorageKind storage,
            string filePath,
            ExpressionSyntax initializer)
        {
            ClassName = className;
            FullClassName = fullClassName;
            FieldName = fieldName;
            TypeName = typeName;
            Storage = storage;
            FilePath = filePath;
            Initializer = initializer;
        }

        public string ClassName { get; }
        public string FullClassName { get; }
        public string FieldName { get; }
        public string TypeName { get; }
        public StorageKind Storage { get; }
        public string FilePath { get; }
        public ExpressionSyntax Initializer { get; }
        public string QualifiedName => FullClassName + "." + FieldName;
    }

    internal sealed class StaticFieldSyntaxRewriter : CSharpSyntaxRewriter
    {
        private readonly StaticFieldCatalog _catalog;
        private readonly Stack<HashSet<string>> _localScopes = new Stack<HashSet<string>>();
        private StaticClassInfo _currentClass;
        private bool _classNeedsStore;

        public StaticFieldSyntaxRewriter(StaticFieldCatalog catalog)
        {
            _catalog = catalog;
        }

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var previousClass = _currentClass;
            var previousNeedsStore = _classNeedsStore;

            _catalog.TryGetClass(GetFullClassName(node), out _currentClass);
            _classNeedsStore = false;

            var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node);

            if (_classNeedsStore)
            {
                visited = visited.WithMembers(visited.Members.InsertRange(0, BuildStoreMembers()));
            }

            _currentClass = previousClass;
            _classNeedsStore = previousNeedsStore;
            return visited;
        }

        public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            if (_currentClass != null && node.Modifiers.Any(SyntaxKind.StaticKeyword))
                return null;

            return base.VisitFieldDeclaration(node);
        }

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (_currentClass == null)
                return base.VisitMethodDeclaration(node);

            var scope = new HashSet<string>();
            foreach (var parameter in node.ParameterList.Parameters)
            {
                scope.Add(parameter.Identifier.ValueText);
            }

            _localScopes.Push(scope);
            var visited = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node);
            _localScopes.Pop();

            return visited;
        }

        public override SyntaxNode VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            if (_currentClass == null || _localScopes.Count == 0)
                return base.VisitLocalDeclarationStatement(node);

            var visited = (LocalDeclarationStatementSyntax)base.VisitLocalDeclarationStatement(node);
            var scope = _localScopes.Peek();
            foreach (var variable in node.Declaration.Variables)
            {
                scope.Add(variable.Identifier.ValueText);
            }

            return visited;
        }

        public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (
                node.Expression is not IdentifierNameSyntax typeName ||
                !_catalog.TryGetField(typeName.Identifier.ValueText, node.Name.Identifier.ValueText, out var field)
            )
            {
                return base.VisitMemberAccessExpression(node);
            }

            _classNeedsStore = true;
            return BuildStorageAccess(field, false).WithTriviaFrom(node);
        }

        public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (
                _currentClass == null ||
                IsLocallyDeclared(node.Identifier.ValueText) ||
                !_currentClass.TryGetField(node.Identifier.ValueText, out StaticFieldInfo field)
            )
            {
                return base.VisitIdentifierName(node);
            }

            _classNeedsStore = true;
            return BuildStorageAccess(field, false).WithTriviaFrom(node);
        }

        private bool IsLocallyDeclared(string name) => _localScopes.Any(scope => scope.Contains(name));

        private ExpressionSyntax BuildStorageAccess(StaticFieldInfo field, bool writable)
        {
            ExpressionSyntax elementAccess = SyntaxFactory.ElementAccessExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName(UdonStaticLowerer.StoreGetterNameForGeneratedCode)),
                    SyntaxFactory.IdentifierName(field.Storage.ArrayName)),
                SyntaxFactory.BracketedArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(
                            SyntaxKind.NumericLiteralExpression,
                            SyntaxFactory.Literal(field.Slot))))));

            if (writable || !field.Storage.NeedsCast)
                return elementAccess;

            return SyntaxFactory.CastExpression(SyntaxFactory.ParseTypeName(field.TypeName), elementAccess)
                .Parenthesize();
        }

        private static SyntaxList<MemberDeclarationSyntax> BuildStoreMembers()
        {
            var field = (FieldDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(
                $"private {UdonStaticLowerer.StoreTypeName} {UdonStaticLowerer.StoreFieldNameForGeneratedCode};");

            var getter =
                (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(
                    $@"private {UdonStaticLowerer.StoreTypeName} {UdonStaticLowerer.StoreGetterNameForGeneratedCode}()
                        {{
                            if ({UdonStaticLowerer.StoreFieldNameForGeneratedCode} == null)
                                {UdonStaticLowerer.StoreFieldNameForGeneratedCode} = UnityEngine.GameObject.Find(""{UdonStaticLowerer.StoreObjectName}"").GetComponent<{UdonStaticLowerer.StoreTypeName}>();

                            return {UdonStaticLowerer.StoreFieldNameForGeneratedCode};
                        }}"
                );

            return SyntaxFactory.List(new MemberDeclarationSyntax[] { field, getter });
        }

        private static string GetFullClassName(ClassDeclarationSyntax declaration)
        {
            var names = new Stack<string>();
            SyntaxNode current = declaration;

            while (current != null)
            {
                switch (current)
                {
                    case ClassDeclarationSyntax classDeclaration:
                        names.Push(classDeclaration.Identifier.ValueText);
                        break;
                    case NamespaceDeclarationSyntax namespaceDeclaration:
                        names.Push(namespaceDeclaration.Name.ToString());
                        break;
                }

                current = current.Parent;
            }

            return string.Join(".", names);
        }
    }

    internal static class SyntaxExtensions
    {
        public static ExpressionSyntax Parenthesize(this ExpressionSyntax expression)
        {
            return expression is ParenthesizedExpressionSyntax
                ? expression
                : SyntaxFactory.ParenthesizedExpression(expression);
        }
    }
}
