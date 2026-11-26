using System.Diagnostics;
using System.Text.Json;
using StreamJsonRpc;

namespace GitFlow.Server.Tests;

/// <summary>The real server process, spoken to over stdin/stdout like the VS Code extension does.</summary>
public class ServerProcessTests
{
    [Fact]
    public async Task Server_process_answers_over_stdio()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "GitFlow.Server.dll");
        File.Exists(dll).ShouldBeTrue(dll);
        using var process = Process.Start(new ProcessStartInfo("dotnet", $"\"{dll}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;

        try
        {
            var formatter = new SystemTextJsonFormatter { JsonSerializerOptions = RpcHost.JsonOptions };

            // Disposing the client closes the server's stdin, which ends the server.
            using (var client = new JsonRpc(new HeaderDelimitedMessageHandler(
                process.StandardInput.BaseStream, process.StandardOutput.BaseStream, formatter)))
            {
                client.StartListening();

                var info = await client.InvokeWithParameterObjectAsync<JsonElement>("initialize", new { culture = "en-US" }).WaitAsync(TimeSpan.FromSeconds(30));
                var root = await client.InvokeWithParameterObjectAsync<string?>("findRepository", new { path = Path.GetTempPath() });

                info.GetProperty("culture").GetString().ShouldBe("en-US");
                root.ShouldBeNull();
            }

            process.WaitForExit(10_000).ShouldBeTrue("closing stdin ends the server");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }

        process.ExitCode.ShouldBe(0, "closing stdin ends the server cleanly");
    }
}
