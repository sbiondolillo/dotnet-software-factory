using Core;
using Microsoft.Extensions.Hosting;
using System.CommandLine;

HostApplicationBuilder builder = Host.CreateApplicationBuilder();
using IHost host = builder.Build();
RootCommand root = GreeterCommands.Create();
ParseResult parse = root.Parse(args);
return await parse.InvokeAsync().ConfigureAwait(false);
