namespace Orders.ParityTests;

/// <summary>
/// Contract / golden-master tests. Every case is sent to BOTH running APIs. The two responses
/// must match each other AND the golden file recorded from the legacy API, so a change that
/// breaks both sides the same way is still caught.
/// </summary>
public class GoldenParityTests
{
    public static IEnumerable<object[]> CaseNames() => ParityCases.All.Select(c => new object[] { c.Name });

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task Both_apis_match_the_golden_response(string caseName)
    {
        var parityCase = ParityCases.Get(caseName);
        var golden = ParityCases.Golden(caseName);
        await ParityApis.ResetBothAsync();

        var (legacy, dotnet) = await ParityApis.SendToBothAsync(parityCase.Method, parityCase.Path, parityCase.Body);

        Assert.Equal(golden.Status, legacy.Status);
        Assert.Equal(golden.Status, dotnet.Status);

        foreach (var header in parityCase.ComparedHeaders)
        {
            Assert.Equal(golden.Headers![header], legacy.Header(header));
            Assert.Equal(golden.Headers![header], dotnet.Header(header));
        }

        if (golden.Body is null || golden.Body.Value.ValueKind == System.Text.Json.JsonValueKind.Null)
        {
            Assert.Equal(string.Empty, legacy.Body);
            Assert.Equal(string.Empty, dotnet.Body);
            return;
        }

        Assert.Equal(ParityApis.NormalizeContentType(legacy.ContentType), ParityApis.NormalizeContentType(dotnet.ContentType));
        Assert.Equal("application/json;charset=utf-8", ParityApis.NormalizeContentType(dotnet.ContentType));

        var legacyJson = JsonParity.NormalizeVolatile(legacy.Json, parityCase.VolatileFields);
        var dotnetJson = JsonParity.NormalizeVolatile(dotnet.Json, parityCase.VolatileFields);

        Assert.Empty(JsonParity.Diff(legacyJson, dotnetJson));
        Assert.Empty(JsonParity.Diff(golden.Body.Value, legacyJson));
        Assert.Empty(JsonParity.Diff(golden.Body.Value, dotnetJson));
    }

    [Fact]
    public void Every_case_has_a_golden_file()
    {
        Assert.All(ParityCases.All, c => Assert.True(File.Exists(ParityCases.GoldenPath(c.Name)), $"Missing golden file for {c.Name}"));
        Assert.Equal(ParityCases.All.Count, ParityCases.All.Select(c => c.Name).Distinct().Count());
    }
}
