# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the dependency layer is cached between code changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/NexoraRent.Core/NexoraRent.Core.csproj src/NexoraRent.Core/
COPY src/NexoraRent.Infrastructure/NexoraRent.Infrastructure.csproj src/NexoraRent.Infrastructure/
COPY src/NexoraRent.Api/NexoraRent.Api.csproj src/NexoraRent.Api/
RUN dotnet restore src/NexoraRent.Api/NexoraRent.Api.csproj

COPY src/ src/
RUN dotnet publish src/NexoraRent.Api/NexoraRent.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# The base image ships a non-root "app" user.
USER app
ENTRYPOINT ["dotnet", "NexoraRent.Api.dll"]
