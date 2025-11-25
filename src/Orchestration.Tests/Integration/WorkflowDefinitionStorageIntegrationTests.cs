using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Orchestration.Core.Workflow;
using Orchestration.Core.Workflow.StateTypes;
using Orchestration.Infrastructure.Storage;

namespace Orchestration.Tests.Integration;

/// <summary>
/// Integration tests for WorkflowDefinitionStorage using Azurite emulator.
/// These tests require Azurite to be running locally or in CI.
/// </summary>
[Trait("Category", "Integration")]
public class WorkflowDefinitionStorageIntegrationTests : IAsyncLifetime
{
    private readonly WorkflowDefinitionStorage _storage;
    private readonly IConfiguration _configuration;
    private readonly string _testContainerName;

    public WorkflowDefinitionStorageIntegrationTests()
    {
        _testContainerName = $"test-workflows-{Guid.NewGuid():N}";

        // Use Azurite connection string
        var azuriteConnection = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
            ?? "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";

        var configData = new Dictionary<string, string?>
        {
            ["WorkflowStorageConnection"] = azuriteConnection,
            ["WorkflowStorageContainer"] = _testContainerName
        };

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var logger = Mock.Of<ILogger<WorkflowDefinitionStorage>>();
        _storage = new WorkflowDefinitionStorage(_configuration, logger);
    }

    public Task InitializeAsync()
    {
        // Storage initialization happens in constructor
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        // Cleanup: Delete test container
        try
        {
            var connectionString = _configuration["WorkflowStorageConnection"];
            if (!string.IsNullOrEmpty(connectionString))
            {
                var blobServiceClient = new Azure.Storage.Blobs.BlobServiceClient(connectionString);
                var container = blobServiceClient.GetBlobContainerClient(_testContainerName);
                await container.DeleteIfExistsAsync();
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetAsync_WithBuiltInDefinition_ReturnsDeviceOnboarding()
    {
        // Act
        var definition = await _storage.GetAsync("DeviceOnboarding");

        // Assert
        definition.Should().NotBeNull();
        definition.Id.Should().Be("DeviceOnboarding");
        definition.Name.Should().Be("Device Onboarding Workflow");
        definition.States.Should().NotBeEmpty();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListWorkflowTypesAsync_ReturnsBuiltInDefinitions()
    {
        // Act
        var types = await _storage.ListWorkflowTypesAsync();

        // Assert
        types.Should().NotBeEmpty();
        types.Should().Contain("DeviceOnboarding");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SaveAsync_AndGetAsync_RoundTripsDefinition()
    {
        // Arrange
        var testDefinition = CreateTestDefinition();

        // Act
        await _storage.SaveAsync(testDefinition);
        var retrieved = await _storage.GetAsync(testDefinition.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved.Id.Should().Be(testDefinition.Id);
        retrieved.Name.Should().Be(testDefinition.Name);
        retrieved.Version.Should().Be(testDefinition.Version);
        retrieved.States.Should().HaveCount(testDefinition.States.Count);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListVersionsAsync_ReturnsVersions()
    {
        // Act
        var versions = await _storage.ListVersionsAsync("DeviceOnboarding");

        // Assert
        versions.Should().NotBeEmpty();
        versions.Should().Contain("1.0.0");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetAsync_WithNonExistentDefinition_ThrowsException()
    {
        // Act
        var act = () => _storage.GetAsync("NonExistent");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    private static WorkflowDefinition CreateTestDefinition()
    {
        return new WorkflowDefinition
        {
            Id = $"TestWorkflow-{Guid.NewGuid():N}",
            Name = "Test Workflow",
            Version = "1.0.0",
            Description = "A test workflow for integration testing",
            StartAt = "Start",
            States = new Dictionary<string, WorkflowStateDefinition>
            {
                ["Start"] = new TaskStateDefinition
                {
                    Activity = "TestActivity",
                    Next = "End"
                },
                ["End"] = new SucceedStateDefinition()
            },
            Config = new WorkflowConfiguration
            {
                TimeoutSeconds = 300
            }
        };
    }
}
