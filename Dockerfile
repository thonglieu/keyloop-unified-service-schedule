FROM mcr.microsoft.com/dotnet/sdk:8.0.407 AS build
WORKDIR /src

COPY src/backend/Scheduler.Domain/Scheduler.Domain.csproj src/backend/Scheduler.Domain/
COPY src/backend/Scheduler.Application/Scheduler.Application.csproj src/backend/Scheduler.Application/
COPY src/backend/Scheduler.Infrastructure/Scheduler.Infrastructure.csproj src/backend/Scheduler.Infrastructure/
COPY src/backend/Scheduler.Api/Scheduler.Api.csproj src/backend/Scheduler.Api/
RUN dotnet restore src/backend/Scheduler.Api/Scheduler.Api.csproj

COPY src/backend/ src/backend/
RUN dotnet publish src/backend/Scheduler.Api/Scheduler.Api.csproj \
    --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
USER root
RUN apt-get update \
    && apt-get install --no-install-recommends -y curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=5s --retries=6 \
    CMD curl --fail --silent http://127.0.0.1:8080/health/ready || exit 1
ENTRYPOINT ["dotnet", "Scheduler.Api.dll"]
