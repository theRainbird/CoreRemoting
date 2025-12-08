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