using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using kiota.Rpc;
using Kiota.Builder;
using Kiota.Builder.Configuration;
using Microsoft.OpenApi;
using Xunit;

namespace Kiota.Builder.Tests.OpenApiExtensions;

public sealed class OpenApiDocumentDownloadServiceTests : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private const string DocumentContentWithNoServer = @"openapi: 3.0.0
info:
  title: Graph Users
  version: 0.0.0
tags: []
paths:
  /users:
    get:
      operationId: getUsers
      parameters: []
      responses:
        '200':
          description: The request has succeeded.
          content:
            application/json:
              schema:
                type: object";

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    [Fact]
    public async Task GetDocumentFromStreamAsyncTest_IncludeKiotaValidationRulesInConfig()
    {
        var generationConfig = new GenerationConfiguration
        {
            PluginTypes = [PluginType.APIPlugin],
            IncludeKiotaValidationRules = true
        };
        var fakeLogger = new FakeLogger<OpenApiDocumentDownloadService>();

        using var inputDocumentStream = CreateMemoryStreamFromString(DocumentContentWithNoServer);
        var documentDownloadService = new OpenApiDocumentDownloadService(_httpClient, fakeLogger);
        var document = await documentDownloadService.GetDocumentFromStreamAsync(inputDocumentStream, generationConfig, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(document);
        //There should be a log entry for the no server rule
        var logEntryForNoServerRule = fakeLogger.LogEntries
            .Where(l => l.message.StartsWith("OpenAPI warning: #/ - A servers entry (v3) or host + basePath + schemes properties (v2) was not present in the OpenAPI description"));
        Assert.Single(logEntryForNoServerRule);
    }

    [Fact]
    public async Task GetDocumentFromStreamAsyncTest_No_IncludeKiotaValidationRulesInConfig()
    {
        var generationConfig = new GenerationConfiguration
        {
            PluginTypes = [PluginType.APIPlugin],
            IncludeKiotaValidationRules = false
        };
        var fakeLogger = new FakeLogger<OpenApiDocumentDownloadService>();

        using var inputDocumentStream = CreateMemoryStreamFromString(DocumentContentWithNoServer);
        var documentDownloadService = new OpenApiDocumentDownloadService(_httpClient, fakeLogger);
        var document = await documentDownloadService.GetDocumentFromStreamAsync(inputDocumentStream, generationConfig, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(document);
        //There should be no log entry for the no server rule
        var logEntryForNoServerRule = fakeLogger.LogEntries
            .Where(l => l.message.StartsWith("OpenAPI warning: #/ - A servers entry (v3) or host + basePath + schemes properties (v2) was not present in the OpenAPI description"));
        Assert.Empty(logEntryForNoServerRule);
    }

    [Fact]
    public async Task GetDocumentFromStreamAsync_LogsSpecificationPathWhenParsingThrows()
    {
        const string brokenDocument = """
{
  "openapi": "3.0.1",
  "info": {
    "title": "Repro API",
    "version": "1.0.0"
  },
  "paths": {},
  "components": {
    "schemas": {
      "ItemStatus": {
        "type": "string",
        "enum": [
          "Active",
          "Archived"
        ],
        "x-ms-enum-flags": []
      }
    }
  }
}
""";

        var generationConfig = new GenerationConfiguration
        {
            OpenAPIFilePath = "repro-broken.json"
        };
        var fakeLogger = new FakeLogger<OpenApiDocumentDownloadService>();

        using var inputDocumentStream = CreateMemoryStreamFromString(brokenDocument);
        var documentDownloadService = new OpenApiDocumentDownloadService(_httpClient, fakeLogger);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            documentDownloadService.GetDocumentFromStreamAsync(inputDocumentStream, generationConfig, cancellationToken: TestContext.Current.CancellationToken));

        var parsingLogEntry = fakeLogger.LogEntries
            .Where(l => l.message.Contains("Error parsing specification", StringComparison.OrdinalIgnoreCase));

        var logEntry = Assert.Single(parsingLogEntry);
        Assert.Contains("repro-broken.json", logEntry.message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDocumentFromStreamAsyncTest_Default_IncludeKiotaValidationRulesInConfig()
    {
        var generationConfig = new GenerationConfiguration
        {
            PluginTypes = [PluginType.APIPlugin],
        };
        var fakeLogger = new FakeLogger<OpenApiDocumentDownloadService>();

        using var inputDocumentStream = CreateMemoryStreamFromString(DocumentContentWithNoServer);
        var documentDownloadService = new OpenApiDocumentDownloadService(_httpClient, fakeLogger);
        var document = await documentDownloadService.GetDocumentFromStreamAsync(inputDocumentStream, generationConfig, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(document);
        //There should be no log entry for the no server rule
        var logEntryForNoServerRule = fakeLogger.LogEntries
            .Where(l => l.message.StartsWith("OpenAPI warning: #/ - A servers entry (v3) or host + basePath + schemes properties (v2) was not present in the OpenAPI description"));
        Assert.Empty(logEntryForNoServerRule);
    }

    [Fact]
    public async Task DoesNotLoadExternalReferencesByDefault()
    {
        var generationConfig = new GenerationConfiguration
        {
            OpenAPIFilePath = "https://example.com/openapi.yaml",
        };
        var fakeLogger = new FakeLogger<OpenApiDocumentDownloadService>();

        using var inputDocumentStream = CreateMemoryStreamFromString("""
openapi: 3.0.0
info:
  title: External refs
  version: 0.0.0
paths: {}
components:
  schemas:
    Pet:
      $ref: 'https://contoso.com/schemas/pet.yaml#/components/schemas/Pet'
""");
        var documentDownloadService = new OpenApiDocumentDownloadService(_httpClient, fakeLogger);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            documentDownloadService.GetDocumentFromStreamAsync(inputDocumentStream, generationConfig, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AllowedExternalOriginsLoadMatchingReferences()
    {
        var generationConfig = new GenerationConfiguration
        {
            OpenAPIFilePath = "https://example.com/openapi.yaml",
            AllowedExternalOrigins = ["https://contoso.com/schemas/*"],
        };
        var fakeLogger = new FakeLogger<OpenApiDocumentDownloadService>();

        using var inputDocumentStream = CreateMemoryStreamFromString("""
openapi: 3.0.0
info:
  title: External refs
  version: 0.0.0
paths: {}
components:
  schemas:
    Pet:
      $ref: 'https://contoso.com/schemas/pet.yaml#/components/schemas/Pet'
""");
        using var httpClient = new HttpClient(new ResponseHandler());
        var documentDownloadService = new OpenApiDocumentDownloadService(httpClient, fakeLogger);

        var document = await documentDownloadService.GetDocumentFromStreamAsync(inputDocumentStream, generationConfig, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(document);
    }

    [Fact]
    public async Task AllowedExternalOriginsStreamLoaderImplementsOpenApiLoader()
    {
        using var httpClient = new HttpClient(new ResponseHandler());
        var loader = (IStreamLoader)new AllowedExternalOriginsStreamLoader(httpClient, ["https://contoso.com/schemas/*"]);

        await using var stream = await loader.LoadAsync(
            new Uri("https://example.com/openapi.yaml"),
            new Uri("https://contoso.com/schemas/pet.yaml"),
            TestContext.Current.CancellationToken);
        Assert.NotNull(stream);
    }

    [Theory]
    // a wildcard placed before the path must not consume the delimiters that end the authority, otherwise it
    // completes the match with text taken from a later URI component and hosts outside the intended set are allowed.
    [InlineData("https://*.contoso.com/*", "https://evil.attacker.com/x/.contoso.com/y.json")]
    [InlineData("https://*.contoso.com/*", "https://evil.attacker.com/x/.contoso.com/")]
    [InlineData("http://*.contoso.com/*", "http://127.0.0.1:8080/x/.contoso.com/y")]
    [InlineData("http://*.contoso.com/*", "http://169.254.169.254/x/.contoso.com/")]
    [InlineData("https://contoso.com/schemas/*", "https://evil.attacker.com/https://contoso.com/schemas/pet.yaml")]
    // a wildcard scheme must not reach into the path either
    [InlineData("*://contoso.com/*", "https://evil.attacker.com/x://contoso.com/y")]
    // credentials in the authority must not disguise the real host
    [InlineData("https://*.contoso.com/*", "https://zap.contoso.com@evil.attacker.com/schemas/pet.yaml")]
    // the wildcard must not cross the scheme or the port
    [InlineData("https://*.contoso.com/*", "http://zap.contoso.com/schemas/pet.yaml")]
    [InlineData("https://*.contoso.com/*", "https://zap.contoso.com:8443/schemas/pet.yaml")]
    // the authority is delimited the same way whatever the case of the scheme
    [InlineData("HTTPS://*.contoso.com/*", "https://evil.attacker.com/x/.contoso.com/y.json")]
    // a host pattern without a scheme is not a URI pattern and matches nothing
    [InlineData("*.contoso.com", "https://zap.contoso.com/schemas/pet.yaml")]
    [InlineData("*.contoso.com", "https://evil.attacker.com/x/.contoso.com")]
    public async Task AllowedExternalOriginsStreamLoaderRejectsAuthorityBypass(string allowedOrigin, string externalReference)
    {
        using var httpClient = new HttpClient(new ResponseHandler());
        var loader = (IStreamLoader)new AllowedExternalOriginsStreamLoader(httpClient, [allowedOrigin]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => loader.LoadAsync(
            new Uri("https://example.com/openapi.yaml"),
            new Uri(externalReference),
            TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("https://*.contoso.com/*", "https://zap.contoso.com/schemas/pet.yaml")]
    [InlineData("https://*.contoso.com/*", "https://zap.nested.contoso.com/schemas/pet.yaml")]
    [InlineData("HTTPS://*.contoso.com/*", "https://zap.contoso.com/schemas/pet.yaml")]
    [InlineData("https://*/schemas/*", "https://anything.example.com/schemas/pet.yaml")]
    [InlineData("https://contoso.com/schemas/*", "https://contoso.com/schemas/pet.yaml")]
    [InlineData("https://contoso.com/schemas/*", "https://contoso.com/schemas/nested/pet.yaml")]
    [InlineData("https://contoso.com:8443/schemas/*", "https://contoso.com:8443/schemas/pet.yaml")]
    // wildcards from the path onwards keep matching any character, the destination is already pinned by then
    [InlineData("https://*.contoso.com/schemas/pet.yaml?version=*", "https://zap.contoso.com/schemas/pet.yaml?version=2")]
    // a query or a fragment can follow the authority without a path, and its wildcard is not bounded either
    [InlineData("https://contoso.com?next=*", "https://contoso.com?next=schemas/pet.yaml")]
    [InlineData("https://*.contoso.com#*", "https://zap.contoso.com#schemas/pet.yaml")]
    [InlineData("https://user1@contoso.com/schemas/*", "https://user1@contoso.com/schemas/pet.yaml")]
    public async Task AllowedExternalOriginsStreamLoaderAllowsMatchingOrigins(string allowedOrigin, string externalReference)
    {
        using var httpClient = new HttpClient(new ResponseHandler());
        var loader = (IStreamLoader)new AllowedExternalOriginsStreamLoader(httpClient, [allowedOrigin]);

        await using var stream = await loader.LoadAsync(
            new Uri("https://example.com/openapi.yaml"),
            new Uri(externalReference),
            TestContext.Current.CancellationToken);

        Assert.NotNull(stream);
    }

    [Fact]
    public async Task AllowedExternalOriginsWildcardAllowsAnyExternalReference()
    {
        using var httpClient = new HttpClient(new ResponseHandler());
        var loader = (IStreamLoader)new AllowedExternalOriginsStreamLoader(httpClient, ["*"]);

        await using var stream = await loader.LoadAsync(
            new Uri("https://example.com/openapi.yaml"),
            new Uri("https://contoso.com/schemas/pet.yaml"),
            TestContext.Current.CancellationToken);

        Assert.NotNull(stream);
    }

    [Fact]
    public async Task AllowedExternalOriginsStreamLoaderAllowsFullLocalPaths()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var schemaPath = Path.Combine(tempDirectory, "schemas", "pet.yaml");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(schemaPath)!);
            await File.WriteAllTextAsync(schemaPath, "type: object", TestContext.Current.CancellationToken);
            using var httpClient = new HttpClient(new ResponseHandler());
            var loader = (IStreamLoader)new AllowedExternalOriginsStreamLoader(httpClient, [schemaPath]);

            await using var stream = await loader.LoadAsync(
                new Uri(Path.Combine(tempDirectory, "openapi.yaml")),
                new Uri(schemaPath),
                TestContext.Current.CancellationToken);

            Assert.NotNull(stream);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, true);
        }
    }

    [Fact]
    public async Task AllowedExternalOriginsStreamLoaderAllowsWildcardFileUris()
    {
        var tempDirectory = Path.Join(Path.GetTempPath(), Path.GetRandomFileName());
        var schemaPath = Path.Join(tempDirectory, "schemas", "pet.yaml");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(schemaPath)!);
            await File.WriteAllTextAsync(schemaPath, "type: object", TestContext.Current.CancellationToken);
            using var httpClient = new HttpClient(new ResponseHandler());
            var allowedOrigin = new Uri(Path.Join(tempDirectory, "schemas", "*")).AbsoluteUri;
            var loader = (IStreamLoader)new AllowedExternalOriginsStreamLoader(httpClient, [allowedOrigin]);

            await using var stream = await loader.LoadAsync(
                new Uri(Path.Join(tempDirectory, "openapi.yaml")),
                new Uri(schemaPath),
                TestContext.Current.CancellationToken);

            Assert.NotNull(stream);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, true);
        }
    }

    [Fact]
    public async Task AllowedExternalOriginsStreamLoaderAllowsRelativePathPatterns()
    {
        var relativeDirectory = Path.GetRandomFileName();
        var schemaPath = Path.Combine(Directory.GetCurrentDirectory(), relativeDirectory, "schemas", "pet.yaml");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(schemaPath)!);
            await File.WriteAllTextAsync(schemaPath, "type: object", TestContext.Current.CancellationToken);
            using var httpClient = new HttpClient(new ResponseHandler());
            var allowedOrigin = Path.Combine(relativeDirectory, "schemas", "*");
            var loader = (IStreamLoader)new AllowedExternalOriginsStreamLoader(httpClient, [allowedOrigin]);

            await using var stream = await loader.LoadAsync(
                new Uri(Path.Combine(Directory.GetCurrentDirectory(), "openapi.yaml")),
                new Uri($"{relativeDirectory}/schemas/pet.yaml", UriKind.Relative),
                TestContext.Current.CancellationToken);

            Assert.NotNull(stream);
        }
        finally
        {
            var tempDirectory = Path.Combine(Directory.GetCurrentDirectory(), relativeDirectory);
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, true);
        }
    }

    [Fact]
    public async Task GetDocumentFromStreamAsyncSupportsLargeYamlScalars()
    {
        var largeDescription = new string('a', 100000);
        var generationConfig = new GenerationConfiguration();
        var fakeLogger = new FakeLogger<OpenApiDocumentDownloadService>();

        using var inputDocumentStream = CreateMemoryStreamFromString($$"""
openapi: 3.0.0
info:
  title: Large scalars
  version: 0.0.0
  description: {{largeDescription}}
paths: {}
""");
        var documentDownloadService = new OpenApiDocumentDownloadService(_httpClient, fakeLogger);
        var document = await documentDownloadService.GetDocumentFromStreamAsync(inputDocumentStream, generationConfig, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(document);
        Assert.Equal(largeDescription, document.Info?.Description);
    }

    [Theory]
    [InlineData("absolute", "pet.yaml")]
    [InlineData("relative", "pet.yaml")]
    [InlineData("uri", "pet.yaml")]
    [InlineData("absolute", "schemas/pet.yaml")]
    [InlineData("relative", "schemas/pet.yaml")]
    [InlineData("uri", "schemas/pet.yaml")]
    public async Task ResolvesLocalReferencesRelativeToDocument(string pathKind, string referencePath)
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "TestData", "specification with spaces");
        var documentDirectory = referencePath.StartsWith("schemas/", StringComparison.Ordinal) ? fixtureDirectory : Path.Combine(fixtureDirectory, "schemas");
        var documentPath = Path.Combine(documentDirectory, "openapi.yaml");
        var schemaPath = Path.Combine(fixtureDirectory, "schemas", "pet.yaml");
        var generationConfig = new GenerationConfiguration
        {
            OpenAPIFilePath = pathKind switch
            {
                "relative" => Path.GetRelativePath(Directory.GetCurrentDirectory(), documentPath),
                "uri" => new Uri(documentPath).AbsoluteUri,
                _ => documentPath,
            },
            AllowedExternalOrigins = [schemaPath],
        };
        using var inputDocumentStream = CreateMemoryStreamFromString(CreateDocumentWithExternalReference($"./{referencePath}"));
        var service = new OpenApiDocumentDownloadService(_httpClient, new FakeLogger<OpenApiDocumentDownloadService>());

        var document = await service.GetDocumentFromStreamAsync(inputDocumentStream, generationConfig, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(JsonSchemaType.String, document?.Paths["/pets"].Operations?[HttpMethod.Get].Responses?["200"].Content?["application/json"].Schema?.Properties?["name"].Type);
    }

    [Theory]
    [InlineData("https://example.com/specs/openapi.yaml", "pet.yaml", "https://example.com/specs/pet.yaml")]
    [InlineData("https://example.com/specs/openapi.yaml?version=1", "schemas/pet.yaml", "https://example.com/specs/schemas/pet.yaml")]
    [InlineData("https://example.com/specs/openapi.yaml", "../pet.yaml", "https://example.com/pet.yaml")]
    public async Task ResolvesRemoteReferencesRelativeToDocument(string documentPath, string referencePath, string expectedUri)
    {
        var generationConfig = new GenerationConfiguration
        {
            OpenAPIFilePath = documentPath,
            AllowedExternalOrigins = [expectedUri],
        };
        using var httpClient = new HttpClient(new RelativeReferenceResponseHandler(expectedUri));
        using var inputDocumentStream = CreateMemoryStreamFromString(CreateDocumentWithExternalReference(referencePath));
        var service = new OpenApiDocumentDownloadService(httpClient, new FakeLogger<OpenApiDocumentDownloadService>());

        var document = await service.GetDocumentFromStreamAsync(inputDocumentStream, generationConfig, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(JsonSchemaType.String, document?.Paths["/pets"].Operations?[HttpMethod.Get].Responses?["200"].Content?["application/json"].Schema?.Properties?["name"].Type);
    }

    [Fact]
    public async Task RelativeReferencesStillRequireAllowedExternalOrigins()
    {
        var generationConfig = new GenerationConfiguration
        {
            OpenAPIFilePath = "https://example.com/specs/openapi.yaml",
            AllowedExternalOrigins = ["https://example.com/specs/allowed.yaml"],
        };
        using var inputDocumentStream = CreateMemoryStreamFromString(CreateDocumentWithExternalReference("pet.yaml"));
        var service = new OpenApiDocumentDownloadService(_httpClient, new FakeLogger<OpenApiDocumentDownloadService>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetDocumentFromStreamAsync(inputDocumentStream, generationConfig, cancellationToken: TestContext.Current.CancellationToken));
    }

    private static string CreateDocumentWithExternalReference(string referencePath) => $$"""
openapi: 3.0.0
info:
  title: Relative references
  version: 1.0.0
paths:
  /pets:
    get:
      responses:
        '200':
          description: A pet
          content:
            application/json:
              schema:
                $ref: '{{referencePath}}#/components/schemas/Pet'
""";

    private const string ExternalPetDocument = """
openapi: 3.0.0
info:
  title: Pet components
  version: 1.0.0
paths: {}
components:
  schemas:
    Pet:
      type: object
      properties:
        name:
          type: string
""";

    private sealed class RelativeReferenceResponseHandler(string expectedUri) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(expectedUri, request.RequestUri?.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ExternalPetDocument),
            });
        }
    }

    private static Stream CreateMemoryStreamFromString(string s)
    {
        var stream = new MemoryStream();
        var writer = new StreamWriter(stream);
        writer.Write(s);
        writer.Flush();
        stream.Position = 0;
        return stream;
    }

    private sealed class ResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
openapi: 3.0.0
info:
  title: External ref
  version: 0.0.0
paths: {}
components:
  schemas:
    Pet:
      type: object
"""),
            });
        }
    }
}
