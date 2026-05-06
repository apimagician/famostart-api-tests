# Use the official .NET 9 runtime as the base image
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

# Copy project files
COPY ["Famostar.ApiPulse.csproj", "./"]

# Restore dependencies
RUN dotnet restore "Famostar.ApiPulse.csproj"

# Copy source code
COPY . .

# Build
RUN dotnet build "Famostar.ApiPulse.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "Famostar.ApiPulse.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final stage - Runtime
FROM base AS final
WORKDIR /app

# Copy http file from root (one level up)
COPY ../api-gateways.http ./

# Copy published app
COPY --from=publish /app/publish .

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Famostar.ApiPulse.dll"]
