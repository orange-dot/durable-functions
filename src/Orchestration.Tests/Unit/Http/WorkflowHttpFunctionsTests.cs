using System.Collections.Specialized;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Azure.Core.Serialization;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Orchestration.Core.Models;
using Orchestration.Functions.Http;
using Orchestration.Functions.Orchestrators;

namespace Orchestration.Tests.Unit.Http;

public sealed class WorkflowHttpFunctionsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task StartWorkflow_StartsOrchestration_AndReturnsAcceptedResponse()
    {
        var logger = Mock.Of<ILogger<StartWorkflowFunction>>();
        var client = new Mock<DurableTaskClient>("test-client");
        client.Setup(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(name => name.Name == nameof(WorkflowOrchestrator)),
                It.Is<object?>(value => MatchesWorkflowInput(value, "order-processing", "order-123", "idem-1")),
                It.Is<StartOrchestrationOptions>(options => options.InstanceId == "instance-123"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("instance-123");

        var request = CreateRequest(
            method: "POST",
            url: "https://localhost/api/workflows",
            body: """
                {
                  "workflowType": "order-processing",
                  "entityId": "order-123",
                  "instanceId": "instance-123",
                  "idempotencyKey": "idem-1",
                  "data": {
                    "total": 12.5
                  }
                }
                """);

        var function = new StartWorkflowFunction(logger);

        var response = await function.Run(request, client.Object);
        var body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.GetValues("Location").Should().ContainSingle("/api/workflows/instance-123");
        body.GetProperty("InstanceId").GetString().Should().Be("instance-123");
        body.GetProperty("StatusUri").GetString().Should().Be("/api/workflows/instance-123");
    }

    [Fact]
    public async Task GetWorkflowStatus_UsesWorkflowTypeFromInput_AndReturnsStructuredPayloads()
    {
        var logger = Mock.Of<ILogger<GetWorkflowStatusFunction>>();
        var client = new Mock<DurableTaskClient>("test-client");
        var metadata = CreateMetadata(
            instanceId: "instance-456",
            runtimeStatus: OrchestrationRuntimeStatus.Completed,
            workflowType: "invoice-flow",
            output: new
            {
                success = true,
                summary = new { total = 3 }
            },
            customStatus: new
            {
                currentStep = "Completed",
                variables = new { invoiceId = "inv-9" }
            });

        client.Setup(x => x.GetInstanceAsync("instance-456", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        var request = CreateRequest(
            method: "GET",
            url: "https://localhost/api/workflows/instance-456");
        var function = new GetWorkflowStatusFunction(logger);

        var response = await function.Run(request, client.Object, "instance-456");
        var body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.GetProperty("WorkflowType").GetString().Should().Be("invoice-flow");
        body.GetProperty("Input").ValueKind.Should().Be(JsonValueKind.Object);
        body.GetProperty("Output").ValueKind.Should().Be(JsonValueKind.Object);
        body.GetProperty("CustomStatus").ValueKind.Should().Be(JsonValueKind.Object);
        body.GetProperty("Input").GetProperty("workflowType").GetString().Should().Be("invoice-flow");
        body.GetProperty("Output").GetProperty("summary").GetProperty("total").GetInt32().Should().Be(3);
        body.GetProperty("CustomStatus").GetProperty("variables").GetProperty("invoiceId").GetString().Should().Be("inv-9");
    }

    [Fact]
    public async Task ListWorkflows_ReturnsFirstPageAndContinuationToken()
    {
        var logger = Mock.Of<ILogger<GetWorkflowStatusFunction>>();
        var client = new Mock<DurableTaskClient>("test-client");
        OrchestrationQuery? capturedQuery = null;

        client.Setup(x => x.GetAllInstancesAsync(It.IsAny<OrchestrationQuery>()))
            .Returns((OrchestrationQuery query) =>
            {
                capturedQuery = query;
                return new TestAsyncPageable<OrchestrationMetadata>(
                    new Microsoft.DurableTask.Page<OrchestrationMetadata>(
                    [
                        CreateMetadata("instance-a", OrchestrationRuntimeStatus.Running, "device-onboarding")
                    ],
                    continuationToken: "next-page"),
                    new Microsoft.DurableTask.Page<OrchestrationMetadata>(
                    [
                        CreateMetadata("instance-b", OrchestrationRuntimeStatus.Completed, "invoice-flow")
                    ],
                    continuationToken: null));
            });

        var request = CreateRequest(
            method: "GET",
            url: "https://localhost/api/workflows",
            query: new NameValueCollection
            {
                ["pageSize"] = "1"
            });
        var function = new GetWorkflowStatusFunction(logger);

        var response = await function.ListWorkflows(request, client.Object);
        var body = await ReadJsonAsync(response);

        capturedQuery.Should().NotBeNull();
        capturedQuery!.FetchInputsAndOutputs.Should().BeTrue();
        capturedQuery.PageSize.Should().Be(1);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.GetProperty("count").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("returnedCount").GetInt32().Should().Be(1);
        body.GetProperty("pageSize").GetInt32().Should().Be(1);
        body.GetProperty("hasMore").GetBoolean().Should().BeTrue();
        body.GetProperty("continuationToken").GetString().Should().Be("next-page");
        body.GetProperty("workflows").GetArrayLength().Should().Be(1);
        body.GetProperty("workflows")[0].GetProperty("WorkflowType").GetString().Should().Be("device-onboarding");
    }

    [Fact]
    public async Task ListWorkflows_ReturnsExactCount_WhenSinglePageExhaustsQuery()
    {
        var logger = Mock.Of<ILogger<GetWorkflowStatusFunction>>();
        var client = new Mock<DurableTaskClient>("test-client");
        var metadata = new[]
        {
            CreateMetadata("instance-a", OrchestrationRuntimeStatus.Running, "device-onboarding"),
            CreateMetadata("instance-b", OrchestrationRuntimeStatus.Completed, "invoice-flow")
        };

        client.Setup(x => x.GetAllInstancesAsync(It.IsAny<OrchestrationQuery>()))
            .Returns(new TestAsyncPageable<OrchestrationMetadata>(
                new Microsoft.DurableTask.Page<OrchestrationMetadata>(metadata, continuationToken: null)));

        var request = CreateRequest(
            method: "GET",
            url: "https://localhost/api/workflows");
        var function = new GetWorkflowStatusFunction(logger);

        var response = await function.ListWorkflows(request, client.Object);
        var body = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.GetProperty("count").GetInt32().Should().Be(2);
        body.GetProperty("returnedCount").GetInt32().Should().Be(2);
        body.GetProperty("hasMore").GetBoolean().Should().BeFalse();
        body.GetProperty("continuationToken").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void TerminateWorkflow_UsesAnonymousAuthorization()
    {
        var method = typeof(RaiseEventFunction).GetMethod(nameof(RaiseEventFunction.TerminateWorkflow));
        var triggerParameter = method!.GetParameters().First(parameter => parameter.Name == "req");
        var attribute = triggerParameter.GetCustomAttributes(typeof(HttpTriggerAttribute), false)
            .Cast<HttpTriggerAttribute>()
            .Single();

        attribute.AuthLevel.Should().Be(AuthorizationLevel.Anonymous);
    }

    private static OrchestrationMetadata CreateMetadata(
        string instanceId,
        OrchestrationRuntimeStatus runtimeStatus,
        string workflowType,
        object? output = null,
        object? customStatus = null)
    {
        return new OrchestrationMetadata(nameof(WorkflowOrchestrator), instanceId)
        {
            RuntimeStatus = runtimeStatus,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            LastUpdatedAt = DateTimeOffset.UtcNow,
            SerializedInput = JsonSerializer.Serialize(new WorkflowInput
            {
                WorkflowType = workflowType,
                EntityId = "entity-1",
                CorrelationId = "corr-1",
                Data = new Dictionary<string, object?>
                {
                    ["customerId"] = "customer-1"
                }
            }, JsonOptions),
            SerializedOutput = output is null ? null : JsonSerializer.Serialize(output, JsonOptions),
            SerializedCustomStatus = customStatus is null ? null : JsonSerializer.Serialize(customStatus, JsonOptions)
        };
    }

    private static bool MatchesWorkflowInput(
        object? value,
        string workflowType,
        string entityId,
        string idempotencyKey)
    {
        if (value is not WorkflowInput input)
        {
            return false;
        }

        return input.WorkflowType == workflowType &&
               input.EntityId == entityId &&
               input.IdempotencyKey == idempotencyKey;
    }

    private static TestHttpRequestData CreateRequest(
        string method,
        string url,
        string? body = null,
        NameValueCollection? query = null)
    {
        return new TestHttpRequestData(CreateFunctionContext(), method, new Uri(url), body, query);
    }

    private static FunctionContext CreateFunctionContext()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<WorkerOptions>(options =>
        {
            options.Serializer = new JsonObjectSerializer(JsonOptions);
        });

        var context = new Mock<FunctionContext>();
        context.SetupProperty(x => x.InstanceServices, services.BuildServiceProvider());
        context.SetupProperty(x => x.Items, new Dictionary<object, object>());
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseData response)
    {
        response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(response.Body);
        return document.RootElement.Clone();
    }

    private sealed class TestHttpRequestData : HttpRequestData
    {
        private readonly NameValueCollection _query;
        private readonly string _method;
        private readonly Uri _url;

        public TestHttpRequestData(
            FunctionContext functionContext,
            string method,
            Uri url,
            string? body,
            NameValueCollection? query)
            : base(functionContext)
        {
            _method = method;
            _url = url;
            _query = query ?? new NameValueCollection();
            Body = new MemoryStream(Encoding.UTF8.GetBytes(body ?? string.Empty));
            Headers = new HttpHeadersCollection();
        }

        public override Stream Body { get; }

        public override HttpHeadersCollection Headers { get; }

        public override IReadOnlyCollection<IHttpCookie> Cookies { get; } = Array.Empty<IHttpCookie>();

        public override Uri Url => _url;

        public override IEnumerable<ClaimsIdentity> Identities { get; } = Array.Empty<ClaimsIdentity>();

        public override string Method => _method;

        public override NameValueCollection Query => _query;

        public override HttpResponseData CreateResponse()
        {
            return new TestHttpResponseData(FunctionContext);
        }
    }

    private sealed class TestHttpResponseData : HttpResponseData
    {
        private readonly HttpCookies _cookies = Mock.Of<HttpCookies>();

        public TestHttpResponseData(FunctionContext functionContext)
            : base(functionContext)
        {
            Headers = new HttpHeadersCollection();
            Body = new MemoryStream();
            StatusCode = HttpStatusCode.OK;
        }

        public override HttpStatusCode StatusCode { get; set; }

        public override HttpHeadersCollection Headers { get; set; }

        public override Stream Body { get; set; }

        public override HttpCookies Cookies => _cookies;
    }

    private sealed class TestAsyncPageable<T> : Microsoft.DurableTask.AsyncPageable<T>
        where T : notnull
    {
        private readonly IReadOnlyList<Microsoft.DurableTask.Page<T>> _pages;

        public TestAsyncPageable(params Microsoft.DurableTask.Page<T>[] pages)
        {
            _pages = pages;
        }

        public override async IAsyncEnumerable<Microsoft.DurableTask.Page<T>> AsPages(
            string? continuationToken = null,
            int? pageSizeHint = null)
        {
            foreach (var page in _pages)
            {
                yield return page;
            }

            await Task.CompletedTask;
        }
    }
}
