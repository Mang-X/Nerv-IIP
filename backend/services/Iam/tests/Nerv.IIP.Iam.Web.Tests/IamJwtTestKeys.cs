using Microsoft.IdentityModel.Tokens;

namespace Nerv.IIP.Iam.Web.Tests;

internal static class IamJwtTestKeys
{
    public const string Kid = "dev-rsa-2026-01";
    public const string PrivateKeyPem = """
-----BEGIN PRIVATE KEY-----
MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQC0RhTT3ru98EhB
W2yY7zsawlQL/09cQPaGmUj1UyespZb/lQ7fw6WjJw05xUoUNR6RuWmvphnXRKl2
uSjIz2cQ0uiLxZgvn/9VQKj3oNt3mijuRqcCLmbQXO+dj1rQDHf1MVOxTIFnYbxq
VYw6TDX4FgkW2bz7PqP+ROXP3fF7eCuJVwbJdOU1aLT2mC8ALwuPU43jZ+i9eIuP
KCe8IDlkl6+IwVl7jeR+3GMXT5+7QjoHLqP4PKIg8Z0cAhpJafdxKXQiXa4FGaRb
5oKz0ZQsdOzRndeleSWlAJzl1yf9SwU8ZjmAhb5Nuqp8F5tkJkFE52BKdWtI2cix
1ZGCmVH1AgMBAAECggEAF6w0Q/Y1tSV+d4an5hVUL5lhLAokw7qMJPSwDfcTeKpt
/7X1NBEfCSOxquprZefr0bsFU9l9/zS3BC4gWu5RXHY1r1UNPQPHpcxN4+atqzEF
OvTwLWsmeSobFRekFznr7rjBgsDHJWpCMbx2I5mqZJ+QJf4FwQBizJsDip5cfZf7
uO9QKdJ27V8wLPlmuci/Eg/FrsAzxrPub4JrMk+C6sXLD241FE9XzfILyS/qhzOf
R6dJYo4iVCl6Q79RKsmY6Cv8nsBhQFzMhKe11oC54E75ormNueJFQdjQ1VMkLwq1
0E7/akERIjyfth7U95uOwjDf/pOAUfWwu+Z+RG2q6QKBgQDOm/qvlKnorSNodXNE
j+bp2/0OlELbYf/3lRSfm3re5UIWXFlzu3LFJLgPEpgFRD7kKabTEjJ3G8gBWPVu
0I/LnIQ2DUsTi2TLY43KLHQ7gOX4KkOEwIEpRRlImJx/XDTgWT53PHvMMIF9VPOo
cxB5J0qIWxderHld508WOsOnQwKBgQDfXm1Deuq7zvIFhid7oG130qpzN2kJlRG9
V2Vfpnwlxx8qt6RjFmsUApJ1W4bjA3jwgpuYrpsvxVT3Z57gmLfLCrLIj5lqUryd
bvg1OHWq+mvbMxFApYyBwVoDJgYUyGKRUD31RhtWBjTk9xz0tqWgM/46iClPvEPH
u2Cjh7uCZwKBgQCEe7B79jAdayhRSz7mr/+55b6XIqrcUjL4ZzgaQHDBjPCbtgwG
EiS+FZWQ1LN2bRSG6c53eiuyBLZzZr+6lzIdtfdxUYTau3+ei+/XvDmsDjNotnEl
JuurswtLadCwOkgNtCxB+R7JCDGAVIEJev8NMQyx8vdBVgddF323G2dqUQKBgDaW
TP2AvHzJRjwzXNLJkfcGdMFTeUfuNjefdBa8CPryfpth5bqRb/mj50bm5z/zSUr9
oCjgAuzZvLn5iMo6iDAGnUqGTWe+cHnI9L+M3LS8Hj+ja0PxMTVEm0rJsBLEJdJ9
WabnSybqvWJ3QYxMVo2gJzEGtZHW4HmfQS61rQ1hAoGAUKQnhO0i7rOaWEFpdDTS
kGY2MAgEEpWYfkEZGf2ybuDun3x5eQjO8QdQO68AmeifwKHBkGDhdmo2OTSHht4N
FqLC4SCdXVlfmzcW7zeCPEQptoGzHl2lGg5MMEH/4Dp92Q5jPiliv8kyVppuR9UC
yKndmINUKXFRt+mFo0HU2Ec=
-----END PRIVATE KEY-----
""";

    public static JsonWebKeySet PublicJwks()
    {
        const string jwksJson = """
{"keys":[{"kty":"RSA","use":"sig","kid":"dev-rsa-2026-01","alg":"RS256","n":"tEYU0967vfBIQVtsmO87GsJUC_9PXED2hplI9VMnrKWW_5UO38OloycNOcVKFDUekblpr6YZ10SpdrkoyM9nENLoi8WYL5__VUCo96Dbd5oo7kanAi5m0FzvnY9a0Ax39TFTsUyBZ2G8alWMOkw1-BYJFtm8-z6j_kTlz93xe3griVcGyXTlNWi09pgvAC8Lj1ON42fovXiLjygnvCA5ZJeviMFZe43kftxjF0-fu0I6By6j-DyiIPGdHAIaSWn3cSl0Il2uBRmkW-aCs9GULHTs0Z3XpXklpQCc5dcn_UsFPGY5gIW-TbqqfBebZCZBROdgSnVrSNnIsdWRgplR9Q","e":"AQAB"}]}
""";
        return new JsonWebKeySet(jwksJson);
    }
}
