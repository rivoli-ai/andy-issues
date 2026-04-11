// Copyright (c) Rivoli AI 2026. All rights reserved.
// Example: Using the Andy Issues API from C#

using System.Net.Http.Headers;

var apiUrl = "https://localhost:5410";
var token = "YOUR_BEARER_TOKEN"; // Obtain from Andy Auth

using var handler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
};
using var client = new HttpClient(handler) { BaseAddress = new Uri(apiUrl) };
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

// Example: check service health. Feature endpoints will be added per-epic as the
// andy-issues domain is built out (repositories, backlog, sandboxes, ...).
var health = await client.GetStringAsync("/health");
Console.WriteLine(health);
