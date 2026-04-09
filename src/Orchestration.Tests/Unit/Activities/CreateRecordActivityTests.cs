using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Orchestration.Core.Capabilities;
using Orchestration.Core.Contracts;
using Orchestration.Functions.Activities.Database;

namespace Orchestration.Tests.Unit.Activities;

public class CreateRecordActivityTests
{
    [Fact]
    public async Task Run_WithNewRecord_CreatesThroughCapabilityTable_AndPreservesIdempotency()
    {
        var repositoryMock = new Mock<IWorkflowRepository>();
        var tableMock = new Mock<IReadWriteRecordTable>();
        var activity = new CreateRecordActivity(
            repositoryMock.Object,
            TestActivityCapabilityScopeFactoryFactory.CreateScopeFactory("records", CapabilityAccess.ReadWrite, tableMock.Object),
            Mock.Of<ILogger<CreateRecordActivity>>());

        var input = new CreateRecordInput
        {
            RecordType = "OnboardingRecord",
            IdempotencyKey = "idem-key-1",
            Data = new Dictionary<string, object?>
            {
                ["status"] = "pending",
                ["entityId"] = "device-456"
            },
            CapabilityGrants =
            [
                new CapabilityGrant("records", CapabilityKind.Table, CapabilityAccess.ReadWrite)
            ]
        };

        repositoryMock
            .Setup(x => x.GetIdempotencyRecordAsync<CreateRecordOutput>(input.IdempotencyKey, default))
            .ReturnsAsync((CreateRecordOutput?)null);

        tableMock
            .Setup(table => table.InsertAsync(It.IsAny<Dictionary<string, object?>>(), default))
            .ReturnsAsync((Dictionary<string, object?> record, CancellationToken _) => record);

        var result = await activity.Run(input);

        result.RecordType.Should().Be("OnboardingRecord");
        result.WasExisting.Should().BeFalse();
        result.RecordId.Should().NotBeNullOrWhiteSpace();

        tableMock.Verify(
            table => table.InsertAsync(
                It.Is<Dictionary<string, object?>>(record =>
                    object.Equals(record["recordType"], "OnboardingRecord") &&
                    object.Equals(record["idempotencyKey"], "idem-key-1") &&
                    record.ContainsKey("id")),
                default),
            Times.Once);

        repositoryMock.Verify(
            x => x.SaveIdempotencyRecordAsync(
                input.IdempotencyKey,
                It.IsAny<CreateRecordOutput>(),
                default),
            Times.Once);
        repositoryMock.Verify(
            x => x.CreateRecordAsync(It.IsAny<Dictionary<string, object?>>(), It.IsAny<string>(), default),
            Times.Never);
    }

    [Fact]
    public async Task Run_WithMissingCapabilityGrants_FallsBackToRecordTypeResource()
    {
        var repositoryMock = new Mock<IWorkflowRepository>();
        var tableMock = new Mock<IReadWriteRecordTable>();
        var activity = new CreateRecordActivity(
            repositoryMock.Object,
            TestActivityCapabilityScopeFactoryFactory.CreateScopeFactory("OnboardingRecord", CapabilityAccess.ReadWrite, tableMock.Object),
            Mock.Of<ILogger<CreateRecordActivity>>());

        var input = new CreateRecordInput
        {
            RecordType = "OnboardingRecord",
            IdempotencyKey = "fallback-key",
            Data = new Dictionary<string, object?>
            {
                ["entityId"] = "entity-1"
            }
        };

        repositoryMock
            .Setup(x => x.GetIdempotencyRecordAsync<CreateRecordOutput>(input.IdempotencyKey, default))
            .ReturnsAsync((CreateRecordOutput?)null);
        tableMock
            .Setup(table => table.InsertAsync(It.IsAny<Dictionary<string, object?>>(), default))
            .ReturnsAsync((Dictionary<string, object?> record, CancellationToken _) => record);

        var result = await activity.Run(input);

        result.RecordType.Should().Be("OnboardingRecord");
        tableMock.Verify(table => table.InsertAsync(It.IsAny<Dictionary<string, object?>>(), default), Times.Once);
    }

    [Fact]
    public async Task Run_WithExistingIdempotencyKey_ReturnsExistingRecord()
    {
        var repositoryMock = new Mock<IWorkflowRepository>();
        var tableMock = new Mock<IReadWriteRecordTable>();
        var activity = new CreateRecordActivity(
            repositoryMock.Object,
            TestActivityCapabilityScopeFactoryFactory.CreateScopeFactory("records", CapabilityAccess.ReadWrite, tableMock.Object),
            Mock.Of<ILogger<CreateRecordActivity>>());

        var input = new CreateRecordInput
        {
            RecordType = "OnboardingRecord",
            IdempotencyKey = "existing-key",
            Data = new Dictionary<string, object?> { ["status"] = "pending" },
            CapabilityGrants =
            [
                new CapabilityGrant("records", CapabilityKind.Table, CapabilityAccess.ReadWrite)
            ]
        };

        var existingResult = new CreateRecordOutput
        {
            RecordId = "existing-record-id",
            RecordType = "OnboardingRecord",
            WasExisting = false,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        repositoryMock
            .Setup(x => x.GetIdempotencyRecordAsync<CreateRecordOutput>(input.IdempotencyKey, default))
            .ReturnsAsync(existingResult);

        var result = await activity.Run(input);

        result.RecordId.Should().Be("existing-record-id");
        result.WasExisting.Should().BeTrue();

        tableMock.Verify(x => x.InsertAsync(It.IsAny<Dictionary<string, object?>>(), default), Times.Never);
    }

    [Fact]
    public async Task Run_WithReadOnlyGrant_Throws()
    {
        var activity = new CreateRecordActivity(
            Mock.Of<IWorkflowRepository>(),
            TestActivityCapabilityScopeFactoryFactory.CreateScopeFactory("records", CapabilityAccess.Read, Mock.Of<IReadWriteRecordTable>()),
            Mock.Of<ILogger<CreateRecordActivity>>());

        var input = new CreateRecordInput
        {
            RecordType = "OnboardingRecord",
            IdempotencyKey = "readonly-key",
            Data = new Dictionary<string, object?>(),
            CapabilityGrants =
            [
                new CapabilityGrant("records", CapabilityKind.Table, CapabilityAccess.Read)
            ]
        };

        var act = () => activity.Run(input);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*requires read/write access*");
    }
}

public class GetRecordActivityTests
{
    [Fact]
    public async Task Run_WhenRecordExists_ReturnsRecordFromCapabilityTable()
    {
        var tableMock = new Mock<IReadWriteRecordTable>();
        tableMock
            .Setup(table => table.GetByIdAsync("record-1", default))
            .ReturnsAsync(new Dictionary<string, object?>
            {
                ["id"] = "record-1",
                ["status"] = "ready"
            });

        var activity = new GetRecordActivity(
            TestActivityCapabilityScopeFactoryFactory.CreateScopeFactory("records", CapabilityAccess.Read, tableMock.Object),
            Mock.Of<ILogger<GetRecordActivity>>());

        var result = await activity.Run(new GetRecordInput
        {
            RecordId = "record-1",
            RecordType = "OnboardingRecord",
            CapabilityGrants =
            [
                new CapabilityGrant("records", CapabilityKind.Table, CapabilityAccess.Read)
            ]
        });

        result.Found.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!["status"].Should().Be("ready");
    }

    [Fact]
    public async Task Run_WhenRecordMissing_ReturnsNotFound()
    {
        var tableMock = new Mock<IReadWriteRecordTable>();
        tableMock
            .Setup(table => table.GetByIdAsync("missing", default))
            .ReturnsAsync((Dictionary<string, object?>?)null);

        var activity = new GetRecordActivity(
            TestActivityCapabilityScopeFactoryFactory.CreateScopeFactory("records", CapabilityAccess.Read, tableMock.Object),
            Mock.Of<ILogger<GetRecordActivity>>());

        var result = await activity.Run(new GetRecordInput
        {
            RecordId = "missing",
            RecordType = "OnboardingRecord",
            CapabilityGrants =
            [
                new CapabilityGrant("records", CapabilityKind.Table, CapabilityAccess.Read)
            ]
        });

        result.Found.Should().BeFalse();
        result.RecordId.Should().Be("missing");
    }
}

public class UpdateRecordActivityTests
{
    [Fact]
    public async Task Run_UsesCapabilityCrud_AndCapturesPreviousValues()
    {
        var repositoryMock = new Mock<IWorkflowRepository>();
        var tableMock = new Mock<IReadWriteRecordTable>();
        tableMock
            .Setup(table => table.GetByIdAsync("record-1", default))
            .ReturnsAsync(new Dictionary<string, object?>
            {
                ["id"] = "record-1",
                ["status"] = "pending",
                ["priority"] = 1
            });
        tableMock
            .Setup(table => table.UpdateAsync(It.IsAny<Dictionary<string, object?>>(), default))
            .ReturnsAsync((Dictionary<string, object?> record, CancellationToken _) => record);

        var activity = new UpdateRecordActivity(
            repositoryMock.Object,
            TestActivityCapabilityScopeFactoryFactory.CreateScopeFactory("records", CapabilityAccess.ReadWrite, tableMock.Object),
            Mock.Of<ILogger<UpdateRecordActivity>>());

        var result = await activity.Run(new UpdateRecordInput
        {
            RecordId = "record-1",
            RecordType = "OnboardingRecord",
            Updates = new Dictionary<string, object?>
            {
                ["status"] = "complete",
                ["priority"] = 3
            },
            IdempotencyKey = "update-idem",
            CapabilityGrants =
            [
                new CapabilityGrant("records", CapabilityKind.Table, CapabilityAccess.ReadWrite)
            ]
        });

        result.Success.Should().BeTrue();
        result.PreviousValues.Should().NotBeNull();
        result.PreviousValues!["status"].Should().Be("pending");
        result.PreviousValues["priority"].Should().Be(1);

        tableMock.Verify(
            table => table.UpdateAsync(
                It.Is<Dictionary<string, object?>>(record =>
                    object.Equals(record["status"], "complete") &&
                    object.Equals(record["priority"], 3) &&
                    record.ContainsKey("updatedAt")),
                default),
            Times.Once);
        repositoryMock.Verify(
            x => x.SaveIdempotencyRecordAsync("update-idem", It.IsAny<UpdateRecordOutput>(), default),
            Times.Once);
        repositoryMock.Verify(
            x => x.UpdateRecordAsync(It.IsAny<Dictionary<string, object?>>(), default),
            Times.Never);
    }
}

public class CompensateCreateRecordActivityTests
{
    [Fact]
    public async Task Run_WhenRecordMissing_ReturnsAlreadyDeletedWithoutDelete()
    {
        var repositoryMock = new Mock<IWorkflowRepository>();
        var tableMock = new Mock<IReadWriteRecordTable>();
        tableMock
            .Setup(table => table.GetByIdAsync("record-1", default))
            .ReturnsAsync((Dictionary<string, object?>?)null);

        var activity = new CompensateCreateRecordActivity(
            repositoryMock.Object,
            TestActivityCapabilityScopeFactoryFactory.CreateScopeFactory("records", CapabilityAccess.ReadWrite, tableMock.Object),
            Mock.Of<ILogger<CompensateCreateRecordActivity>>());

        var result = await activity.Run(new CompensateCreateRecordInput
        {
            RecordId = "record-1",
            RecordType = "OnboardingRecord",
            IdempotencyKey = "compensate-1",
            CapabilityGrants =
            [
                new CapabilityGrant("records", CapabilityKind.Table, CapabilityAccess.ReadWrite)
            ]
        });

        result.Success.Should().BeTrue();
        result.WasAlreadyDeleted.Should().BeTrue();

        tableMock.Verify(table => table.DeleteByIdAsync(It.IsAny<string>(), default), Times.Never);
        repositoryMock.Verify(
            x => x.SaveIdempotencyRecordAsync(It.IsAny<string>(), It.IsAny<CompensateCreateRecordOutput>(), default),
            Times.Once);
    }

    [Fact]
    public async Task Run_WhenRecordExists_DeletesThroughCapabilityTable()
    {
        var repositoryMock = new Mock<IWorkflowRepository>();
        var tableMock = new Mock<IReadWriteRecordTable>();
        tableMock
            .Setup(table => table.GetByIdAsync("record-2", default))
            .ReturnsAsync(new Dictionary<string, object?>
            {
                ["id"] = "record-2"
            });

        var activity = new CompensateCreateRecordActivity(
            repositoryMock.Object,
            TestActivityCapabilityScopeFactoryFactory.CreateScopeFactory("records", CapabilityAccess.ReadWrite, tableMock.Object),
            Mock.Of<ILogger<CompensateCreateRecordActivity>>());

        var result = await activity.Run(new CompensateCreateRecordInput
        {
            RecordId = "record-2",
            RecordType = "OnboardingRecord",
            CapabilityGrants =
            [
                new CapabilityGrant("records", CapabilityKind.Table, CapabilityAccess.ReadWrite)
            ]
        });

        result.Success.Should().BeTrue();
        result.WasAlreadyDeleted.Should().BeFalse();

        tableMock.Verify(table => table.DeleteByIdAsync("record-2", default), Times.Once);
        repositoryMock.Verify(
            x => x.DeleteRecordAsync<Dictionary<string, object?>>(It.IsAny<string>(), default),
            Times.Never);
    }
}

file sealed class TestActivityCapabilityScopeFactory : IActivityCapabilityScopeFactory
{
    private readonly CapabilityScope _scope;

    public TestActivityCapabilityScopeFactory(CapabilityScope scope)
    {
        _scope = scope;
    }

    public CapabilityScope CreateScope(IEnumerable<CapabilityGrant> grants)
    {
        return _scope;
    }
}

file static class TestActivityCapabilityScopeFactoryFactory
{
    public static TestActivityCapabilityScopeFactory CreateScopeFactory(
        string resourceName,
        CapabilityAccess access,
        IReadWriteRecordTable table)
    {
        var scope = new CapabilityScope(
            new Dictionary<string, CapabilityScope.TableCapabilityRegistration>(),
            new Dictionary<string, CapabilityScope.RecordCapabilityRegistration>
            {
                [resourceName] = new(access, table)
            },
            new Dictionary<string, CapabilityScope.BucketCapabilityRegistration>(),
            new Dictionary<string, CapabilityScope.FunctionCapabilityRegistration>());

        return new TestActivityCapabilityScopeFactory(scope);
    }
}
