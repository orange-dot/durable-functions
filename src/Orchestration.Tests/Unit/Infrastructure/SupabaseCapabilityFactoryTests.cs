using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Orchestration.Core.Capabilities;
using Orchestration.Supabase;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using Supabase.Storage;

namespace Orchestration.Tests.Unit.Infrastructure;

public sealed class SupabaseCapabilityFactoryTests
{
    private readonly Mock<global::OrangeDot.Supabase.ISupabaseClient> _clientMock = new();
    private readonly Mock<global::OrangeDot.Supabase.ISupabaseTable<CapabilityTestRecord>> _tableMock = new();
    private readonly Mock<global::Supabase.Storage.Interfaces.IStorageClient<Bucket, FileObject>> _storageClientMock = new();
    private readonly Mock<global::Supabase.Storage.Interfaces.IStorageFileApi<FileObject>> _bucketMock = new();
    private readonly Mock<global::Supabase.Functions.Interfaces.IFunctionsClient> _functionsClientMock = new();

    public SupabaseCapabilityFactoryTests()
    {
        _clientMock.SetupGet(client => client.Ready).Returns(Task.CompletedTask);
        _clientMock.Setup(client => client.Table<CapabilityTestRecord>()).Returns(_tableMock.Object);
        _clientMock.SetupGet(client => client.Storage).Returns(_storageClientMock.Object);
        _clientMock.SetupGet(client => client.Functions).Returns(_functionsClientMock.Object);

        _storageClientMock.Setup(client => client.From("artifacts")).Returns(_bucketMock.Object);
    }

    [Fact]
    public async Task ReadTable_with_read_grant_delegates_to_supabase_table()
    {
        var expected = new CapabilityTestRecord { Id = "record-1", Name = "Widget" };

        _tableMock
            .Setup(table => table.Filter("id", global::Supabase.Postgrest.Constants.Operator.Equals, "record-1"))
            .Returns(_tableMock.Object);
        _tableMock
            .Setup(table => table.Single(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var factory = CreateFactory();
        var scope = factory.CreateScope(
        [
            new CapabilityGrant("records", CapabilityKind.Table, CapabilityAccess.Read)
        ]);

        var result = await scope.ReadTable<CapabilityTestRecord>("records").GetByIdAsync("record-1");

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public void WriteTable_with_read_grant_throws()
    {
        var factory = CreateFactory();
        var scope = factory.CreateScope(
        [
            new CapabilityGrant("records", CapabilityKind.Table, CapabilityAccess.Read)
        ]);

        Action act = () => scope.WriteTable<CapabilityTestRecord>("records");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*does not allow write access*");
    }

    [Fact]
    public void RecordTable_with_read_write_grant_resolves()
    {
        var factory = CreateFactory();
        var scope = factory.CreateScope(
        [
            new CapabilityGrant("Onboarding", CapabilityKind.Table, CapabilityAccess.ReadWrite)
        ]);

        var table = scope.RecordTable("Onboarding");

        table.Should().NotBeNull();
    }

    [Fact]
    public void RecordTable_with_read_only_grant_throws_for_write_access()
    {
        var factory = CreateFactory();
        var scope = factory.CreateScope(
        [
            new CapabilityGrant("Onboarding", CapabilityKind.Table, CapabilityAccess.Read)
        ]);

        Action act = () => scope.RecordTable("Onboarding");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*does not allow read/write access*");
    }

    [Fact]
    public void ReadTable_with_wrong_record_type_throws()
    {
        var factory = CreateFactory();
        var scope = factory.CreateScope(
        [
            new CapabilityGrant("records", CapabilityKind.Table, CapabilityAccess.Read)
        ]);

        Action act = () => scope.ReadTable<CapabilityOtherRecord>("records");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*configured for record type*");
    }

    [Fact]
    public async Task Bucket_with_read_grant_allows_download_and_blocks_upload()
    {
        _bucketMock
            .Setup(bucket => bucket.Download("artifact.json", (global::Supabase.Storage.TransformOptions?)null, null))
            .ReturnsAsync([0x01, 0x02]);

        var factory = CreateFactory();
        var scope = factory.CreateScope(
        [
            new CapabilityGrant("artifacts", CapabilityKind.StorageBucket, CapabilityAccess.Read)
        ]);

        var bucket = scope.Bucket("artifacts");
        var data = await bucket.DownloadAsync("artifact.json");

        data.Should().Equal([0x01, 0x02]);

        var act = async () => await bucket.UploadAsync([0x03], "artifact.json", "application/json");

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not allow write access*");
    }

    [Fact]
    public async Task EdgeFunctionInvoker_normalizes_body_and_delegates()
    {
        global::Supabase.Functions.Client.InvokeFunctionOptions? capturedOptions = null;

        _functionsClientMock
            .Setup(client => client.Invoke("echo-function", null, It.IsAny<global::Supabase.Functions.Client.InvokeFunctionOptions>()))
            .Callback<string, string?, global::Supabase.Functions.Client.InvokeFunctionOptions?>((_, _, options) => capturedOptions = options)
            .ReturnsAsync("ok");

        var factory = CreateFactory();
        var scope = factory.CreateScope(
        [
            new CapabilityGrant("echo", CapabilityKind.EdgeFunction, CapabilityAccess.Write)
        ]);

        var document = JsonDocument.Parse("""{"attempt":1,"nested":{"success":true}}""");
        var result = await scope.Function("echo").InvokeAsync(new Dictionary<string, object?>
        {
            ["payload"] = document.RootElement
        });

        result.Should().Be("ok");
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Body.Should().ContainKey("payload");
        capturedOptions.Body["payload"].Should().BeOfType<Dictionary<string, object?>>();
    }

    [Fact]
    public void EdgeFunction_with_read_grant_throws_during_scope_creation()
    {
        var factory = CreateFactory();

        var act = () => factory.CreateScope(
        [
            new CapabilityGrant("echo", CapabilityKind.EdgeFunction, CapabilityAccess.Read)
        ]);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*cannot be granted read-only access*");
    }

    [Fact]
    public void Unknown_table_mapping_throws()
    {
        var options = new SupabaseRuntimeOptions();
        var factory = new SupabaseCapabilityFactory(_clientMock.Object, Options.Create(options));

        var act = () => factory.CreateScope(
        [
            new CapabilityGrant("unknown", CapabilityKind.Table, CapabilityAccess.Read)
        ]);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*is not mapped in Supabase runtime options*");
    }

    private SupabaseCapabilityFactory CreateFactory()
    {
        var options = new SupabaseRuntimeOptions
        {
            Url = "http://127.0.0.1:54321",
            ApiKey = "service-role"
        };

        options.MapOnboardingRecordTable("Onboarding");
        options.MapTable<CapabilityTestRecord>("records");
        options.MapStorageBucket("artifacts", "artifacts");
        options.MapEdgeFunction("echo", "echo-function");

        return new SupabaseCapabilityFactory(_clientMock.Object, Options.Create(options));
    }

    [Table("capability_test_records")]
    public sealed class CapabilityTestRecord : BaseModel
    {
        [PrimaryKey("id", true)]
        public string? Id { get; set; }

        [Column("name")]
        public string? Name { get; set; }
    }

    [Table("capability_other_records")]
    public sealed class CapabilityOtherRecord : BaseModel
    {
        [PrimaryKey("id", true)]
        public string? Id { get; set; }
    }
}
