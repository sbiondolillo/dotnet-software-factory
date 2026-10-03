using Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.CommandLine;

HostApplicationBuilder builder = Host.CreateApplicationBuilder();
builder.Logging.ClearProviders();
builder.Services.AddSingleton<CommandRunner>();
builder.Services.AddSingleton<HttpMessageHandler>(_ => new HttpClientHandler());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ConsoleHost>(provider =>
{
    CommandRunner runner = provider.GetRequiredService<CommandRunner>();
    HttpMessageHandler handler = provider.GetRequiredService<HttpMessageHandler>();
    TimeProvider time = provider.GetRequiredService<TimeProvider>();
    return new ConsoleHost(
        Environment.GetEnvironmentVariable,
        runner,
        handler,
        Console.Out,
        Console.Error,
        Directory.GetCurrentDirectory(),
        (span, token) => Task.Delay(span, token),
        time);
});
using IHost host = builder.Build();
ConsoleHost console = host.Services.GetRequiredService<ConsoleHost>();
RootCommand root = ConsoleCommands.Create(console);
ParseResult parse = root.Parse(args);
return await parse.InvokeAsync(new InvocationConfiguration { Output = console.Out, Error = console.Error }).ConfigureAwait(false);
