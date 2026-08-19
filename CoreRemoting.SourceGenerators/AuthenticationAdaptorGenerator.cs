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
/// Generates a new Authenticate method for classes that implement the legacy
/// IAuthenticationProvider interface (with out parameter) but lack the new
/// Task-based method.
/// </summary>
[Generator]
public class AuthenticationAdapterGenerator : IIncrementalGenerator
{
    private const string InterfaceName = "CoreRemoting.Authentication.IAuthenticationProvider";
    private const string MethodName = "Authenticate";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsClassOrStruct(s),
                transform: static (ctx, _) => GetTypeInfo(ctx))
            .Where(static info => info is not null)
            .Collect();

        context.RegisterSourceOutput(classDeclarations, Execute);
    }

    private static bool IsClassOrStruct(SyntaxNode node) =>
        node is ClassDeclarationSyntax or StructDeclarationSyntax;

    private static TypeInfo GetTypeInfo(GeneratorSyntaxContext context)
    {
        var declaration = (TypeDeclarationSyntax)context.Node;
        var model = context.SemanticModel;
        var typeSymbol = model.GetDeclaredSymbol(declaration);
        if (typeSymbol is null) return null;

        if (typeSymbol.TypeKind != TypeKind.Class) return null;

        // Get the interface symbol from the compilation
        var compilation = model.Compilation;
        var interfaceSymbol = compilation.GetTypeByMetadataName(InterfaceName);
        if (interfaceSymbol is null) return null;

        // Check if the class implements the interface
        bool implementsInterface = typeSymbol.AllInterfaces
            .Any(i => SymbolEqualityComparer.Default.Equals(i, interfaceSymbol));
        if (!implementsInterface) return null;

        // Find legacy method: bool Authenticate(..., out ...)
        // We only check the return type, parameter count, and that the last parameter is 'out'
        var legacyMethod = typeSymbol.GetMembers(MethodName).OfType<IMethodSymbol>()
            .FirstOrDefault(m =>
                m.ReturnsVoid == false &&
                m.ReturnType.SpecialType == SpecialType.System_Boolean &&
                m.Parameters.Length == 2 &&
                m.Parameters[1].RefKind == RefKind.Out);

        if (legacyMethod is null) return null;

        // Check if the class already has the new method
        bool hasNewMethod = typeSymbol.GetMembers(MethodName).OfType<IMethodSymbol>()
            .Any(m =>
                m.ReturnsVoid == false &&
                m.ReturnType is INamedTypeSymbol ret &&
                ret.Name == "Task" &&
                ret.TypeArguments.Length == 1 &&
                ret.TypeArguments[0].Name == "AuthenticationResponseMessage" &&
                m.Parameters.Length == 1 &&
                m.Parameters[0].Type.Name == "AuthenticationRequestMessage");

        if (hasNewMethod) return null;

        // Require the class to be partial
        if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            return null;

        return new TypeInfo(typeSymbol, declaration);
    }

    private static void Execute(SourceProductionContext context, ImmutableArray<TypeInfo> typeInfos)
    {
        // Diagnostics to see how many classes were found
        context.ReportDiagnostic(Diagnostic.Create(
            new DiagnosticDescriptor("AD001", "Debug", $"Found {typeInfos.Length} classes requiring adapter", "Debug", DiagnosticSeverity.Info, true),
            Location.None));

        foreach (var info in typeInfos)
        {
            string source = GenerateSource(info.TypeSymbol);
            string rawName = info.TypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ".AuthenticationAdapter.g.cs";
            string fileName = GeneratorHelpers.SanitizeFileName(rawName);
            context.AddSource(fileName, source);
        }
    }

    private static string GenerateSource(INamedTypeSymbol typeSymbol)
    {
        var sb = new StringBuilder();

        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using CoreRemoting.Authentication;");
        sb.AppendLine();

        var ns = typeSymbol.ContainingNamespace.ToDisplayString();
        if (!string.IsNullOrEmpty(ns))
        {
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
        }

        // Nested types
        var containingTypes = new List<INamedTypeSymbol>();
        var current = typeSymbol.ContainingType;
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

        sb.AppendLine($"partial class {typeSymbol.Name}");
        sb.AppendLine("{");
        sb.AppendLine($"    [global::System.CodeDom.Compiler.GeneratedCode(\"{nameof(AuthenticationAdapterGenerator)}\", \"1.0.0.0\")]");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Adapter method that calls the legacy Authenticate method.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    /// <param name=\"request\">Authentication request.</param>");
        sb.AppendLine("    /// <returns>Authentication response.</returns>");
        sb.AppendLine($"    public Task<AuthenticationResponseMessage> {MethodName}(AuthenticationRequestMessage request)");
        sb.AppendLine("    {");
        sb.AppendLine("        var isAuthenticated = Authenticate(request.Credentials, out var identity);");
        sb.AppendLine("        return Task.FromResult(new AuthenticationResponseMessage");
        sb.AppendLine("        {");
        sb.AppendLine("            IsAuthenticated = isAuthenticated,");
        sb.AppendLine("            AuthenticatedIdentity = identity");
        sb.AppendLine("        });");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        // Close outer types
        foreach (var _ in containingTypes)
        {
            sb.AppendLine("}");
        }

        if (!string.IsNullOrEmpty(ns))
            sb.AppendLine("}");

        return sb.ToString();
    }

    private record TypeInfo(INamedTypeSymbol TypeSymbol, TypeDeclarationSyntax Declaration);
}
