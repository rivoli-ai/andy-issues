// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Andy.Issues.Api.Auth;

// Translates UnauthorizedAccessException thrown from controllers (see
// UserIdExtensions.RequireUserId) into a 401 Unauthorized response
// with a clean JSON error body. Without this the default pipeline
// would emit a 500 — correct status for "something broke", wrong
// status for "who are you?".
public sealed class UnauthorizedExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is UnauthorizedAccessException ex)
        {
            context.Result = new ObjectResult(new { error = ex.Message })
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
            context.ExceptionHandled = true;
        }
    }
}
