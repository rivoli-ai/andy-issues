// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Security.Cryptography;
using Andy.Issues.Application.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace Andy.Issues.Infrastructure.Services;

public sealed class LlmSecretStore(IDataProtectionProvider protection, ISecretStore legacy) : ILlmSecretStore
{
    public const string Prefix = "protected::llm:v1:";
    private readonly IDataProtector _protector = protection.CreateProtector("Andy.Issues.LlmCredential.v1");

    public Task<string> StoreAsync(string key, string value, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(string.IsNullOrEmpty(value) ? value : Prefix + _protector.Protect(value));
    }

    public async Task<string?> ResolveAsync(string? valueOrRef, CancellationToken ct = default)
    {
        if (valueOrRef?.StartsWith(Prefix, StringComparison.Ordinal) == true)
        {
            try { valueOrRef = _protector.Unprotect(valueOrRef[Prefix.Length..]); }
            catch (CryptographicException) { return null; }
        }
        return await legacy.ResolveAsync(valueOrRef, ct);
    }
}
