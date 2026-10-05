using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>The plan and the executor's report: who may ask for either, and what a report does to the plan.</summary>
public sealed class TranscodeEndpointTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private static TranscodeStatus Report() => new()
    {
        Units =
        [
            new TranscodeUnitReport
            {
                Id = "video-rung/nothing-here/v0",
                State = TranscodeUnitState.Running,
                Speed = 6.3,
                Heads = new Dictionary<string, double> { ["v0"] = 42 },
                Disk = "ssd",
                Since = DateTimeOffset.UtcNow
            }
        ],
        Disks = [new DiskReading("ssd", 100_000_000_000, 500_000_000_000)]
    };

    [Fact]
    public async Task Nobody_without_the_service_key_reads_the_plan()
    {
        using var anonymous = await fixture.CreateClient().GetAsync("/api/transcode/plan");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task A_person_reads_neither_the_plan_nor_reports()
    {
        var member = fixture.CreatePersonClient();

        using var plan = await member.GetAsync("/api/transcode/plan");
        using var status = await member.PutAsJsonAsync("/api/transcode/status", Report(), ManifestJsonContext.Default.TranscodeStatus);

        Assert.Equal(HttpStatusCode.Forbidden, plan.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, status.StatusCode);
    }

    [Fact]
    public async Task The_service_key_reads_the_plan_and_files_a_report()
    {
        var client = fixture.CreateServiceClient();

        using var filed = await client.PutAsJsonAsync("/api/transcode/status", Report(), ManifestJsonContext.Default.TranscodeStatus);
        var plan = await client.GetFromJsonAsync("/api/transcode/plan", ManifestJsonContext.Default.TranscodePlan);

        Assert.Equal(HttpStatusCode.NoContent, filed.StatusCode);
        Assert.NotNull(plan);
        Assert.True(plan.ExecutorReporting);
    }

    [Fact]
    public async Task A_report_with_nothing_in_it_is_refused()
    {
        using var response = await fixture.CreateServiceClient().PutAsync(
            "/api/transcode/status", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
