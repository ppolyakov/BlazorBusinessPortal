FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet restore src/BusinessPortal.Web/BusinessPortal.Web.csproj
RUN dotnet publish src/BusinessPortal.Web/BusinessPortal.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update \
    && apt-get install --yes --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /app/keys && chown app:app /app/keys
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
HEALTHCHECK --interval=10s --timeout=6s --start-period=20s --retries=6 \
    CMD ["dotnet", "BusinessPortal.Web.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "BusinessPortal.Web.dll"]
