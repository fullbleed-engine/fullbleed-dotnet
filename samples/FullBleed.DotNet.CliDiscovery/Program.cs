using FullBleed.DotNet;

var client = new FullBleedCliClient();
var capabilities = await client.GetCapabilitiesAsync();
var contract = await client.GetAgentContractAsync();

Console.WriteLine($"installed Fullbleed: {contract.GetProperty("product").GetProperty("version").GetString()}");
Console.WriteLine("commands:");
foreach (var command in capabilities.GetProperty("commands").EnumerateArray())
{
    Console.WriteLine($"  - {command.GetString()}");
}

var renderSchema = await client.GetSchemaAsync(["render"]);
Console.WriteLine($"render result schema: {renderSchema.GetProperty("target").GetString()}");
