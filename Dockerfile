# syntax=docker/dockerfile:1

# Build stage. The SDK image is only needed to compile, so it is not carried into the final image.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy dependency inputs first to cache restores across source changes.
# Locked mode keeps image dependencies aligned with the reviewed lock file.
COPY cronus-delivery-service.csproj Directory.Build.props packages.lock.json ./
RUN dotnet restore cronus-delivery-service.csproj --locked-mode

# Then everything the publish needs. tests/ is excluded by .dockerignore, and the project also
# excludes it from compilation, so test code can never reach the image.
COPY . .

# UseAppHost=false drops the native launcher: the entrypoint below runs the assembly through dotnet.
RUN dotnet publish cronus-delivery-service.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish \
        -p:UseAppHost=false

# Runtime stage: the ASP.NET runtime only, with no SDK or NuGet cache.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish ./

# Listen on the port the runtime image expects. Nothing is baked in for the database, so this image
# holds no secrets.
#   ConnectionStrings__CronusDelivery   e.g. Host=...;Port=5432;Database=cronus_delivery
#
# Database:UseEntraAuthentication comes from appsettings.json, which is published into the image, and
# is true there. So a container started with only the connection string above authenticates with a
# Microsoft Entra access token and needs no password. Set it to false to use a password instead.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# The base image defines an unprivileged "app" user; the application writes nothing to disk.
USER app

ENTRYPOINT ["dotnet", "cronus-delivery-service.dll"]
