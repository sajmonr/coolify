# syntax=docker/dockerfile:1

# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (cached unless the project file changes).
COPY CoolifyTest/CoolifyTest.csproj CoolifyTest/
RUN dotnet restore CoolifyTest/CoolifyTest.csproj

# Copy the rest of the source and publish.
COPY CoolifyTest/ CoolifyTest/
RUN dotnet publish CoolifyTest/CoolifyTest.csproj -c Release -o /app/publish /p:UseAppHost=false

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CoolifyTest.dll"]
