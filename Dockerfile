FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore first (layer caching)
COPY src/AzDoReviewAgent/AzDoReviewAgent.csproj src/AzDoReviewAgent/
COPY Directory.Build.props ./
RUN dotnet restore src/AzDoReviewAgent/AzDoReviewAgent.csproj

# Copy everything and publish
COPY . .
RUN dotnet publish src/AzDoReviewAgent/AzDoReviewAgent.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Non-root user
RUN adduser --disabled-password --gecos "" appuser
USER appuser

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "AzDoReviewAgent.dll"]
