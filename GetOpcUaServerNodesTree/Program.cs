using GetOpcUaServerNodesTree;
using System.CommandLine;

//endpoint
var serverUrl = new Option<string?>
     (name: "--serverUrl",
      description: "Endpoint of machine server (opc.tcp://123.251.215.2:62541)");
serverUrl.AddAlias("-u");
//name
var file = new Option<string>
     (name: "--fileName",
      description: "Name saved file."
         + "\n\t Optional name.",
      getDefaultValue: () => "NodesTree");
file.AddAlias("-n");

var rootCommand = new RootCommand("Command Line");

rootCommand.AddOption(serverUrl);
rootCommand.AddOption(file);
string newEndpointClient = string.Empty;
string filename = string.Empty;
rootCommand.SetHandler((ted, tfile) =>
{
    newEndpointClient = ted ?? string.Empty;
    filename = tfile;
}, serverUrl, file);
await rootCommand.InvokeAsync(args);

if (newEndpointClient == string.Empty)
{
    Console.WriteLine("Machine endpoint missing!");
    return;
}

using UAClient client = new(newEndpointClient, filename);
bool result = await client.GetMachineTreeRecursive();

if (result)
    Console.WriteLine($"File created {Directory.GetCurrentDirectory()}\\{filename}.json");
else
    Console.WriteLine("Error, file not created!");