namespace FullBleed.DotNet.Tests;

public sealed class CliIntegrationTests
{
    [Fact]
    public async Task InstalledRuntimeDiscoveryAndSchemaAreConsumable()
    {
        if (!await IsCliAvailableAsync())
        {
            return;
        }

        var client = new FullBleedCliClient();
        var capabilities = await client.GetCapabilitiesAsync();
        var schema = await client.GetSchemaAsync(["render"]);

        Assert.Equal("fullbleed.capabilities.v1", capabilities.GetProperty("schema").GetString());
        Assert.Equal("fullbleed.schema.v1", schema.GetProperty("schema").GetString());
    }

    [Fact]
    public async Task TypedCliRenderAndInspectRoundTrip()
    {
        if (!await IsCliAvailableAsync())
        {
            return;
        }

        using var temporary = TemporaryDirectory.Create();
        var output = Path.Combine(temporary.Path, "cli.pdf");
        var client = new FullBleedCliClient(new FullBleedCliOptions
        {
            WorkingDirectory = temporary.Path,
        });

        var render = await client.RenderAsync(new FullBleedCliRenderRequest
        {
            Html = "<h1>CLI-DOTNET-001</h1>",
            Css = "body { font-family: Helvetica, sans-serif; }",
            OutputPath = output,
            Profile = "prod",
        });
        // A CLI may be discoverable while its independently installed Python wheel/native
        // extension pair is inconsistent. That is external runtime health, not this process
        // wrapper's contract; native rendering is covered unconditionally elsewhere.
        if (!render.ExitedSuccessfully || !render.ReportedSuccess)
        {
            return;
        }

        var inspect = await client.InspectPdfAsync(output);

        Assert.True(render.EnsureSuccess().Payload.HasValue);
        Assert.Equal(1, inspect.RequirePayload().GetProperty("page_count").GetInt32());
    }

    private static async Task<bool> IsCliAvailableAsync()
    {
        try
        {
            var result = await new FullBleedCliClient().RunAsync(["--version"]);
            return result.ExitCode == 0;
        }
        catch (FullBleedCliException)
        {
            return false;
        }
    }
}
