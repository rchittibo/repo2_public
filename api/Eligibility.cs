using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;

public class Eligibility
{
    private readonly ILogger _logger;

    public Eligibility(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<Eligibility>();
    }

    [Function("Eligibility")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "eligibility")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var memberId = query["memberId"];
        var dateText = query["date"];

        var resp = req.CreateResponse(HttpStatusCode.BadRequest);

        if (string.IsNullOrWhiteSpace(memberId) || string.IsNullOrWhiteSpace(dateText) || !DateTime.TryParse(dateText, out var asOfDate))
        {
            await resp.WriteAsJsonAsync(new { error = "Provide memberId and date (YYYY-MM-DD)." });
            return resp;
        }

        var connStr = Environment.GetEnvironmentVariable("SqlConnectionString");
        if (string.IsNullOrWhiteSpace(connStr))
        {
            var r = req.CreateResponse(HttpStatusCode.InternalServerError);
            await r.WriteAsJsonAsync(new { error = "SqlConnectionString not configured." });
            return r;
        }

        const string sql = @"
SELECT TOP 1 PlanCode
FROM dbo.Eligibility
WHERE MemberId = @MemberId
  AND @AsOfDate BETWEEN CoverageStart AND CoverageEnd
ORDER BY CoverageStart DESC;";

        string? planCode = null;

        await using (var conn = new SqlConnection(connStr))
        {
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@MemberId", memberId);
            cmd.Parameters.AddWithValue("@AsOfDate", asOfDate.Date);

            var result = await cmd.ExecuteScalarAsync();
            if (result != null && result != DBNull.Value)
                planCode = (string)result;
        }

        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteAsJsonAsync(new
        {
            memberId,
            date = asOfDate.ToString("yyyy-MM-dd"),
            eligible = planCode != null,
            planCode
        });

        return ok;
    }
}
