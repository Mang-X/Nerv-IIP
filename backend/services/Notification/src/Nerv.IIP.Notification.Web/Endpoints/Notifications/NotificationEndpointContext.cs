using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Web.Endpoints.Notifications;

internal static class NotificationEndpointContext
{
    public static string RequiredHeader(HttpContext context, string name)
    {
        var value = context.Request.Headers[name].ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new KnownException($"{name} header is required.");
        }

        return value;
    }
}
