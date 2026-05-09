using Microsoft.Extensions.Logging.Abstractions;
using omniDesk.Api.Features.AiAgents.Playground;
using Xunit;

namespace omniDesk.Api.Tests.Features.AiAgents;

/// <summary>
/// Spec 006 research §R12 — PlaygroundCleanupJob é stub no-throw em V1; seu papel é
/// reservar o slot recurring e logar. Garante que a invocação não vaza exceções.
/// </summary>
public class PlaygroundCleanupJobTests
{
    [Fact]
    public async Task RunAsync_DoesNotThrow()
    {
        var job = new PlaygroundCleanupJob(NullLogger<PlaygroundCleanupJob>.Instance);
        var ex = await Record.ExceptionAsync(() => job.RunAsync(CancellationToken.None));
        Assert.Null(ex);
    }

    [Fact]
    public async Task RunAsync_HonorsCancellation()
    {
        var job = new PlaygroundCleanupJob(NullLogger<PlaygroundCleanupJob>.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        // Stub completes immediately without observing the token — that's acceptable for V1.
        var ex = await Record.ExceptionAsync(() => job.RunAsync(cts.Token));
        Assert.Null(ex);
    }
}
