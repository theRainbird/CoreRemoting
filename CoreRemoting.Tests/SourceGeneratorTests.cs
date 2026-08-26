using System;
using System.CodeDom.Compiler;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Castle.Core.Internal;
using CoreRemoting.Authentication;
using CoreRemoting.Tests.Tools;
using CoreRemoting.Toolbox;
using Xunit;

namespace CoreRemoting.Tests;

public partial class SourceGeneratorTests
{
    partial class LegacyAuthenticationProvider : IAuthenticationProvider
    {
        public bool Authenticate(Credential[] credentials, out RemotingIdentity id)
        {
            id = credentials.FindByName("login") != "root" ? null : new()
            {
                Name = "Administrator"
            };

            return id != null;
        }
    }

    [Fact]
    public async Task Legacy_AuthenticationProvider_is_automatically_upgraded()
    {
        var type = typeof(LegacyAuthenticationProvider);

        // check that the new method exists with the correct signature
        var newMethod = type.GetMethod(
            nameof(IAuthenticationProvider.Authenticate),
            [typeof(AuthenticationRequestMessage)]);
        Assert.NotNull(newMethod);
        Assert.Equal(typeof(Task<AuthenticationResponseMessage>), newMethod.ReturnType);

        // check that it has the GeneratedCode attribute
        var generatedCodeAttr = newMethod.GetCustomAttribute<GeneratedCodeAttribute>();
        Assert.NotNull(generatedCodeAttr);

        // test the adapter logic
        var provider = new LegacyAuthenticationProvider();

        // request with valid credentials
        var validRequest = new AuthenticationRequestMessage
        {
            Credentials =
            [
                new() { Name = "login", Value = "root" }
            ]
        };

        var response = await provider.Authenticate(validRequest);
        Assert.True(response.IsAuthenticated);
        Assert.NotNull(response.AuthenticatedIdentity);
        Assert.Equal("Administrator", response.AuthenticatedIdentity.Name);

        // request with invalid credentials
        response = await provider.Authenticate(new());
        Assert.False(response.IsAuthenticated);
        Assert.Null(response.AuthenticatedIdentity);
    }

    [NotImplemented]
    partial class PartiallyImplementedService : ITestService
    {
        public string Echo(string text) => text;

        public string Reverse(string text) => new([.. text.Reverse()]);
    }

    [Fact]
    public void PartiallyImplementedService_HasAllInterfaceMembers()
    {
        var type = typeof(PartiallyImplementedService);

        // this method exists in the code
        var existingMethod = type.GetMethod(nameof(ITestService.Echo));
        Assert.NotNull(existingMethod);
        Assert.Null(existingMethod.GetAttribute<NotImplementedAttribute>());

        // this method doesn't exist in the code
        var interfaceMap = type.GetInterfaceMap(typeof(ITestService));
        var interfaceMethod = typeof(ITestService).GetMethod(nameof(ITestService.TestMethodWithDelegateArg));
        int index = Array.IndexOf(interfaceMap.InterfaceMethods, interfaceMethod);
        var generatedMethod = interfaceMap.TargetMethods[index];
        Assert.NotNull(generatedMethod);
        Assert.Contains(nameof(ITestService.TestMethodWithDelegateArg), generatedMethod.Name);
        Assert.NotNull(generatedMethod.GetCustomAttribute<NotImplementedAttribute>());

        // this event is also auto-generated
        var interfaceEvent = typeof(ITestService).GetEvent(nameof(ITestService.ServiceEvent));
        Assert.NotNull(interfaceEvent);

        // Get the add and remove methods of the event
        var addMethod = interfaceEvent.GetAddMethod();
        var removeMethod = interfaceEvent.GetRemoveMethod();
        Assert.NotNull(addMethod);
        Assert.NotNull(removeMethod);

        // Find the corresponding target methods via interface map
        var addIndex = Array.IndexOf(interfaceMap.InterfaceMethods, addMethod);
        var removeIndex = Array.IndexOf(interfaceMap.InterfaceMethods, removeMethod);
        var generatedAddMethod = interfaceMap.TargetMethods[addIndex];
        var generatedRemoveMethod = interfaceMap.TargetMethods[removeIndex];

        Assert.NotNull(generatedAddMethod);
        Assert.NotNull(generatedRemoveMethod);
        Assert.Contains("add_", generatedAddMethod.Name);
        Assert.Contains("remove_", generatedRemoveMethod.Name);

        // add and remove methods don't have the [NotImplemented] attribute
        Assert.Null(generatedAddMethod.GetCustomAttribute<NotImplementedAttribute>());
        Assert.Null(generatedRemoveMethod.GetCustomAttribute<NotImplementedAttribute>());

        // find the generated event by filtering all non-public events
        var allEvents = type.GetEvents(BindingFlags.NonPublic | BindingFlags.Instance);
        var generatedEvent = allEvents.FirstOrDefault(e =>
            e.Name.EndsWith("." + interfaceEvent.Name, StringComparison.Ordinal));
        Assert.NotNull(generatedEvent);
        Assert.NotNull(generatedEvent.GetCustomAttribute<NotImplementedAttribute>());

        // auto-generated property from base interface (explicit implementation)
        var baseInterface = typeof(IBaseService);
        var versionProperty = baseInterface.GetProperty(nameof(IBaseService.Version));
        Assert.NotNull(versionProperty);

        // Find the generated property by filtering non-public properties
        var allProperties = type.GetProperties(BindingFlags.NonPublic | BindingFlags.Instance);
        var generatedProperty = allProperties.FirstOrDefault(p =>
            p.Name.EndsWith("." + versionProperty.Name, StringComparison.Ordinal));
        Assert.NotNull(generatedProperty);
        Assert.NotNull(generatedProperty.GetCustomAttribute<NotImplementedAttribute>());
    }
}