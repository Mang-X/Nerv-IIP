#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
RUN adduser --system --group --home /app appuser
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "src/Nerv.IIP.Business.Wms.Web/Nerv.IIP.Business.Wms.Web.csproj"
WORKDIR "/src/src/Nerv.IIP.Business.Wms.Web"
RUN dotnet build "Nerv.IIP.Business.Wms.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Nerv.IIP.Business.Wms.Web.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish --chown=appuser:appuser /app/publish .
USER appuser
ENTRYPOINT ["dotnet", "Nerv.IIP.Business.Wms.Web.dll"]
