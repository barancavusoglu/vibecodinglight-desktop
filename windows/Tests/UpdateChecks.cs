using AiLight.App;
using System.Net;
using System.Text;

static class UpdateChecks {
    public static async Task Run(Action<bool, string> check) {
        var installed = new Version(0, 1, 46, 0);
        check(UpdateService.Parse("{\"version\":\"0.1.47\"}", installed)?.Version == "0.1.47", "New Windows release offered");
        foreach (var version in new[] { "0.1.46", "0.1.46.0", "0.1.45", "0.1" })
            check(UpdateService.Parse($$"""{"Version":"{{version}}"}""", installed) is null, "Same or older release not offered: " + version);
        check(UpdateService.Parse("{\"version\":\"0.1.47\",\"url\":\"https://untrusted.invalid/run.exe\"}", installed)?.Version == "0.1.47"
            && UpdateService.DownloadPageUrl == "https://www.vibecodinglight.com/#setup", "Remote metadata cannot change download destination");
        foreach (var text in new[] { "null", "{}", "[]", "broken", "{\"version\":null}", "{\"version\":47}", "{\"version\":\"0.1.47-beta\"}", new string('x', 32769) }) {
            bool rejected = false;
            try { UpdateService.Parse(text, installed); } catch (System.IO.InvalidDataException) { rejected = true; }
            check(rejected, "Malformed release metadata rejected");
        }
        using var handler = new MetadataHandler();
        using var client = new HttpClient(handler);
        check((await UpdateService.Check(client, installed))?.Version == "0.1.47" && handler.Requests == 1, "Update check fetches only official release metadata");
        handler.Status = HttpStatusCode.NotFound;
        bool unpublished = false;
        try { await UpdateService.Check(client, installed); } catch (InvalidOperationException ex) { unpublished = ex.Message.Contains("henüz yayımlanmış"); }
        check(unpublished, "Missing release reported as unpublished, not up to date");
        handler.Status = HttpStatusCode.InternalServerError;
        bool failed = false;
        try { await UpdateService.Check(client, installed); } catch (HttpRequestException) { failed = true; }
        check(failed, "Server failure is not reported as up to date");
    }
    private sealed class MetadataHandler : HttpMessageHandler {
        public int Requests;
        public HttpStatusCode Status = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            if (request.Method != HttpMethod.Get || request.RequestUri?.AbsoluteUri != UpdateService.ManifestUrl)
                throw new Exception("Unexpected update request");
            Requests++;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent("{\"version\":\"0.1.47\"}", Encoding.UTF8, "application/json") });
        }
    }
}
