# CoreRemoting Development Guidelines

## Build & Test Commands
- Build: `dotnet build CoreRemoting.sln`
- Run all tests: `dotnet test CoreRemoting.Tests/CoreRemoting.Tests.csproj`
- Run single test: `dotnet test --filter "TestMethodName"` or `dotnet test --filter "ClassName"`
- Run specific test file: `dotnet test --filter "FullyQualifiedName~RpcTests"`

## Code Style Guidelines
- Target frameworks: .NET Standard 2.0 and .NET 8.0 (except Quic channel project, which runs only on .NET 9.0)
- Use xUnit for testing with ITestOutputHelper for output
- Follow C# naming conventions (PascalCase for public members, camelCase for private)
- Use async/await patterns for async operations
- Implement IDisposable/IAsyncDisposable for cleanup
- Use dependency injection (Microsoft DI or Castle Windsor)
- XML documentation for public APIs
- Use InternalsVisibleTo for test access
- Include null reference annotations ([NotNull], [MaybeNull])
- Use regions to organize code sections
- Follow existing exception handling patterns with custom exception types

## QUIC CHANNEL SPECIAL INSTRUCTIONS
⚠️ **IMPORTANT**: The Quic channel project requires .NET 9.0 due to library dependencies.
- When working on the Quic channel, temporarily change the target framework to .NET 9.0
- For all other development, keep it at .NET 8.0 for compatibility
- The AI assistant should be aware of this requirement and not automatically "fix" the .NET 9.0 target
- Only change the target framework if specifically requested or if there are build issues