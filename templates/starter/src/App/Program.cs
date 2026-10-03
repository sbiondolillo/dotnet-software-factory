using Core;
using Microsoft.Extensions.Hosting;
using System.CommandLine;

using IHost host = Host.CreateApplicationBuilder().Build();
RootCommand root = GreetCommand.Create();
ParseResult parse = root.Parse(args);
return await parse.InvokeAsync().ConfigureAwait(false);
