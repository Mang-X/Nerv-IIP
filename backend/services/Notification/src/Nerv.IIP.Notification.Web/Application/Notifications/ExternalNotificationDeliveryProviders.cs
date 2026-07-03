using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using Nerv.IIP.Notification.Infrastructure;

namespace Nerv.IIP.Notification.Web.Application.Notifications;

internal sealed class WeComDeliveryProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration) : INotificationDeliveryProvider
{
    public string Channel => NotificationDeliveryChannels.WeCom;

    public async Task<NotificationDeliveryProviderResult> SendAsync(NotificationDeliveryRequest request, CancellationToken cancellationToken)
    {
        var section = configuration.GetSection("Notification:Delivery:Providers:WeCom");
        var accessToken = section["AccessToken"];
        var agentId = section["AgentId"];
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(agentId))
        {
            return NotificationDeliveryProviderResult.Failed("WeCom provider is not configured.");
        }

        var baseUrl = section["BaseUrl"] ?? "https://qyapi.weixin.qq.com";
        var client = httpClientFactory.CreateClient(nameof(WeComDeliveryProvider));
        var response = await client.PostAsJsonAsync(
            $"{baseUrl.TrimEnd('/')}/cgi-bin/message/send?access_token={Uri.EscapeDataString(accessToken)}",
            new
            {
                touser = request.RecipientAddress,
                msgtype = "text",
                agentid = agentId,
                text = new
                {
                    content = $"{request.Title}\n{request.Summary}",
                },
            },
            cancellationToken);

        return await ProviderResponseReader.ReadJsonProviderResultAsync(response, "errcode", "errmsg", cancellationToken);
    }
}

internal sealed class DingTalkDeliveryProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration) : INotificationDeliveryProvider
{
    public string Channel => NotificationDeliveryChannels.DingTalk;

    public async Task<NotificationDeliveryProviderResult> SendAsync(NotificationDeliveryRequest request, CancellationToken cancellationToken)
    {
        var section = configuration.GetSection("Notification:Delivery:Providers:DingTalk");
        var accessToken = section["AccessToken"];
        var agentId = section["AgentId"];
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(agentId))
        {
            return NotificationDeliveryProviderResult.Failed("DingTalk provider is not configured.");
        }

        var baseUrl = section["BaseUrl"] ?? "https://oapi.dingtalk.com";
        var client = httpClientFactory.CreateClient(nameof(DingTalkDeliveryProvider));
        var response = await client.PostAsJsonAsync(
            $"{baseUrl.TrimEnd('/')}/topapi/message/corpconversation/asyncsend_v2?access_token={Uri.EscapeDataString(accessToken)}",
            new
            {
                agent_id = agentId,
                userid_list = request.RecipientAddress,
                msg = new
                {
                    msgtype = "text",
                    text = new
                    {
                        content = $"{request.Title}\n{request.Summary}",
                    },
                },
            },
            cancellationToken);

        return await ProviderResponseReader.ReadJsonProviderResultAsync(response, "errcode", "errmsg", cancellationToken);
    }
}

internal sealed class SmtpEmailDeliveryProvider(IConfiguration configuration) : INotificationDeliveryProvider
{
    public string Channel => NotificationDeliveryChannels.Email;

    public async Task<NotificationDeliveryProviderResult> SendAsync(NotificationDeliveryRequest request, CancellationToken cancellationToken)
    {
        var section = configuration.GetSection("Notification:Delivery:Providers:Smtp");
        var host = section["Host"];
        var from = section["From"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
        {
            return NotificationDeliveryProviderResult.Failed("SMTP provider is not configured.");
        }

        using var client = new SmtpClient(host, section.GetValue("Port", 25))
        {
            EnableSsl = section.GetValue("EnableSsl", false),
        };
        var userName = section["UserName"];
        var password = section["Password"];
        if (!string.IsNullOrWhiteSpace(userName) && !string.IsNullOrWhiteSpace(password))
        {
            client.Credentials = new NetworkCredential(userName, password);
        }

        using var message = new MailMessage(from, request.RecipientAddress, request.Title, request.Summary);
        await client.SendMailAsync(message, cancellationToken);
        return NotificationDeliveryProviderResult.Succeeded();
    }
}

internal sealed class WebhookDeliveryProvider(IHttpClientFactory httpClientFactory) : INotificationDeliveryProvider
{
    public string Channel => NotificationDeliveryChannels.Webhook;

    public async Task<NotificationDeliveryProviderResult> SendAsync(NotificationDeliveryRequest request, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.RecipientAddress, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return NotificationDeliveryProviderResult.Failed("Webhook recipient address must be an absolute HTTP(S) URL.");
        }

        var client = httpClientFactory.CreateClient(nameof(WebhookDeliveryProvider));
        var response = await client.PostAsJsonAsync(uri, new
        {
            organizationId = request.OrganizationId,
            environmentId = request.EnvironmentId,
            notificationType = request.NotificationType,
            severity = request.Severity,
            recipientRef = request.RecipientRef,
            title = request.Title,
            summary = request.Summary,
            resource = request.ResourceType is null ? null : new
            {
                resourceType = request.ResourceType,
                resourceId = request.ResourceId,
                fileId = request.FileId,
            },
        }, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return NotificationDeliveryProviderResult.Succeeded(response.Headers.TryGetValues("X-Notification-Message-Id", out var values)
                ? values.FirstOrDefault()
                : null);
        }

        return NotificationDeliveryProviderResult.Failed($"Webhook delivery failed with HTTP {(int)response.StatusCode}.");
    }
}

file static class ProviderResponseReader
{
    public static async Task<NotificationDeliveryProviderResult> ReadJsonProviderResultAsync(
        HttpResponseMessage response,
        string codeProperty,
        string messageProperty,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            return NotificationDeliveryProviderResult.Failed($"Provider returned HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var code = root.TryGetProperty(codeProperty, out var codeElement) ? codeElement.GetInt32() : 0;
        if (code == 0)
        {
            var messageId = root.TryGetProperty("task_id", out var taskId) ? taskId.ToString() : null;
            return NotificationDeliveryProviderResult.Succeeded(messageId);
        }

        var message = root.TryGetProperty(messageProperty, out var messageElement)
            ? messageElement.ToString()
            : $"Provider returned error code {code}.";
        return NotificationDeliveryProviderResult.Failed(message);
    }
}
