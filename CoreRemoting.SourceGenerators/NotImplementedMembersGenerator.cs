using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CoreRemoting.SourceGenerators;

/// <summary>
/// Implements missing interface members for classes
/// marked with NotImplementedAttribute.
/// </summary>
[Generator]
public class NotImplementedMembersGenerator : IIncrementalGenerator
{
    private static readonly SymbolDisplayFormat InterfaceNameFormat =
        new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classInfos = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsClassWithAttribute(s),
                transform: static (ctx, _) => GetClassInfo(ctx))
            .Where(static info => info is not null)
            .Collect();

        context.RegisterSourceOutput(classInfos, Execute);
    }

    private static bool IsClassWithAttribute(SyntaxNode node) =>
        node is ClassDeclarationSyntax { AttributeLists.Count: > 0 };

    private static ClassInfo GetClassInfo(GeneratorSyntaxContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var model = context.SemanticModel;
        var classSymbol = model.GetDeclaredSymbol(classDecl);
        if (classSymbol is null) return null;

        bool hasAttr = classSymbol.GetAttributes().Any(attr =>
            attr.AttributeClass?.Name is "NotImplementedAttribute" or "NotImplemented");

        if (!hasAttr) return null;

        var allInterfaces = classSymbol.AllInterfaces;
        if (allInterfaces.IsEmpty) return null;

        var missingMethods = new List<IMethodSymbol>();
        var missingProperties = new List<IPropertySymbol>();
        var missingEvents = new List<IEventSymbol>();

        foreach (var interfaceSymbol in allInterfaces)
        {
            foreach (var member in interfaceSymbol.GetMembers())
            {
                if (member.IsImplicitlyDeclared) continue;

                var impl = classSymbol.FindImplementationForInterfaceMember(member);
                if (impl == null)
                {
                    switch (member)
                    {
                        case IMethodSymbol m when m.MethodKind == MethodKind.Ordinary:
                            missingMethods.Add(m);
                            break;
                        case IPropertySymbol p:
                            missingProperties.Add(p);
                            break;
                        case IEventSymbol e:
                            missingEvents.Add(e);
                            break;
                    }
                }
            }
        }

        if (missingMethods.Count == 0 && missingProperties.Count == 0 && missingEvents.Count == 0)
            return null;

        return new ClassInfo(classSymbol, classDecl, missingMethods, missingProperties, missingEvents);
    }

    private static void Execute(SourceProductionContext context, ImmutableArray<ClassInfo> classInfos)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            new DiagnosticDescriptor("NG001", "Debug", $"Found {classInfos.Length} classes with [NotImplemented] and missing members", "Debug", DiagnosticSeverity.Info, true),
            Location.None));

        if (classInfos.Length == 0)
        {
            context.AddSource("NoClassesFound.g.cs", "// No classes with [NotImplemented] and missing members found.");
            return;
        }

        foreach (var info in classInfos)
        {
            string source = GenerateSource(info);
            string rawName = info.ClassSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ".NotImplemented.g.cs";
            string fileName = GeneratorHelpers.SanitizeFileName(rawName);
            context.AddSource(fileName, source);
        }
    }

    private static string GenerateSource(ClassInfo info)
    {
        var sb = new StringBuilder();

        sb.AppendLine("using System;");
        sb.AppendLine("using CoreRemoting.Toolbox;");
        sb.AppendLine();

        var ns = info.ClassSymbol.ContainingNamespace.ToDisplayString();
        if (!string.IsNullOrEmpty(ns))
        {
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
        }

        var containingTypes = new List<INamedTypeSymbol>();
        var current = info.ClassSymbol.ContainingType;
        while (current != null)
        {
            containingTypes.Insert(0, current);
            current = current.ContainingType;
        }

        foreach (var outer in containingTypes)
        {
            sb.AppendLine($"partial class {outer.Name}");
            sb.AppendLine("{");
        }

        sb.AppendLine($"partial class {info.ClassSymbol.Name}");
        sb.AppendLine("{");

        foreach (var method in info.MissingMethods)
        {
            GenerateMethod(sb, method);
            sb.AppendLine();
        }

        foreach (var prop in info.MissingProperties)
        {
            GenerateProperty(sb, prop);
            sb.AppendLine();
        }

        foreach (var ev in info.MissingEvents)
        {
            GenerateEvent(sb, ev);
            sb.AppendLine();
        }

        sb.AppendLine("}"); // close target class

        foreach (var _ in containingTypes)
        {
            sb.AppendLine("}");
        }

        if (!string.IsNullOrEmpty(ns))
            sb.AppendLine("}");

        return sb.ToString();
    }

    private static void GenerateMethod(StringBuilder sb, IMethodSymbol method)
    {
        string interfaceName = method.ContainingType.ToDisplayString(InterfaceNameFormat);
        string methodName = method.Name;
        string typeParams = method.TypeParameters.Any() ? $"<{string.Join(", ", method.TypeParameters.Select(t => t.Name))}>" : "";
        string returnType = method.ReturnsVoid ? "void" : method.ReturnType.ToDisplayString();

        var paramStrings = method.Parameters.Select(p =>
        {
            string mod = p.RefKind switch
            {
                RefKind.Ref => "ref ",
                RefKind.Out => "out ",
                RefKind.In => "in ",
                _ => ""
            };
            string type = p.Type.ToDisplayString();
            string name = p.Name;
            return $"{mod}{type} {name}";
        });

        string paramList = string.Join(", ", paramStrings);

        sb.AppendLine("    [NotImplemented]");
        sb.AppendLine($"    {returnType} {interfaceName}.{methodName}{typeParams}({paramList})");
        sb.AppendLine("    {");
        sb.AppendLine("        throw new NotImplementedException();");
        sb.AppendLine("    }");
    }

    private static void GenerateProperty(StringBuilder sb, IPropertySymbol prop)
    {
        string interfaceName = prop.ContainingType.ToDisplayString(InterfaceNameFormat);
        string propName = prop.Name;
        bool isIndexer = prop.IsIndexer;
        string accessor = isIndexer ? "this" : propName;

        string parameters = "";
        if (isIndexer)
        {
            var paramStrings = prop.Parameters.Select(p =>
            {
                string mod = p.RefKind switch
                {
                    RefKind.Ref => "ref ",
                    RefKind.Out => "out ",
                    RefKind.In => "in ",
                    _ => ""
                };
                return $"{mod}{p.Type.ToDisplayString()} {p.Name}";
            });
            parameters = $"[{string.Join(", ", paramStrings)}]";
        }

        sb.AppendLine("    [NotImplemented]");
        sb.AppendLine($"    {prop.Type.ToDisplayString()} {interfaceName}.{accessor}{parameters}");
        sb.AppendLine("    {");
        if (prop.GetMethod != null)
            sb.AppendLine($"        get {{ throw new NotImplementedException(); }}");
        if (prop.SetMethod != null)
            sb.AppendLine($"        set {{ throw new NotImplementedException(); }}");
        sb.AppendLine("    }");
    }

    private static void GenerateEvent(StringBuilder sb, IEventSymbol ev)
    {
        string interfaceName = ev.ContainingType.ToDisplayString(InterfaceNameFormat);
        string eventName = ev.Name;

        sb.AppendLine("    [NotImplemented]");
        sb.AppendLine($"    event {ev.Type.ToDisplayString()} {interfaceName}.{eventName}");
        sb.AppendLine("    {");
        sb.AppendLine($"        add {{ throw new NotImplementedException(); }}");
        sb.AppendLine($"        remove {{ throw new NotImplementedException(); }}");
        sb.AppendLine("    }");
    }

    private record ClassInfo(
        INamedTypeSymbol ClassSymbol,
        ClassDeclarationSyntax ClassDeclaration,
        List<IMethodSymbol> MissingMethods,
        List<IPropertySymbol> MissingProperties,
        List<IEventSymbol> MissingEvents);
}
