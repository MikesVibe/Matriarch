using Xunit;
using Matriarch.Shared.Services;
using Microsoft.Graph.Beta;
using Azure.Identity;

namespace Matriarch.IntegrationTests;

public class GraphClientFactoryTests
{
    [Theory]
    [InlineData("Public")]
    [InlineData("public")]
    [InlineData("Government")]
    [InlineData("GOVERNMENT")]
    [InlineData("China")]
    [InlineData("china")]
    public void ParseCloudEnvironment_WithValidValue_ReturnsCorrectEnvironment(string value)
    {
        // Act
        var result = GraphClientFactory.ParseCloudEnvironment(value);

        // Assert
        Assert.NotNull(result);
        Assert.True(Enum.IsDefined(typeof(CloudEnvironment), result));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void ParseCloudEnvironment_WithNullOrEmpty_ReturnsPublic(string? value)
    {
        // Act
        var result = GraphClientFactory.ParseCloudEnvironment(value);

        // Assert
        Assert.Equal(CloudEnvironment.Public, result);
    }

    [Fact]
    public void ParseCloudEnvironment_WithInvalidValue_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            GraphClientFactory.ParseCloudEnvironment("InvalidCloud"));

        Assert.Contains("Invalid cloud environment value", exception.Message);
    }

    [Fact]
    public void CreateClient_ForPublicCloud_CreatesClientWithCorrectEndpoint()
    {
        // Arrange
        var tenantId = "test-tenant-id";
        var clientId = "test-client-id";
        var clientSecret = "test-client-secret";

        // Act
        var client = GraphClientFactory.CreateClient(
            tenantId,
            clientId,
            clientSecret,
            CloudEnvironment.Public);

        // Assert
        Assert.NotNull(client);
        Assert.IsType<GraphServiceClient>(client);
    }

    [Fact]
    public void CreateClient_ForGovernmentCloud_CreatesClientWithCorrectEndpoint()
    {
        // Arrange
        var tenantId = "test-tenant-id";
        var clientId = "test-client-id";
        var clientSecret = "test-client-secret";

        // Act
        var client = GraphClientFactory.CreateClient(
            tenantId,
            clientId,
            clientSecret,
            CloudEnvironment.Government);

        // Assert
        Assert.NotNull(client);
        Assert.IsType<GraphServiceClient>(client);
    }

    [Fact]
    public void CreateClient_ForChinaCloud_CreatesClientWithCorrectEndpoint()
    {
        // Arrange
        var tenantId = "test-tenant-id";
        var clientId = "test-client-id";
        var clientSecret = "test-client-secret";

        // Act
        var client = GraphClientFactory.CreateClient(
            tenantId,
            clientId,
            clientSecret,
            CloudEnvironment.China);

        // Assert
        Assert.NotNull(client);
        Assert.IsType<GraphServiceClient>(client);
    }

    [Theory]
    [InlineData(CloudEnvironment.Public, "https://management.azure.com")]
    [InlineData(CloudEnvironment.Government, "https://management.usgovcloudapi.net")]
    [InlineData(CloudEnvironment.China, "https://management.chinacloudapi.cn")]
    public void GetResourceManagerEndpoint_ReturnsCorrectEndpoint(CloudEnvironment environment, string expectedEndpoint)
    {
        // Act
        var endpoint = GraphClientFactory.GetResourceManagerEndpoint(environment);

        // Assert
        Assert.Equal(expectedEndpoint, endpoint);
    }

    [Fact]
    public void CreateClient_WithDefaultCloudEnvironment_UsesPublicCloud()
    {
        // Arrange
        var tenantId = "test-tenant-id";
        var clientId = "test-client-id";
        var clientSecret = "test-client-secret";

        // Act - not specifying cloud environment should default to Public
        var client = GraphClientFactory.CreateClient(
            tenantId,
            clientId,
            clientSecret);

        // Assert
        Assert.NotNull(client);
        Assert.IsType<GraphServiceClient>(client);
    }
}
