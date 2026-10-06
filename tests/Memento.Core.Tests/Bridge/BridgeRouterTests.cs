using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Bridge;

public sealed class BridgeRouterTests
{
    private static BridgeRouter CreateRouter(params IBridgeHandler[] handlers) =>
        new(handlers, NullLogger<BridgeRouter>.Instance);

    private static async Task<JsonElement> SendAsync(BridgeRouter router, string message)
    {
        var json = await router.HandleAsync(message, CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string ErrorCode(JsonElement response) =>
        response.GetProperty("error").GetProperty("code").GetString()!;

    [Fact]
    public async Task DispatchesToTheHandlerNamedByMethodAndEchoesTheId()
    {
        var echo = DelegateHandler.Echo("test.echo");
        var other = DelegateHandler.Echo("test.other");
        var router = CreateRouter(echo, other);

        var response = await SendAsync(router, """{"id":42,"method":"test.echo","params":{"value":7}}""");

        Assert.Equal(42, response.GetProperty("id").GetInt64());
        Assert.Equal(7, response.GetProperty("result").GetProperty("value").GetInt32());
        Assert.False(response.TryGetProperty("error", out _));
        Assert.Equal(1, echo.Calls);
        Assert.Equal(0, other.Calls);
    }

    [Theory]
    [InlineData("""{"id":1,"method":"test.echo"}""")]
    [InlineData("""{"id":1,"method":"test.echo","params":null}""")]
    public async Task MissingOrNullParamsArriveAsAnEmptyObject(string message)
    {
        var router = CreateRouter(DelegateHandler.Echo("test.echo"));

        var response = await SendAsync(router, message);

        var result = response.GetProperty("result");
        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.Empty(result.EnumerateObject());
    }

    [Fact]
    public async Task UnknownMethodIsReportedWithTheRequestId()
    {
        var router = CreateRouter(DelegateHandler.Echo("test.echo"));

        var response = await SendAsync(router, """{"id":5,"method":"library.nothing"}""");

        Assert.Equal(5, response.GetProperty("id").GetInt64());
        Assert.Equal(BridgeErrorCodes.UnknownMethod, ErrorCode(response));
        Assert.Contains("library.nothing", response.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(response.TryGetProperty("result", out _));
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("""{"id":1,"method":"test.echo",}""")]
    public async Task MalformedJsonIsReportedWithANullId(string message)
    {
        var router = CreateRouter(DelegateHandler.Echo("test.echo"));

        var response = await SendAsync(router, message);

        Assert.Equal(JsonValueKind.Null, response.GetProperty("id").ValueKind);
        Assert.Equal(BridgeErrorCodes.InvalidJson, ErrorCode(response));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("[]", null)]
    [InlineData("42", null)]
    [InlineData("""{"method":"test.echo"}""", null)]
    [InlineData("""{"id":"1","method":"test.echo"}""", null)]
    [InlineData("""{"id":1.5,"method":"test.echo"}""", null)]
    [InlineData("""{"id":-1,"method":"test.echo"}""", null)]
    [InlineData("""{"id":3}""", 3L)]
    [InlineData("""{"id":3,"method":7}""", 3L)]
    [InlineData("""{"id":3,"method":"Test.Echo"}""", 3L)]
    [InlineData("""{"id":3,"method":"echo"}""", 3L)]
    [InlineData("""{"id":3,"method":"test.echo","extra":true}""", 3L)]
    public async Task InvalidEnvelopesAreRejected(string message, long? expectedId)
    {
        var handler = DelegateHandler.Echo("test.echo");
        var router = CreateRouter(handler);

        var response = await SendAsync(router, message);

        Assert.Equal(BridgeErrorCodes.InvalidRequest, ErrorCode(response));
        if (expectedId is null)
        {
            Assert.Equal(JsonValueKind.Null, response.GetProperty("id").ValueKind);
        }
        else
        {
            Assert.Equal(expectedId, response.GetProperty("id").GetInt64());
        }

        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("""{"id":1,"method":"test.echo","params":[1,2]}""")]
    [InlineData("""{"id":1,"method":"test.echo","params":"x"}""")]
    public async Task NonObjectParamsAreRejected(string message)
    {
        var handler = DelegateHandler.Echo("test.echo");
        var router = CreateRouter(handler);

        var response = await SendAsync(router, message);

        Assert.Equal(BridgeErrorCodes.InvalidParams, ErrorCode(response));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task OversizedMessagesAreRejectedBeforeParsing()
    {
        var handler = DelegateHandler.Echo("test.echo");
        var router = CreateRouter(handler);
        var message = """{"id":1,"method":"test.echo","params":{"pad":" """ + new string('x', BridgeRouter.MaxMessageLength) + "\"}}";

        var response = await SendAsync(router, message);

        Assert.Equal(BridgeErrorCodes.InvalidRequest, ErrorCode(response));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task DeeplyNestedJsonIsRejected()
    {
        var router = CreateRouter(DelegateHandler.Echo("test.echo"));
        var nested = string.Concat(Enumerable.Repeat("{\"a\":", 64)) + "1" + new string('}', 64);

        var response = await SendAsync(router, "{\"id\":1,\"method\":\"test.echo\",\"params\":" + nested + "}");

        Assert.Equal(BridgeErrorCodes.InvalidJson, ErrorCode(response));
    }

    [Fact]
    public async Task BridgeExceptionsBecomeTheirStructuredError()
    {
        var router = CreateRouter(DelegateHandler.Throwing(
            "test.fail",
            new BridgeException("test.specific", "The thing failed at 10:02. Your data is safe.", "Free space: 4 GB")));

        var response = await SendAsync(router, """{"id":9,"method":"test.fail"}""");

        var error = response.GetProperty("error");
        Assert.Equal(9, response.GetProperty("id").GetInt64());
        Assert.Equal("test.specific", error.GetProperty("code").GetString());
        Assert.Equal("The thing failed at 10:02. Your data is safe.", error.GetProperty("message").GetString());
        Assert.Equal("Free space: 4 GB", error.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task UnexpectedExceptionsNeverLeakTheirMessageOrStackTrace()
    {
        InvalidOperationException thrown;
        try
        {
            throw new InvalidOperationException("secret internal state C:\\private\\path");
        }
        catch (InvalidOperationException ex)
        {
            thrown = ex;
        }

        var router = CreateRouter(DelegateHandler.Throwing("test.crash", thrown));

        var raw = await router.HandleAsync("""{"id":11,"method":"test.crash"}""", CancellationToken.None);
        using var document = JsonDocument.Parse(raw);
        var error = document.RootElement.GetProperty("error");

        Assert.Equal(11, document.RootElement.GetProperty("id").GetInt64());
        Assert.Equal(BridgeErrorCodes.Internal, error.GetProperty("code").GetString());
        Assert.Contains("test.crash", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("private", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", raw, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", raw, StringComparison.Ordinal);
        Assert.StartsWith("Log reference ", error.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationDuringShutdownIsReportedAsCancelled()
    {
        using var cts = new CancellationTokenSource();
        var router = CreateRouter(new DelegateHandler("test.slow", (_, token) =>
        {
            cts.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(default(JsonElement));
        }));

        var response = await router.DispatchAsync("""{"id":2,"method":"test.slow"}""", cts.Token);

        Assert.Equal(BridgeErrorCodes.Cancelled, response.Error?.Code);
    }

    [Fact]
    public void DuplicateMethodNamesAreRejectedAtStartup()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CreateRouter(DelegateHandler.Echo("test.echo"), DelegateHandler.Echo("test.echo")));

        Assert.Contains("test.echo", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MethodNamesMustBeAreaDotVerb()
    {
        Assert.Throws<ArgumentException>(() => CreateRouter(DelegateHandler.Echo("NoDot")));
    }

    [Fact]
    public void TheProductionRegistrationExposesEveryM0Method()
    {
        using var host = new BridgeTestHost();

        string[] expected = ["app.openExternal", "app.version", "library.list", "settings.get", "settings.set", "ui.ready"];

        Assert.Equal(expected, host.Router.MethodNames.Order(StringComparer.Ordinal));
    }
}
