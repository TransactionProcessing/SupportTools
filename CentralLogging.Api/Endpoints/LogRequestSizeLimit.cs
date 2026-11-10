using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace CentralLogging.Api.Endpoints;

public static class LogRequestSizeLimit
{
    private const Int64 MultipartFramingAllowance = 64 * 1024;

    public static void Apply(HttpContext context, LogIngestionOptions options)
    {
        if (!HttpMethods.IsPost(context.Request.Method) || !context.Request.Path.StartsWithSegments("/api/logs"))
            return;

        IHttpMaxRequestBodySizeFeature? sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is not { IsReadOnly: false })
            return;

        Int64 maximumBytes = context.Request.Path.StartsWithSegments("/api/logs/upload")
            ? options.MaximumUploadBytes + MultipartFramingAllowance
            : options.MaximumRequestBytes;
        sizeFeature.MaxRequestBodySize = maximumBytes;
    }
}
