FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

ARG PROJECT
COPY . .
RUN dotnet restore "$PROJECT"
RUN dotnet publish "$PROJECT" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ARG DLL
ENV ASPNETCORE_URLS=http://+:8080
ENV NERV_IIP_ENTRYPOINT=$DLL
EXPOSE 8080
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*
RUN adduser --system --group --home /app appuser
# 命名卷首次创建时继承镜像内该目录的属主，先建目录才能让非 root 的 appuser 写入挂载点。
RUN mkdir -p /var/lib/nerv-iip/filestorage && chown -R appuser:appuser /var/lib/nerv-iip
COPY --from=build --chown=appuser:appuser /app/publish .
USER appuser
ENTRYPOINT ["sh", "-c", "dotnet \"$NERV_IIP_ENTRYPOINT\""]
