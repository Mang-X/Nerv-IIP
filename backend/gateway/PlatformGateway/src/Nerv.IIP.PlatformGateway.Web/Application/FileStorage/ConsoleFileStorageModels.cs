using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;

namespace Nerv.IIP.PlatformGateway.Web.Application.FileStorage;

internal static class ConsoleFileStorageTransferRoutes
{
    public const string DownstreamTusPrefix = "/api/files/v1/tus/";
    public const string ConsoleTusPrefix = "/api/console/v1/files/tus/";
    public const string DownstreamDownloadGrantPrefix = "/api/files/v1/download-grants/";
}

/// <summary>
/// #3314 第 2 轮审核 E1 的结构性替代。
///
/// 前两轮的护栏都写在**契约形状**上（先禁参数拼写、再禁路径前缀与参数枚举），连续被三种形状
/// 打穿：换个参数名、把标识改走 query、把路由挂到扫描前缀之外——每一种都让「调用方携带
/// grant id 并据此兑换」原样复活。按本仓判据，连续多轮点名同类特例时应换结构性替代，
/// 而不是加第四条谓词。
///
/// 本类型就是那个替代：**代理一跳的目标地址不再是 <see cref="string"/>**。
/// 构造函数私有，对外只有两个工厂：
///
/// - <see cref="Tus"/>：把 uploadSessionId 嵌进固定模板，调用方给的字符串只能落在参数位上；
/// - <see cref="FromSignedGrant"/>：**入参是 FileStorage 的签发响应，不是任何调用方标识**。
///   它校验下游给的路径确实落在 download-grant 兑换前缀内，然后原样承载。
///
/// 于是「拿调用方传来的 grant id 去代理兑换面」这句话**写不出来**：没有任何 API 接受一个
/// URL 字符串或一个 grant 标识来代理——无论那个标识走 path 还是 query、路由叫什么名字、
/// 挂在哪个前缀下。
///
/// **本装置不自称完备**：在本文件里新增第三个工厂、或伪造一个 <see cref="DownloadGrantResponse"/>
/// 再喂给 <see cref="FromSignedGrant"/>，仍可绕过。区别在于暴露面从「任意一处的任意字符串」
/// 收缩成「这一个类型上的工厂集合」——那是一次显式的、评审看得见的编辑。
/// </summary>
internal readonly struct FileStorageDownstreamAddress
{
    private FileStorageDownstreamAddress(string path) => Path = path;

    public string Path { get; }

    public static FileStorageDownstreamAddress Tus(string uploadSessionId) =>
        new(ConsoleFileStorageTransferRoutes.DownstreamTusPrefix + Uri.EscapeDataString(uploadSessionId));

    /// <summary>
    /// 由**本网关刚刚签发**的 download grant 产出取字节地址。
    ///
    /// FileStorage 只应回内部相对路径；绝对 URL、协议相对 URL 与前缀不符都在这里失败关闭
    /// （ADR 0023 决策 1.3、ADR 0030 决策 1）。前缀以 <c>/</c> 开头，所以 <c>https://…</c> 与
    /// <c>//host/…</c> 都不可能通过，不需要另写一条「是否外部地址」的析取项。
    /// </summary>
    public static FileStorageDownstreamAddress FromSignedGrant(DownloadGrantResponse grant)
    {
        var url = grant.Download.Url;
        if (!url.StartsWith(ConsoleFileStorageTransferRoutes.DownstreamDownloadGrantPrefix, StringComparison.Ordinal))
        {
            throw GatewayAuthException.BadGateway("filestorage-transfer-url-not-proxyable");
        }

        return new FileStorageDownstreamAddress(url);
    }
}
