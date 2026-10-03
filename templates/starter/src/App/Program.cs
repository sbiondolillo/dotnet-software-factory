using Core;
using Microsoft.Extensions.Hosting;
using System.CommandLine;

HostApplicationBuilder builder = Host.CreateApplicationBuilder();
using IHost host = builder.Build();
RootCommand root = StarterCommands.Create(Console.Out);
ParseResult parse = root.Parse(args);
return await parse.InvokeAsync(new InvocationConfiguration { Output = Console.Out, Error = Console.Error }).ConfigureAwait(false);
