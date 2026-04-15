// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Api.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

public class UnauthorizedExceptionFilterTests
{
    [Fact]
    public void OnException_TranslatesUnauthorizedAccessTo401()
    {
        var filter = new UnauthorizedExceptionFilter();
        var ctx = BuildContext(new UnauthorizedAccessException("no sub claim"));

        filter.OnException(ctx);

        Assert.True(ctx.ExceptionHandled);
        var result = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
    }

    [Fact]
    public void OnException_LeavesOtherExceptionsAlone()
    {
        var filter = new UnauthorizedExceptionFilter();
        var ctx = BuildContext(new InvalidOperationException("something else"));

        filter.OnException(ctx);

        Assert.False(ctx.ExceptionHandled);
        Assert.Null(ctx.Result);
    }

    private static ExceptionContext BuildContext(Exception ex)
    {
        var actionContext = new ActionContext(
            new DefaultHttpContext(),
            new RouteData(),
            new ActionDescriptor());
        return new ExceptionContext(actionContext, new List<IFilterMetadata>())
        {
            Exception = ex
        };
    }
}
